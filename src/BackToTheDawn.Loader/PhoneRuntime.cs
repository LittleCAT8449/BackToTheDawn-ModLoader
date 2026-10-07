using BackToTheDawn.ModAPI;
using BackToTheDawn.PhoneAPI;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace BackToTheDawn.Loader;

internal static partial class PhoneRuntime
{
    private sealed record OwnedConversation(string OwnerId, PhoneConversationDefinition Definition);
    private sealed record OwnedCondition(string OwnerId, Func<PhoneCallContext, bool> Predicate);

    internal sealed record NativeDialogueTarget(
        InteractionTalk? CallerInteractionTalk,
        CharacterTalk? PlayerCharacterTalk,
        TalkWordBase? CallerTalkWord,
        TalkWordBase? PlayerTalkWord);

    private sealed class OwnedPhoneNumber
    {
        public string OwnerId { get; init; } = string.Empty;
        public string Number { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string ConversationKey { get; init; } = string.Empty;
        public bool AddToPhoneBook { get; init; }
        public int NativeId { get; set; }
        public c_phone? NativeRow { get; set; }
    }

    private sealed record OwnedLineOverride(string OwnerId, string Text);
    private sealed record ActiveCall(OwnedPhoneNumber Phone, OwnedConversation Conversation);
    private sealed record NativePhoneSignature(int NumericNumber, string DisplayName);
    private sealed record PhoneIconImageState(
        Image Image,
        Sprite? Sprite,
        Sprite? OverrideSprite,
        Image.Type Type,
        bool PreserveAspect);
    private sealed record LoadedPhoneIcon(string OwnerId, Sprite Sprite);

    private static readonly Dictionary<string, OwnedConversation> Conversations =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, OwnedCondition> Conditions =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, OwnedPhoneNumber> PhoneNumbers =
        new(StringComparer.Ordinal);
    private static readonly Dictionary<string, List<OwnedLineOverride>> LineOverrides =
        new(StringComparer.Ordinal);
    private static readonly HashSet<int> RuntimePhoneIds = new();
    private static readonly Dictionary<int, NativePhoneSignature> RuntimePhoneRows = new();

    private static ActiveCall? _activeCall;
    private static int _activeLineIndex;
    private static bool _nativePhoneCallActive;
    private static bool _nativeActionProcessSuspended;
    private static bool _resumeNativeActionProcessAfterHangUp;
    private static int _deferredNativeActionAdvanceCount;
    private static bool _nativeDialogueDisplayed;
    private static NativeDialogueTarget? _activeNativeDialogueTarget;
    private static int _nativeDialogueLineIndex;
    private static Il2CppSystem.Action? _nativeDialogueLineFinished;
    private static Il2CppSystem.Action? _nativePhoneHangUpCallback;
    private static TalkWord? _nativePlayerTalkWord;
    private static string? _nativePlayerStringKey;
    private static int _nativePlayerLinePresentedFrame = -1;
    private static int _lastNativeDialogueAdvanceFrame = -1;
    private static TalkPhone? _activeTalkPhone;
    private static bool _phoneIconHierarchyLogged;
    private static int _nextPhoneIconSearchFrame;
    private static string? _activeInteractionIconPath;
    private static readonly Dictionary<string, (int Index, PhoneDialogueLine Line)> NativeDialogueLines =
        new(StringComparer.Ordinal);
    private static readonly List<TalkString> NativeDialogueTalkStrings = new();
    private static readonly HashSet<string> NativeDialogueTextRestoreLogged = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, LoadedPhoneIcon> PhoneInteractionIcons =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> MissingPhoneInteractionIcons =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<int, PhoneIconImageState> ActivePhoneIconImages = new();
    private static readonly HashSet<int> VisiblePhoneIconImages = new();

    internal static PhoneConversationRegistrationResult RegisterConversation(
        string ownerId,
        PhoneConversationDefinition definition)
    {
        if (definition.DynamicLinesFactory is null &&
            PhoneDialogueGraph.Validate(definition.Lines) is { } graphError)
        {
            return new PhoneConversationRegistrationResult(
                definition.Key,
                PhoneConversationRegistrationStatus.InvalidDefinition,
                graphError);
        }

        if (definition.InteractionIconPath is not null &&
            string.IsNullOrWhiteSpace(definition.InteractionIconPath))
        {
            return new PhoneConversationRegistrationResult(
                definition.Key,
                PhoneConversationRegistrationStatus.InvalidDefinition,
                "The phone interaction icon path cannot be empty.");
        }

        if (Conversations.ContainsKey(definition.Key))
        {
            return new PhoneConversationRegistrationResult(
                definition.Key,
                PhoneConversationRegistrationStatus.AlreadyRegistered,
                $"Phone conversation '{definition.Key}' is already registered.");
        }

        definition = definition with { Lines = PhoneDialogueGraph.Snapshot(definition.Lines) };
        Conversations.Add(definition.Key, new OwnedConversation(ownerId, definition));
        Plugin.Logger?.LogInfo(
            definition.DynamicLinesFactory is null
                ? $"[PhoneRuntime] Registered phone conversation '{definition.Key}' " +
                  $"with {definition.Lines.Count} line(s)."
                : $"[PhoneRuntime] Registered dynamic phone conversation '{definition.Key}'.");
        return new PhoneConversationRegistrationResult(
            definition.Key,
            PhoneConversationRegistrationStatus.Succeeded,
            "Phone conversation registered.");
    }

    internal static PhoneConditionRegistrationResult RegisterCondition(
        string ownerId,
        string key,
        Func<PhoneCallContext, bool> predicate)
    {
        if (Conditions.ContainsKey(key))
        {
            return new PhoneConditionRegistrationResult(
                key,
                PhoneConditionRegistrationStatus.AlreadyRegistered,
                $"Phone condition '{key}' is already registered.");
        }

        Conditions.Add(key, new OwnedCondition(ownerId, predicate));
        Plugin.Logger?.LogInfo($"[PhoneRuntime] Registered phone condition '{key}'.");
        return new PhoneConditionRegistrationResult(
            key,
            PhoneConditionRegistrationStatus.Succeeded,
            "Phone condition registered.");
    }

    internal static bool? EvaluateCondition(
        string requestingModId,
        string key,
        PhoneCallContext context)
    {
        if (!Conditions.TryGetValue(key, out var condition))
        {
            return null;
        }

        try
        {
            return condition.Predicate(context);
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogError(
                $"[PhoneRuntime] Condition '{key}' used by '{requestingModId}' " +
                $"threw an exception: {exception}");
            return null;
        }
    }

    internal static PhoneNumberRegistrationResult RegisterNumber(
        string ownerId,
        string number,
        string displayName,
        string conversationKey,
        bool addToPhoneBook)
    {
        if (!Conversations.TryGetValue(conversationKey, out var conversation) ||
            !string.Equals(conversation.OwnerId, ownerId, StringComparison.OrdinalIgnoreCase))
        {
            return new PhoneNumberRegistrationResult(
                number,
                PhoneNumberRegistrationStatus.ConversationNotFound,
                $"Conversation '{conversationKey}' is not registered by this Mod.");
        }

        if (PhoneNumbers.ContainsKey(number))
        {
            return new PhoneNumberRegistrationResult(
                number,
                PhoneNumberRegistrationStatus.DuplicateNumber,
                $"Phone number '{number}' is already registered.");
        }

        if (IsNativeNumberInUse(number))
        {
            return new PhoneNumberRegistrationResult(
                number,
                PhoneNumberRegistrationStatus.DuplicateNumber,
                $"Phone number '{number}' is already used by the game.");
        }

        PhoneNumbers.Add(number, new OwnedPhoneNumber
        {
            OwnerId = ownerId,
            Number = number,
            DisplayName = displayName,
            ConversationKey = conversationKey,
            AddToPhoneBook = addToPhoneBook,
        });

        RefreshConfigPhoneRows();
        Plugin.Logger?.LogInfo(
            $"[PhoneRuntime] Registered number {number} for '{displayName}'.");
        return new PhoneNumberRegistrationResult(
            number,
            PhoneNumberRegistrationStatus.Succeeded,
            "Phone number registered for this runtime.");
    }

    internal static PhoneLineOverrideResult OverrideLine(
        string ownerId,
        string stringKey,
        string replacementText)
    {
        if (!LineOverrides.TryGetValue(stringKey, out var overrides))
        {
            overrides = new List<OwnedLineOverride>();
            LineOverrides.Add(stringKey, overrides);
        }

        overrides.Add(new OwnedLineOverride(ownerId, replacementText));
        return new PhoneLineOverrideResult(
            stringKey,
            PhoneLineOverrideStatus.Succeeded,
            $"A replacement was registered for native dialogue line '{stringKey}'.");
    }

    internal static bool TryGetNumber(string number, out string displayName)
    {
        if (PhoneNumbers.TryGetValue(number, out var phone))
        {
            displayName = phone.DisplayName;
            return true;
        }

        displayName = string.Empty;
        return false;
    }

    internal static bool TryGetPhoneBookEntry(int id, out (string Number, string Name) entry)
    {
        var phone = PhoneNumbers.Values.FirstOrDefault(value => value.NativeId == id);
        if (phone is not null)
        {
            entry = (phone.Number, phone.DisplayName);
            return true;
        }

        entry = default;
        return false;
    }

    internal static IReadOnlyList<c_phone> GetPhoneBookRows()
    {
        RefreshConfigPhoneRows();
        return PhoneNumbers.Values
            .Where(phone => phone.AddToPhoneBook && phone.NativeRow is not null)
            .Select(phone => phone.NativeRow!)
            .ToArray();
    }

    internal static bool TryOverrideLine(string stringKey, out string replacementText)
    {
        if (LineOverrides.TryGetValue(stringKey, out var overrides) && overrides.Count > 0)
        {
            replacementText = overrides[^1].Text;
            return true;
        }

        replacementText = string.Empty;
        return false;
    }

    internal static bool BeginCall(string number)
    {
        if (_activeCall is not null ||
            !PhoneNumbers.TryGetValue(number, out var phone) ||
            !Conversations.TryGetValue(phone.ConversationKey, out var conversation))
        {
            return false;
        }

        try
        {
            var definition = conversation.Definition;
            var lines = definition.DynamicLinesFactory is { } lineFactory
                ? lineFactory(new PhoneCallContext(phone.Number, phone.DisplayName, GameContext.Time))?.ToArray()
                : definition.Lines.ToArray();
            if (lines is null)
            {
                Plugin.Logger?.LogError(
                    $"[PhoneRuntime] Dynamic conversation '{definition.Key}' returned no lines for call '{number}'.");
                return false;
            }

            if (PhoneDialogueGraph.Validate(lines) is { } graphError)
            {
                Plugin.Logger?.LogError(
                    $"[PhoneRuntime] Dynamic conversation '{definition.Key}' returned an invalid dialogue graph: {graphError}");
                return false;
            }

            conversation = conversation with
            {
                Definition = definition with
                {
                    Lines = PhoneDialogueGraph.Snapshot(lines),
                    DynamicLinesFactory = null,
                },
            };
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogError(
                $"[PhoneRuntime] Creating dialogue for call '{number}' failed; call was not started: {exception}");
            return false;
        }

        var phoneInfo = GameProcess.singleton?.phoneInfo;
        if (phoneInfo is null)
        {
            Plugin.Logger?.LogWarning(
                $"[PhoneRuntime] Cannot start custom call '{number}': native phone storage is unavailable.");
            return false;
        }

        try
        {
            var before = phoneInfo.callTimesToday;
            phoneInfo.AddCallTimes();
            var after = phoneInfo.callTimesToday;
            Plugin.Logger?.LogInfo(
                $"[PhoneRuntime] Recorded custom call '{number}' in native phone storage: " +
                $"callsToday={before}->{after}.");
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogWarning(
                $"[PhoneRuntime] Cannot record custom call '{number}' in native phone storage: {exception.Message}");
            return false;
        }

        RestorePhoneInteractionIcon();
        ClearPhoneOptions();
        _activeTalkPhone = null;
        _phoneIconHierarchyLogged = false;
        _nextPhoneIconSearchFrame = 0;
        _activeInteractionIconPath = null;
        _activeCall = new ActiveCall(phone, conversation);
        _nativeActionProcessSuspended = false;
        _resumeNativeActionProcessAfterHangUp = false;
        _deferredNativeActionAdvanceCount = 0;
        _activeLineIndex = 0;
        _nativeDialogueDisplayed = false;
        _activeNativeDialogueTarget = null;
        _nativeDialogueLineIndex = 0;
        _nativeDialogueLineFinished = null;
        _nativePlayerTalkWord = null;
        _nativePlayerStringKey = null;
        _nativePlayerLinePresentedFrame = -1;
        _lastNativeDialogueAdvanceFrame = -1;
        NativeDialogueLines.Clear();
        NativeDialogueTalkStrings.Clear();
        NativeDialogueTextRestoreLogged.Clear();
        RaiseActiveLine();
        return true;
    }

    internal static bool ResumeCurrentCall(string ownerId, string number, string conversationKey)
    {
        if (_activeCall is not { } call ||
            !string.Equals(call.Phone.OwnerId, ownerId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(call.Phone.Number, number, StringComparison.Ordinal) ||
            !string.Equals(call.Conversation.Definition.Key, conversationKey, StringComparison.OrdinalIgnoreCase) ||
            _waitingForPhoneOption)
        {
            return false;
        }

        if (CurrentLine is { Options.Count: > 0 })
        {
            _nativeDialogueLineIndex = _activeLineIndex;
            _lastNativeDialogueAdvanceFrame = Time.frameCount;
            ShowPhoneOptions();
            KeepNativePhoneIconVisible();
        }
        else if (_activeNativeDialogueTarget is not null &&
            _activeLineIndex >= 0 && _activeLineIndex < NativeDialogueTalkStrings.Count)
        {
            _nativeDialogueDisplayed = true;
            _nativeDialogueLineIndex = _activeLineIndex;
            _lastNativeDialogueAdvanceFrame = Time.frameCount;
            PresentNativeDialogueLine();
            KeepNativePhoneIconVisible();
        }
        else
        {
            _nativeDialogueDisplayed = false;
            RaiseActiveLine();
        }

        Plugin.Logger?.LogInfo(
            $"[PhoneRuntime] Restored the current phone interaction for '{number}' after an external UI closed.");
        return true;
    }

    internal static bool HasActiveCall => _activeCall is not null;

    internal static bool ShouldDeferNativeActionProgress =>
        _nativeActionProcessSuspended && (_activeCall is not null || _resumeNativeActionProcessAfterHangUp);

    internal static void SuspendNativeActionProcessForShop()
    {
        if (_activeCall is null || _nativeActionProcessSuspended)
        {
            return;
        }

        _nativeActionProcessSuspended = true;
        Plugin.Logger?.LogInfo(
            "[PhoneRuntime] Suspended native ActionBase progression for the shop opened from this phone call.");
    }

    internal static void DeferNativeActionProgress()
    {
        _deferredNativeActionAdvanceCount++;
        Plugin.Logger?.LogInfo(
            $"[PhoneRuntime] Deferred ActionBase.DoNextProcess during the phone shop handoff " +
            $"(pending={_deferredNativeActionAdvanceCount}).");
    }

    internal static bool ShouldSuppressNativeHangUp => _activeCall is not null;

    internal static bool ShouldPreserveNativeTalkPhone(TalkPhone phone)
    {
        if (_activeCall is null || phone is null)
        {
            return false;
        }

        try
        {
            var activePhone = _activeNativeDialogueTarget?.CallerInteractionTalk?.talkPhone ?? _activeTalkPhone;
            return activePhone is null || activePhone.GetInstanceID() == phone.GetInstanceID();
        }
        catch (Exception exception)
        {
            Plugin.DebugLog(
                $"[PhoneRuntime] Could not compare the active TalkPhone before hang-up: {exception.Message}");
            return true;
        }
    }

    internal static bool ShouldPreserveNativeTalkPhoneInteraction(InteractionTalk interaction)
    {
        if (_activeCall is null || interaction is null)
        {
            return false;
        }

        try
        {
            var activeInteraction = _activeNativeDialogueTarget?.CallerInteractionTalk;
            var activePhone = activeInteraction?.talkPhone ?? _activeTalkPhone;
            var interactionPhone = interaction.talkPhone;
            return activePhone is not null && interactionPhone is not null &&
                   activePhone.GetInstanceID() == interactionPhone.GetInstanceID();
        }
        catch (Exception exception)
        {
            Plugin.DebugLog(
                $"[PhoneRuntime] Could not compare the active InteractionTalk before phone cleanup: {exception.Message}");
            return false;
        }
    }

    internal static string ActiveDisplayName => _activeCall?.Phone.DisplayName ?? string.Empty;

    internal static string ActiveSpeaker => CurrentLine?.Speaker ?? string.Empty;

    internal static string ActiveText => CurrentLine?.Text ?? string.Empty;

    internal static bool IsLastLine => _activeCall is null ||
        (CurrentLine!.Options.Count == 0 &&
         PhoneDialogueGraph.Next(_activeCall.Conversation.Definition.Lines, _activeLineIndex) < 0);

    internal static bool NativeDialogueDisplayed => _nativeDialogueDisplayed;

    internal static bool TryGetNativeDialogueText(string stringKey, out string text)
    {
        if (NativeDialogueLines.TryGetValue(stringKey, out var customLine))
        {
            text = FormatNativeDialogueText(customLine.Line);
            return true;
        }

        text = string.Empty;
        return false;
    }

    internal static void ForceNativeDialogueText(TalkWord talkWord, ref string text)
    {
        try
        {
            var talkString = talkWord.talkString ?? talkWord.talkWordBase?.currentTalkString;
            var stringKey = talkString?.stringKey;
            if (string.IsNullOrEmpty(stringKey) ||
                !TryGetNativeDialogueText(stringKey, out var registeredText))
            {
                return;
            }

            if (!string.Equals(text, registeredText, StringComparison.Ordinal))
            {
                Plugin.Logger?.LogInfo(
                    $"[PhoneRuntime] Replaced typewriter text for '{stringKey}' " +
                    $"(received='{text}', registered='{registeredText}').");
            }

            text = registeredText;
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogWarning(
                $"[PhoneRuntime] Could not map native typewriter text to its registered line: {exception.Message}");
        }
    }

    internal static void EnsureNativeDialogueText(TalkWord talkWord, TalkString talkString)
    {
        if (talkString is null)
        {
            return;
        }

        try
        {
            var stringKey = talkString.stringKey;
            if (string.IsNullOrEmpty(stringKey) ||
                !TryGetNativeDialogueText(stringKey, out var registeredText))
            {
                return;
            }

            var contentText = talkWord.contentText;
            if (contentText is null)
            {
                Plugin.Logger?.LogWarning(
                    $"[PhoneRuntime] TalkWord has no contentText component for '{stringKey}'.");
                return;
            }

            var previousText = contentText.text;
            contentText.text = registeredText;
            if (NativeDialogueLines.TryGetValue(stringKey, out var nativeLine))
            {
                if (nativeLine.Line.SpeakerType == PhoneDialogueSpeakerType.Player)
                {
                    _nativePlayerTalkWord = talkWord;
                    _nativePlayerStringKey = stringKey;
                }
            }

            Plugin.Logger?.LogInfo(
                $"[PhoneRuntime] Applied custom line to TalkWord.contentText " +
                $"(key='{stringKey}', object='{contentText.gameObject.name}', " +
                $"active={contentText.gameObject.activeInHierarchy}, enabled={contentText.enabled}, " +
                $"previous='{previousText}', text='{contentText.text}').");
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogWarning(
                $"[PhoneRuntime] Could not write registered text to TalkWord.contentText: {exception.Message}");
        }
    }

    internal static NativeDialogueTarget? FindNativeDialogueTarget()
    {
        try
        {
            var interactionTalk = CharacterManage.interaction?.interactionTalk;
            var interactionTalkWord = interactionTalk?.GetCurrentTalkWordBase();
            interactionTalk ??= CharacterManage.faceInteraction?.interactionTalk;
            interactionTalkWord ??= interactionTalk?.GetCurrentTalkWordBase();

            var character = CharacterManage.currentControlCharacter ?? CharacterManage.protagonist;
            var characterTalk = character?.characterTalk;
            var characterTalkWord = characterTalk?.GetCurrentTalkWord() ?? characterTalk?.talkWord;

            if (interactionTalk is null && characterTalk is null)
            {
                Plugin.Logger?.LogWarning("[PhoneRuntime] No native dialogue presenters were available for the call.");
            }
            else
            {
                Plugin.Logger?.LogInfo(
                    $"[PhoneRuntime] Native presenters found: caller={(interactionTalk is not null)}, " +
                    $"player={(characterTalk is not null)}.");
            }

            return interactionTalkWord is null && characterTalkWord is null
                ? null
                : new NativeDialogueTarget(
                    interactionTalk,
                    characterTalk,
                    interactionTalkWord,
                    characterTalkWord);
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogWarning(
                $"[PhoneRuntime] Could not find the game's native dialogue component: {exception.Message}");
            return null;
        }
    }

    internal static bool TryShowNativeDialogue(NativeDialogueTarget? target)
    {
        if (_activeCall is null || target is null)
        {
            if (target is null)
            {
                Plugin.Logger?.LogWarning("[PhoneRuntime] Native dialogue target is missing; using the Loader panel.");
            }

            return false;
        }

        try
        {
            var nativeLines = new Il2CppSystem.Collections.Generic.List<TalkString>();
            var call = _activeCall;
            var callerRole = target.CallerTalkWord?.GetCurrentTalkType() ??
                             target.PlayerTalkWord?.GetCurrentTalkType() ?? 0;
            var playerRole = target.PlayerTalkWord?.GetCurrentTalkType() ?? callerRole;
            var playerCharacterId = CharacterManage.protagonistAttribute?.id ?? 0;
            var keyPrefix = $"mod-phone:{call.Phone.Number}:{Guid.NewGuid():N}";
            for (var index = 0; index < call.Conversation.Definition.Lines.Count; index++)
            {
                var line = call.Conversation.Definition.Lines[index];
                var stringKey = $"{keyPrefix}:{index}";
                var content = FormatNativeDialogueText(line);
                var role = line.SpeakerType == PhoneDialogueSpeakerType.Player
                    ? playerRole
                    : callerRole;
                var dialogBox = line.SpeakerType == PhoneDialogueSpeakerType.Player
                    ? DialogBoxType.Normal
                    : DialogBoxType.Phone;
                var nativeLine = new TalkString(
                    role,
                    content,
                    dialogBox,
                    movie_mode: 0,
                    stringKey,
                    talkOrder: index);
                nativeLine.SetCanClickContinue(true);
                nativeLine.SetDisplayType(TextDisplayType.SentenceBySentenceClick);
                nativeLines.Add(nativeLine);
                NativeDialogueLines[stringKey] = (index, line);
                NativeDialogueTalkStrings.Add(nativeLine);
            }

            _activeNativeDialogueTarget = target;
            _activeInteractionIconPath = call.Conversation.Definition.InteractionIconPath;
            _nativeDialogueLineIndex = 0;
            _nativeDialogueDisplayed = true;
            Plugin.Logger?.LogInfo(
                $"[PhoneRuntime] Starting native lines for '{call.Phone.DisplayName}' " +
                $"(callerTalkType={callerRole}, playerTalkType={playerRole}, " +
                $"playerCharacterId={playerCharacterId}, lines={nativeLines.Count}).");
            PresentNativeDialogueLine();
            KeepNativePhoneIconVisible();
            Plugin.Logger?.LogInfo(
                $"[PhoneRuntime] Started '{call.Phone.DisplayName}' in speaker-specific native dialogue UIs.");
            return true;
        }
        catch (Exception exception)
        {
            NativeDialogueLines.Clear();
            NativeDialogueTalkStrings.Clear();
            _activeNativeDialogueTarget = null;
            _nativeDialogueLineFinished = null;
            _nativeDialogueDisplayed = false;
            Plugin.Logger?.LogWarning(
                $"[PhoneRuntime] Native dialogue UI failed; using the Loader panel instead: {exception}");
            return false;
        }
    }

    internal static void AdvanceCall()
    {
        if (_activeCall is null || _waitingForPhoneOption)
        {
            return;
        }

        if (_nativeDialogueDisplayed)
        {
            AdvanceNativeDialogueLine();
            return;
        }
        CompletePhoneDialogueLine();
    }

    internal static void EndCall()
    {
        var shouldHangUpPhone = _activeCall is not null;
        _resumeNativeActionProcessAfterHangUp |= shouldHangUpPhone && _nativeActionProcessSuspended;
        var dialogueTarget = _activeNativeDialogueTarget;
        _nativeLineGeneration++;
        ClearPhoneOptions();
        RestorePhoneInteractionIcon();
        CloseActivePlayerDialogue();
        _activeCall = null;
        _nativeDialogueDisplayed = false;
        _activeNativeDialogueTarget = null;
        _nativeDialogueLineIndex = 0;
        _nativeDialogueLineFinished = null;
        _nativePlayerTalkWord = null;
        _nativePlayerStringKey = null;
        _activeTalkPhone = null;
        _phoneIconHierarchyLogged = false;
        _nextPhoneIconSearchFrame = 0;
        _activeInteractionIconPath = null;
        _nativePlayerLinePresentedFrame = -1;
        NativeDialogueLines.Clear();
        NativeDialogueTalkStrings.Clear();
        NativeDialogueTextRestoreLogged.Clear();
        ReleaseNativeDialoguePresenters(dialogueTarget);

        if (!shouldHangUpPhone)
        {
            _nativeActionProcessSuspended = false;
            _resumeNativeActionProcessAfterHangUp = false;
            _deferredNativeActionAdvanceCount = 0;
            return;
        }

        try
        {
            _nativePhoneHangUpCallback =
                DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(CompleteNativePhoneHangUp);
            ActionPhone.PlayPhoneEndAnimationAndCallBack(_nativePhoneHangUpCallback);
            Plugin.Logger?.LogInfo(
                "[PhoneRuntime] Started native phone end animation after custom dialogue.");
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogWarning(
                $"[PhoneRuntime] Native phone end animation failed; falling back to direct hang-up: {exception.Message}");
            CompleteNativePhoneHangUp();
        }
    }

    private static void ReleaseNativeDialoguePresenters(NativeDialogueTarget? target)
    {
        if (target?.PlayerCharacterTalk is { } playerTalk)
        {
            try
            {
                playerTalk.EndTalk();
                Plugin.Logger?.LogInfo(
                    "[PhoneRuntime] Called CharacterTalk.EndTalk to restore player controls.");
            }
            catch (Exception exception)
            {
                Plugin.Logger?.LogWarning(
                    $"[PhoneRuntime] CharacterTalk.EndTalk failed during call cleanup: {exception.Message}");
            }
        }

        if (target?.CallerInteractionTalk is { } callerTalk)
        {
            try
            {
                callerTalk.UnShowWord();
                Plugin.Logger?.LogInfo(
                    "[PhoneRuntime] Called InteractionTalk.UnShowWord to release caller dialogue state.");
            }
            catch (Exception exception)
            {
                Plugin.Logger?.LogWarning(
                    $"[PhoneRuntime] InteractionTalk.UnShowWord failed during call cleanup: {exception.Message}");
            }
        }
    }

    private static void CompleteNativePhoneHangUp()
    {
        var shouldResumeActionProcess = _resumeNativeActionProcessAfterHangUp;
        try
        {
            ActionPhone.HangUpPhone();
            _nativePhoneCallActive = false;
            Plugin.Logger?.LogInfo(
                "[PhoneRuntime] Native phone end callback ran; HangUpPhone was invoked.");
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogWarning(
                $"[PhoneRuntime] Native HangUpPhone failed after end animation: {exception.Message}");
        }
        finally
        {
            _nativePhoneHangUpCallback = null;
        }

        var deferredAdvanceCount = _deferredNativeActionAdvanceCount;
        _nativeActionProcessSuspended = false;
        _resumeNativeActionProcessAfterHangUp = false;
        _deferredNativeActionAdvanceCount = 0;
        if (!shouldResumeActionProcess || deferredAdvanceCount == 0)
        {
            return;
        }

        for (var index = 0; index < deferredAdvanceCount; index++)
        {
            try
            {
                ActionBase.DoNextProcess();
                Plugin.Logger?.LogInfo(
                    $"[PhoneRuntime] Resumed deferred ActionBase progression after hang-up " +
                    $"({index + 1}/{deferredAdvanceCount}).");
            }
            catch (Exception exception)
            {
                Plugin.Logger?.LogWarning(
                    $"[PhoneRuntime] Could not resume deferred ActionBase progression after hang-up: {exception.Message}");
                break;
            }
        }
    }

    internal static void KeepNativePlayerDialogueTextVisible()
    {
        if (!_nativeDialogueDisplayed || _activeCall is null || _waitingForPhoneOption ||
            _nativePlayerTalkWord is null || string.IsNullOrEmpty(_nativePlayerStringKey) ||
            !NativeDialogueLines.TryGetValue(_nativePlayerStringKey, out var nativeLine) ||
            nativeLine.Line.SpeakerType != PhoneDialogueSpeakerType.Player)
        {
            return;
        }

        try
        {
            var registeredText = FormatNativeDialogueText(nativeLine.Line);
            var contentText = _nativePlayerTalkWord.contentText;
            if (contentText is null || string.Equals(contentText.text, registeredText, StringComparison.Ordinal))
            {
                return;
            }

            contentText.text = registeredText;
            if (NativeDialogueTextRestoreLogged.Add(_nativePlayerStringKey))
            {
                Plugin.Logger?.LogInfo(
                    $"[PhoneRuntime] Restored player dialogue text after a native UI update " +
                    $"(key='{_nativePlayerStringKey}', object='{contentText.gameObject.name}', " +
                    $"text='{contentText.text}').");
            }
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogWarning(
                $"[PhoneRuntime] Could not keep the registered player dialogue visible: {exception.Message}");
        }
    }

    internal static void TryAdvancePlayerDialogueFromInput()
    {
        if (!_nativeDialogueDisplayed || _activeCall is null || _waitingForPhoneOption ||
            CurrentLine is not { SpeakerType: PhoneDialogueSpeakerType.Player } ||
            Time.frameCount == _nativePlayerLinePresentedFrame ||
            Time.frameCount == _lastNativeDialogueAdvanceFrame)
        {
            return;
        }

        var input = Input.GetKeyDown(KeyCode.Space) ||
                    Input.GetKeyDown(KeyCode.Return) ||
                    Input.GetKeyDown(KeyCode.KeypadEnter)
            ? "keyboard"
            : Input.GetMouseButtonDown(0)
                ? "mouse"
                : null;
        if (input is null)
        {
            return;
        }

        Plugin.Logger?.LogInfo(
            $"[PhoneRuntime] Advancing player dialogue line {_nativeDialogueLineIndex + 1} " +
            $"from {input} input fallback.");
        AdvanceNativeDialogueLine();
    }

    private static void AdvanceNativeDialogueLine()
    {
        if (_activeCall is null || _waitingForPhoneOption)
        {
            return;
        }

        var currentFrame = Time.frameCount;
        if (_lastNativeDialogueAdvanceFrame == currentFrame)
        {
            return;
        }

        _lastNativeDialogueAdvanceFrame = currentFrame;

        CompletePhoneDialogueLine();
    }

    private static void PresentNativeDialogueLine()
    {
        CloseActivePlayerDialogue();

        if (_activeCall is null ||
            _activeNativeDialogueTarget is not { } target ||
            _nativeDialogueLineIndex < 0 ||
            _nativeDialogueLineIndex >= NativeDialogueTalkStrings.Count)
        {
            _nativeDialogueDisplayed = false;
            return;
        }

        var line = _activeCall.Conversation.Definition.Lines[_nativeDialogueLineIndex];
        var call = _activeCall;
        var presentedIndex = _nativeDialogueLineIndex;
        var generation = ++_nativeLineGeneration;
        _nativeDialogueLineFinished = DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(() =>
        {
            if (ReferenceEquals(_activeCall, call) && generation == _nativeLineGeneration &&
                presentedIndex == _nativeDialogueLineIndex && !_waitingForPhoneOption)
            {
                AdvanceNativeDialogueLine();
            }
        });
        _nativePlayerLinePresentedFrame = line.SpeakerType == PhoneDialogueSpeakerType.Player
            ? Time.frameCount
            : -1;
        var nativeLines = new Il2CppSystem.Collections.Generic.List<TalkString>();
        nativeLines.Add(NativeDialogueTalkStrings[_nativeDialogueLineIndex]);
        try
        {
            if (line.SpeakerType == PhoneDialogueSpeakerType.Player)
            {
                if (target.PlayerCharacterTalk is null)
                {
                    throw new InvalidOperationException("The protagonist CharacterTalk presenter is unavailable.");
                }

                Plugin.Logger?.LogInfo(
                    $"[PhoneRuntime] Routing player line {_nativeDialogueLineIndex + 1} through CharacterTalk.SayWord " +
                    "with DialogBoxType.Normal.");
                target.PlayerCharacterTalk.SayWord(nativeLines, _nativeDialogueLineFinished, 0f, canClick: true);
                return;
            }

            if (target.CallerInteractionTalk is not null)
            {
                Plugin.Logger?.LogInfo(
                    $"[PhoneRuntime] Routing caller line {_nativeDialogueLineIndex + 1} through InteractionTalk.SayWord " +
                    "with DialogBoxType.Phone.");
                target.CallerInteractionTalk.SayWord(nativeLines, _nativeDialogueLineFinished, 0f, canClick: true);
                return;
            }

            if (target.CallerTalkWord is not null)
            {
                Plugin.Logger?.LogInfo(
                    $"[PhoneRuntime] Routing caller line {_nativeDialogueLineIndex + 1} through TalkWordBase.Say " +
                    "with DialogBoxType.Phone.");
                target.CallerTalkWord.Say(
                    nativeLines,
                    _nativeDialogueLineFinished,
                    canClick: true,
                    TextDisplayType.SentenceBySentenceClick);
                return;
            }

            throw new InvalidOperationException("The caller dialogue presenter is unavailable.");
        }
        catch (Exception exception)
        {
            _nativeDialogueDisplayed = false;
            _nativeDialogueLineFinished = null;
            Plugin.Logger?.LogWarning(
                $"[PhoneRuntime] Could not present line {_nativeDialogueLineIndex + 1} " +
                $"through its speaker UI; using the Loader panel instead: {exception}");
        }
    }

    internal static void NativePhoneCallStarted() => _nativePhoneCallActive = true;

    internal static void NativePhoneCallEnded() => _nativePhoneCallActive = false;

    internal static void ObserveNativeLine(TalkString line)
    {
        if (line is null)
        {
            return;
        }

        string? stringKey;
        string text;
        try
        {
            stringKey = line.stringKey;
            text = line.content;
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogWarning($"[PhoneRuntime] Could not inspect a native call line: {exception.Message}");
            return;
        }

        if (!string.IsNullOrEmpty(stringKey) &&
            NativeDialogueLines.TryGetValue(stringKey, out var customLine))
        {
            var registeredText = FormatNativeDialogueText(customLine.Line);
            if (!string.Equals(text, registeredText, StringComparison.Ordinal))
            {
                line.content = registeredText;
                Plugin.Logger?.LogWarning(
                    $"[PhoneRuntime] Corrected native line {customLine.Index + 1}: " +
                    $"received='{text}', registered='{registeredText}'.");
            }
            else
            {
                Plugin.Logger?.LogInfo(
                    $"[PhoneRuntime] Native line {customLine.Index + 1} matches registered text: '{text}'.");
            }

            if (customLine.Index > 0 && _activeCall is not null)
            {
                PhoneApi.PublishLineDisplayed(new PhoneLineDisplayedEvent(
                    _activeCall.Phone.Number,
                    _activeCall.Conversation.Definition.Key,
                    stringKey,
                    SpeakerId: -1,
                    customLine.Line.Text,
                    IsCustomConversation: true));
            }

        return;
    }

        if (!_nativePhoneCallActive)
        {
            return;
        }

        PhoneApi.PublishLineDisplayed(new PhoneLineDisplayedEvent(
            null,
            null,
            stringKey,
            line.role,
            text,
            IsCustomConversation: false));
    }

    internal static void UnregisterMod(string ownerId)
    {
        foreach (var key in Conditions
                     .Where(pair => string.Equals(pair.Value.OwnerId, ownerId, StringComparison.OrdinalIgnoreCase))
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            Conditions.Remove(key);
        }

        var removedPhones = PhoneNumbers.Values
            .Where(phone => string.Equals(phone.OwnerId, ownerId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        foreach (var phone in removedPhones)
        {
            PhoneNumbers.Remove(phone.Number);
        }

        foreach (var key in Conversations
                     .Where(pair => string.Equals(pair.Value.OwnerId, ownerId, StringComparison.OrdinalIgnoreCase))
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            Conversations.Remove(key);
        }

        foreach (var key in LineOverrides.Keys.ToArray())
        {
            LineOverrides[key].RemoveAll(value =>
                string.Equals(value.OwnerId, ownerId, StringComparison.OrdinalIgnoreCase));
            if (LineOverrides[key].Count == 0)
            {
                LineOverrides.Remove(key);
            }
        }

        if (_activeCall is not null &&
            string.Equals(_activeCall.Phone.OwnerId, ownerId, StringComparison.OrdinalIgnoreCase))
        {
            EndCall();
        }

        RemovePhoneInteractionIcons(ownerId);

        RefreshConfigPhoneRows();
    }

    internal static void Reset()
    {
        RemoveRuntimeRowsIfAvailable();
        PhoneNumbers.Clear();
        Conversations.Clear();
        Conditions.Clear();
        LineOverrides.Clear();
        EndCall();
        RemovePhoneInteractionIcons(ownerId: null);
        _nativePhoneCallActive = false;
    }

    internal static bool RefreshConfigPhoneRows()
    {
        try
        {
            var config = ConfigData.singleton;
            if (config is null)
            {
                return false;
            }

            var phones = config.phone;
            if (phones is null)
            {
                phones = new Il2CppSystem.Collections.Generic.List<c_phone>();
                config.phone = phones;
            }

            var phoneById = ConfigData.dict_phone;
            if (phoneById is null)
            {
                phoneById = new Il2CppSystem.Collections.Generic.Dictionary<int, c_phone>();
                ConfigData.dict_phone = phoneById;
            }

            RemoveExistingRuntimeRows(phones, phoneById);
            var nativeNumberConflicts = new List<OwnedPhoneNumber>();
            foreach (var phone in PhoneNumbers.Values)
            {
                if (ContainsPhoneNumber(phones, int.Parse(phone.Number)))
                {
                    nativeNumberConflicts.Add(phone);
                }
            }

            foreach (var phone in nativeNumberConflicts)
            {
                PhoneNumbers.Remove(phone.Number);
                Plugin.Logger?.LogWarning(
                    $"[PhoneRuntime] Removed custom number {phone.Number}; the game already uses it.");
            }

            foreach (var phone in PhoneNumbers.Values
                         .OrderBy(value => value.OwnerId, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(value => value.Number, StringComparer.Ordinal))
            {
                var id = FindNextPhoneId(phones, phoneById);
                var row = new c_phone
                {
                    phone_id = id,
                    phone_number = int.Parse(phone.Number),
                    lead_id = 0,
                    base_number = false,
                    L_phone_name = phone.DisplayName,
                };
                phones.Add(row);
                phoneById.Add(id, row);
                phone.NativeId = id;
                phone.NativeRow = row;
                RuntimePhoneIds.Add(id);
                RuntimePhoneRows[id] = new NativePhoneSignature(
                    int.Parse(phone.Number),
                    phone.DisplayName);
            }

            return true;
        }
        catch (Exception exception)
        {
            Plugin.DebugLog(
                $"[PhoneRuntime] Phone table is not ready yet; registrations remain queued. {exception.Message}");
            return false;
        }
    }

    private static PhoneDialogueLine? CurrentLine =>
        _activeCall is null
            ? null
            : _activeCall.Conversation.Definition.Lines[_activeLineIndex];

    private static void RaiseActiveLine()
    {
        if (_activeCall is null || CurrentLine is not { } line)
        {
            return;
        }

        PhoneApi.PublishLineDisplayed(new PhoneLineDisplayedEvent(
            _activeCall.Phone.Number,
            _activeCall.Conversation.Definition.Key,
            null,
            SpeakerId: -1,
        line.Text,
        IsCustomConversation: true));
    }

    private static string FormatNativeDialogueText(PhoneDialogueLine line) => line.Text;

    private static void RemoveRuntimeRowsIfAvailable()
    {
        try
        {
            var config = ConfigData.singleton;
            if (config is null)
            {
                return;
            }

            RemoveExistingRuntimeRows(config.phone, ConfigData.dict_phone);
        }
        catch (Exception exception)
        {
            Plugin.DebugLog(
                $"[PhoneRuntime] Could not remove runtime phone rows during reset: {exception.Message}");
        }
    }

    private static void RemoveExistingRuntimeRows(
        Il2CppSystem.Collections.Generic.List<c_phone> phones,
        Il2CppSystem.Collections.Generic.Dictionary<int, c_phone> phoneById)
    {
        if (RuntimePhoneIds.Count == 0)
        {
            return;
        }

        for (var index = phones.Count - 1; index >= 0; index--)
        {
            var row = phones[index];
            if (row is not null && IsOwnedRow(row))
            {
                phones.RemoveAt(index);
            }
        }

        foreach (var id in RuntimePhoneIds.ToArray())
        {
            if (phoneById.TryGetValue(id, out var row) && row is not null && IsOwnedRow(row))
            {
                phoneById.Remove(id);
            }
        }

        RuntimePhoneIds.Clear();
        RuntimePhoneRows.Clear();

        bool IsOwnedRow(c_phone row) =>
            RuntimePhoneRows.TryGetValue(row.phone_id, out var signature) &&
            row.phone_number == signature.NumericNumber &&
            string.Equals(row.L_phone_name, signature.DisplayName, StringComparison.Ordinal);
    }

    private static int FindNextPhoneId(
        Il2CppSystem.Collections.Generic.List<c_phone> phones,
        Il2CppSystem.Collections.Generic.Dictionary<int, c_phone> phoneById)
    {
        var candidate = 1;
        for (var index = 0; index < phones.Count; index++)
        {
            var row = phones[index];
            if (row is not null)
            {
                candidate = Math.Max(candidate, row.phone_id + 1);
            }
        }

        while (phoneById.ContainsKey(candidate))
        {
            candidate++;
        }

        return candidate;
    }

    private static bool IsNativeNumberInUse(string number)
    {
        try
        {
            var config = ConfigData.singleton;
            if (config is null || config.phone is null)
            {
                return false;
            }

            return ContainsPhoneNumber(config.phone, int.Parse(number));
        }
        catch
        {
            // Before ConfigData finishes loading there is no native table to check.
        }

        return false;
    }

    private static bool ContainsPhoneNumber(
        Il2CppSystem.Collections.Generic.List<c_phone> phones,
        int numericNumber)
    {
        for (var index = 0; index < phones.Count; index++)
        {
            var row = phones[index];
            if (row is not null && row.phone_number == numericNumber)
            {
                return true;
            }
        }

        return false;
    }

    internal static void KeepNativePhoneIconVisible()
    {
        if (_activeCall is null || string.IsNullOrWhiteSpace(_activeInteractionIconPath))
        {
            return;
        }

        try
        {
            // TalkImageTips is hidden during calls. TalkPhone owns the visible caller picture.
            var phone = _activeNativeDialogueTarget?.CallerInteractionTalk?.talkPhone;
            if (phone is null || !phone.gameObject.activeInHierarchy)
            {
                phone = _activeTalkPhone;
                if (phone is null || !phone.gameObject.activeInHierarchy)
                {
                    if (Time.frameCount < _nextPhoneIconSearchFrame)
                    {
                        return;
                    }

                    _nextPhoneIconSearchFrame = Time.frameCount + 30;
                    var visiblePhones = UnityEngine.Object.FindObjectsOfType<TalkPhone>()
                        .Where(candidate => candidate is not null && candidate.gameObject.activeInHierarchy)
                        .ToArray();
                    if (visiblePhones.Length != 1)
                    {
                        return;
                    }

                    phone = visiblePhones[0];
                }
            }

            if (_activeTalkPhone?.GetInstanceID() != phone.GetInstanceID())
            {
                _phoneIconHierarchyLogged = false;
            }

            _activeTalkPhone = phone;
            if (!TryGetPhoneInteractionIcon(
                    _activeCall.Phone.OwnerId, _activeInteractionIconPath, out var sprite) || sprite is null)
            {
                return;
            }

            if (!_phoneIconHierarchyLogged)
            {
                _phoneIconHierarchyLogged = true;
                foreach (var image in phone.GetComponentsInChildren<Image>(includeInactive: true))
                {
                    if (image is null || !image.gameObject.activeInHierarchy)
                    {
                        continue;
                    }

                    Plugin.Logger?.LogInfo(
                        $"[PhoneRuntime] TalkPhone image: path='{GetPhoneIconObjectPath(image.transform)}', " +
                        $"sprite='{image.sprite?.name ?? "<none>"}', " +
                        $"active={image.gameObject.activeInHierarchy}, enabled={image.enabled}.");
                }
            }

            Image? callerImage = null;
            if (phone.phoneCharacterList is { } characters)
            {
                foreach (var character in characters)
                {
                    if (character is null || !character.activeInHierarchy)
                    {
                        continue;
                    }

                    // Business panels contain separate background, icon and shadow layers.
                    // NPC portraits instead have their Image directly on the character object.
                    var icon = character.transform.Find("icon")?.GetComponent<Image>();
                    var portrait = character.name.StartsWith("Image", StringComparison.Ordinal)
                        ? character.GetComponent<Image>()
                        : null;
                    var candidate = icon ?? portrait;
                    if (candidate is not null && candidate.enabled && candidate.gameObject.activeInHierarchy)
                    {
                        callerImage = candidate;
                        break;
                    }
                }
            }

            if (callerImage is null)
            {
                var icons = phone.GetComponentsInChildren<Image>(includeInactive: false)
                    .Where(image => image is not null && image.enabled &&
                                    image.gameObject.activeInHierarchy && image.gameObject.name == "icon")
                    .ToArray();
                callerImage = icons.Length == 1 ? icons[0] : null;
            }

            var applied = ApplyPhoneIconImage(callerImage, sprite);
            var targetKey = _activeCall.Phone.OwnerId + "|TalkPhone|" + _activeInteractionIconPath;
            if (!applied && MissingPhoneInteractionIcons.Add(targetKey))
            {
                Plugin.Logger?.LogWarning(
                    "[PhoneRuntime] TalkPhone is visible but its caller image could not be identified; " +
                    "see the TalkPhone image hierarchy above.");
            }
            else if (applied)
            {
                MissingPhoneInteractionIcons.Remove(targetKey);
            }
        }
        catch (Exception exception)
        {
            var targetKey = _activeCall?.Phone.OwnerId + "|TalkPhone|" + _activeInteractionIconPath;
            if (MissingPhoneInteractionIcons.Add(targetKey))
            {
                Plugin.Logger?.LogWarning(
                    $"[PhoneRuntime] Could not update the visible TalkPhone icon: {exception.Message}");
            }
        }
    }

    internal static void ReapplyPhoneCharacterIcon(UIImageAnimator animator)
    {
        if (_activeCall is null || _activeTalkPhone is null ||
            string.IsNullOrWhiteSpace(_activeInteractionIconPath))
        {
            return;
        }

        try
        {
            var image = animator.image;
            if (image is null || !ActivePhoneIconImages.ContainsKey(image.GetInstanceID()))
            {
                return;
            }

            if (TryGetPhoneInteractionIcon(
                    _activeCall.Phone.OwnerId, _activeInteractionIconPath, out var sprite) && sprite is not null)
            {
                ApplyPhoneIconImage(image, sprite);
            }
        }
        catch (Exception exception)
        {
            var targetKey = _activeCall?.Phone.OwnerId + "|UIImageAnimator|" + _activeInteractionIconPath;
            if (MissingPhoneInteractionIcons.Add(targetKey))
            {
                Plugin.Logger?.LogWarning(
                    $"[PhoneRuntime] Could not restore the custom phone image after animation: {exception.Message}");
            }
        }
    }

    private static bool ApplyPhoneIconImage(Image? image, Sprite sprite)
    {
        if (image is null || !image.enabled || !image.gameObject.activeInHierarchy ||
            (image.sprite is null && image.overrideSprite is null))
        {
            return false;
        }

        var instanceId = image.GetInstanceID();
        if (!ActivePhoneIconImages.ContainsKey(instanceId))
        {
            ActivePhoneIconImages.Add(
                instanceId,
                new PhoneIconImageState(
                    image, image.sprite, image.m_OverrideSprite, image.type, image.preserveAspect));
        }

        if (image.sprite?.GetInstanceID() != sprite.GetInstanceID() ||
            image.overrideSprite?.GetInstanceID() != sprite.GetInstanceID() ||
            image.type != Image.Type.Simple || !image.preserveAspect)
        {
            image.sprite = sprite;
            image.overrideSprite = sprite;
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.SetAllDirty();
        }

        if (VisiblePhoneIconImages.Add(instanceId))
        {
            Plugin.Logger?.LogInfo(
                $"[PhoneRuntime] Replaced visible TalkPhone caller image: " +
                $"path='{GetPhoneIconObjectPath(image.transform)}', " +
                $"sprite='{image.sprite?.name ?? "<none>"}', " +
                $"overrideSprite='{image.overrideSprite?.name ?? "<none>"}', " +
                $"active={image.gameObject.activeInHierarchy}, " +
                $"type={image.type}, preserveAspect={image.preserveAspect}.");
        }

        return true;
    }

    private static string GetPhoneIconObjectPath(Transform transform)
    {
        var names = new List<string>();
        Transform? current = transform;
        while (current is not null && names.Count < 16)
        {
            names.Add(current.gameObject.name);
            current = current.parent;
        }

        names.Reverse();
        return string.Join("/", names);
    }

    private static bool TryGetPhoneInteractionIcon(
        string ownerId,
        string relativePath,
        out Sprite? sprite)
    {
        sprite = null;
        try
        {
            var descriptor = ModRegistry.DiscoveredMods.FirstOrDefault(candidate =>
                string.Equals(candidate.Manifest.Id, ownerId, StringComparison.OrdinalIgnoreCase));
            if (descriptor is null)
            {
                throw new DirectoryNotFoundException($"Mod '{ownerId}' is no longer registered.");
            }

            var fullPath = new ModResources(descriptor.ResourceDirectory).GetPath(relativePath);
            var cacheKey = ownerId + "|" + fullPath;
            if (PhoneInteractionIcons.TryGetValue(cacheKey, out var loaded))
            {
                sprite = loaded.Sprite;
                return true;
            }

            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException("The custom phone interaction icon was not found.", fullPath);
            }

            var texture = new Texture2D(2, 2, TextureFormat.ARGB32, false);
            var bytes = new Il2CppStructArray<byte>(File.ReadAllBytes(fullPath));
            if (!ImageConversion.LoadImage(texture, bytes, true))
            {
                UnityEngine.Object.Destroy(texture);
                throw new InvalidDataException("Unity rejected the phone icon image bytes.");
            }

            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            var createdSprite = Sprite.Create(
                texture,
                new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f));
            createdSprite.name = $"phone-interaction.{ownerId}.{Path.GetFileNameWithoutExtension(fullPath)}";
            PhoneInteractionIcons.Add(cacheKey, new LoadedPhoneIcon(ownerId, createdSprite));
            MissingPhoneInteractionIcons.Remove(cacheKey);
            sprite = createdSprite;
            Plugin.Logger?.LogInfo(
                $"[PhoneRuntime] Loaded custom phone interaction icon for '{ownerId}' " +
                $"from '{relativePath}'.");
            return true;
        }
        catch (Exception exception)
        {
            var missingKey = ownerId + "|" + relativePath;
            if (MissingPhoneInteractionIcons.Add(missingKey))
            {
                Plugin.Logger?.LogWarning(
                    $"[PhoneRuntime] Could not load phone interaction icon '{relativePath}' " +
                    $"for '{ownerId}': {exception.Message}");
            }

            return false;
        }
    }

    private static void RestorePhoneInteractionIcon()
    {
        foreach (var state in ActivePhoneIconImages.Values)
        {
            try
            {
                if (state.Image is not null)
                {
                    state.Image.sprite = state.Sprite;
                    state.Image.overrideSprite = state.OverrideSprite;
                    state.Image.type = state.Type;
                    state.Image.preserveAspect = state.PreserveAspect;
                    state.Image.SetAllDirty();
                }
            }
            catch (Exception exception)
            {
                Plugin.DebugLog(
                    $"[PhoneRuntime] Could not restore a phone interaction UI image: {exception.Message}");
            }
        }

        ActivePhoneIconImages.Clear();
        VisiblePhoneIconImages.Clear();
    }

    private static void RemovePhoneInteractionIcons(string? ownerId)
    {
        if (ownerId is null ||
            string.Equals(_activeCall?.Phone.OwnerId, ownerId, StringComparison.OrdinalIgnoreCase))
        {
            RestorePhoneInteractionIcon();
        }

        foreach (var cacheKey in PhoneInteractionIcons
                     .Where(pair => ownerId is null ||
                                    string.Equals(pair.Value.OwnerId, ownerId, StringComparison.OrdinalIgnoreCase))
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            var sprite = PhoneInteractionIcons[cacheKey].Sprite;
            PhoneInteractionIcons.Remove(cacheKey);
            try
            {
                UnityEngine.Object.Destroy(sprite);
                UnityEngine.Object.Destroy(sprite.texture);
            }
            catch (Exception exception)
            {
                Plugin.DebugLog(
                    $"[PhoneRuntime] Could not release a cached phone interaction icon: {exception.Message}");
            }
        }

        if (ownerId is null)
        {
            MissingPhoneInteractionIcons.Clear();
        }
        else
        {
            MissingPhoneInteractionIcons.RemoveWhere(key =>
                key.StartsWith(ownerId + "|", StringComparison.OrdinalIgnoreCase));
        }
    }

    private static void CloseActivePlayerDialogue()
    {
        var talkWord = _nativePlayerTalkWord;
        _nativePlayerTalkWord = null;
        _nativePlayerStringKey = null;
        _nativePlayerLinePresentedFrame = -1;
        if (talkWord is null)
        {
            return;
        }

        try
        {
            talkWord.endWordCallBack = null;
            if (_activeNativeDialogueTarget?.PlayerTalkWord is { } playerTalkWordBase)
            {
                playerTalkWordBase.endWordCallBack = null;
            }

            talkWord.HideTalkWordImmediately();
            if (talkWord.contentText is { } contentText)
            {
                contentText.text = string.Empty;
            }

            Plugin.Logger?.LogInfo(
                "[PhoneRuntime] Closed the previous player dialogue before presenting the next line.");
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogWarning(
                $"[PhoneRuntime] Could not close the previous player dialogue: {exception.Message}");
        }
    }
}

internal static class PhoneRuntimeProviders
{
    internal static void Install()
    {
        PhoneApi.ConversationRegistrationProvider = PhoneRuntime.RegisterConversation;
        PhoneApi.ConditionRegistrationProvider = PhoneRuntime.RegisterCondition;
        PhoneApi.ConditionEvaluationProvider = PhoneRuntime.EvaluateCondition;
        PhoneApi.NumberRegistrationProvider = PhoneRuntime.RegisterNumber;
        PhoneApi.LineOverrideProvider = PhoneRuntime.OverrideLine;
        PhoneApi.ResumeCurrentCallProvider = PhoneRuntime.ResumeCurrentCall;
        PhoneApi.SubscriberErrorLogger = exception =>
            Plugin.Logger?.LogError($"[PhoneRuntime] Phone observer failed: {exception}");
    }

    internal static void Clear()
    {
        PhoneApi.ConversationRegistrationProvider = null;
        PhoneApi.ConditionRegistrationProvider = null;
        PhoneApi.ConditionEvaluationProvider = null;
        PhoneApi.NumberRegistrationProvider = null;
        PhoneApi.LineOverrideProvider = null;
        PhoneApi.ResumeCurrentCallProvider = null;
        PhoneApi.SubscriberErrorLogger = null;
        PhoneApi.ClearSubscribers();
        PhoneRuntime.Reset();
    }
}

internal static class PhoneRuntimePatchInstaller
{
    internal static void Install(Harmony harmony)
    {
        Plugin.Logger?.LogInfo(
            $"[PhoneRuntime] Installing phone patches from '{typeof(PhoneRuntime).Assembly.Location}'.");
        harmony.PatchAll(typeof(PhoneConfigDataPatch));
        harmony.PatchAll(typeof(CustomPhoneNumberValidationPatch));
        harmony.PatchAll(typeof(CustomPhoneSelectionValidationPatch));
        harmony.PatchAll(typeof(CustomPhoneCallPatch));
        harmony.PatchAll(typeof(CustomPhoneStartCallPatch));
        harmony.PatchAll(typeof(CustomPhoneBookPatch));
        harmony.PatchAll(typeof(CustomPhoneBookDisplayPatch));
        harmony.PatchAll(typeof(PhoneDialogueTextPatch));
        harmony.PatchAll(typeof(NativePhoneCallStartedPatch));
        harmony.PatchAll(typeof(NativePhoneCallEndedPatch));
        harmony.PatchAll(typeof(NativePhoneHangUpAnimationPatch));
        harmony.PatchAll(typeof(NativePhoneEndAnimationPatch));
        harmony.PatchAll(typeof(NativePhoneEndAnimationCallbackPatch));
        harmony.PatchAll(typeof(NativeActionBaseDoNextProcessPatch));
        harmony.PatchAll(typeof(NativeInteractionTalkUnShowPhonePatch));
        harmony.PatchAll(typeof(NativeTalkPhoneHangUpPatch));
        harmony.PatchAll(typeof(NativePhoneLineObservedPatch));
        harmony.PatchAll(typeof(PhoneDialogueTypingPatch));
        harmony.PatchAll(typeof(PhoneCharacterAnimationSpritePatch));
        harmony.PatchAll(typeof(PhoneOptionsCancelPatch));
        harmony.PatchAll(typeof(PhoneOptionsCancelSelectPatch));
        }
    }

    [HarmonyPatch(typeof(ConfigData), nameof(ConfigData.InitConfig))]
internal static class PhoneConfigDataPatch
{
    [HarmonyPostfix]
    private static void Postfix() => PhoneRuntime.RefreshConfigPhoneRows();
}

[HarmonyPatch(typeof(UI_PublicTelephone), nameof(UI_PublicTelephone.IsPhoneNumRight))]
internal static class CustomPhoneNumberValidationPatch
{
    [HarmonyPrefix]
    private static bool Prefix(UI_PublicTelephone __instance, ref bool __result)
    {
        var number = __instance.resultPhoneNumber;
        if (!PhoneRuntime.TryGetNumber(number, out var displayName))
        {
            return true;
        }

        Plugin.Logger?.LogInfo(
            $"[PhoneRuntime] Accepted custom phone number input '{number}' ({displayName}).");
        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(UI_PublicTelephone), nameof(UI_PublicTelephone.CheckSelectNumRight))]
internal static class CustomPhoneSelectionValidationPatch
{
    [HarmonyPrefix]
    private static bool Prefix(UI_PublicTelephone __instance, ref bool __result)
    {
        var number = __instance.resultPhoneNumber;
        if (!PhoneRuntime.TryGetNumber(number, out var displayName))
        {
            return true;
        }

        Plugin.Logger?.LogInfo(
            $"[PhoneRuntime] Accepted custom number in CheckSelectNumRight: '{number}' ({displayName}).");
        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(UI_PublicTelephone), nameof(UI_PublicTelephone.PhoneCalley))]
internal static class CustomPhoneCallPatch
{
    [HarmonyPrefix]
    private static bool Prefix(UI_PublicTelephone __instance)
    {
        return !CustomPhoneCallHandler.TryBegin(__instance, nameof(UI_PublicTelephone.PhoneCalley));
    }
}

[HarmonyPatch(typeof(UI_PublicTelephone), nameof(UI_PublicTelephone.StartCallPhone))]
internal static class CustomPhoneStartCallPatch
{
    [HarmonyPrefix]
    private static bool Prefix(UI_PublicTelephone __instance) =>
        !CustomPhoneCallHandler.TryBegin(__instance, nameof(UI_PublicTelephone.StartCallPhone));
}

internal static class CustomPhoneCallHandler
{
    internal static bool TryBegin(UI_PublicTelephone instance, string source)
    {
        var number = instance.resultPhoneNumber;
        Plugin.Logger?.LogInfo(
            $"[PhoneRuntime] {source} reached for '{number}'.");
        if (string.IsNullOrWhiteSpace(number))
        {
            try
            {
                instance.GetPhoneNumber();
                number = instance.resultPhoneNumber;
                var keypadText = ReadKeypadText(instance);
                if (string.IsNullOrWhiteSpace(number) &&
                    !string.IsNullOrWhiteSpace(keypadText) &&
                    !keypadText.StartsWith("<", StringComparison.Ordinal))
                {
                    number = keypadText.Trim();
                    instance.resultPhoneNumber = number;
                }

                Plugin.Logger?.LogInfo(
                    $"[PhoneRuntime] Refreshed number at {source}: result='{number}', " +
                    $"keypad='{keypadText}'.");
            }
            catch (Exception exception)
            {
                Plugin.Logger?.LogWarning(
                    $"[PhoneRuntime] Could not read keypad number at {source}: {exception.Message}");
            }
        }

        var isCustomNumber = PhoneRuntime.TryGetNumber(number, out var displayName);
        Plugin.Logger?.LogInfo(
            $"[PhoneRuntime] Resolved number at {source}: '{number}' (custom={isCustomNumber}).");
        if (!isCustomNumber)
        {
            return false;
        }

        if (PhoneRuntime.HasActiveCall)
        {
            Plugin.Logger?.LogInfo(
                $"[PhoneRuntime] Ignored duplicate custom call callback from {source} " +
                $"for '{number}'; an active call already exists.");
            return true;
        }

        if (!PhoneRuntime.BeginCall(number))
        {
            Plugin.Logger?.LogWarning(
                $"[PhoneRuntime] Could not begin the registered call to '{number}' ({displayName}).");
            return true;
        }

        Plugin.Logger?.LogInfo(
            $"[PhoneRuntime] Started custom call to '{number}' ({displayName}) from {source}.");
        var nativeTalkWord = PhoneRuntime.FindNativeDialogueTarget();
        try
        {
            instance.CloseUI();
        }
        catch (Exception exception)
        {
            // The telephone UI's native close path can throw when its UI_Base close
            // callback/window references are unset. Try the component's animation
            // and auto-close paths as well so the phone UI releases input focus.
            Plugin.Logger?.LogWarning(
                $"[PhoneRuntime] Public telephone CloseUI failed; continuing to native dialogue: {exception}");

            try
            {
                instance.CloseByAnimator();
                Plugin.Logger?.LogInfo(
                    "[PhoneRuntime] Closed public telephone with CloseByAnimator fallback.");
            }
            catch (Exception closeByAnimatorException)
            {
                Plugin.Logger?.LogWarning(
                    $"[PhoneRuntime] CloseByAnimator fallback failed: {closeByAnimatorException.Message}");
            }

            try
            {
                instance.CloseAuto();
                Plugin.Logger?.LogInfo(
                    "[PhoneRuntime] Ran public telephone CloseAuto fallback.");
            }
            catch (Exception closeAutoException)
            {
                Plugin.Logger?.LogWarning(
                    $"[PhoneRuntime] CloseAuto fallback failed: {closeAutoException.Message}");
            }
        }

        PhoneRuntime.TryShowNativeDialogue(nativeTalkWord);
        return true;
    }

    private static string ReadKeypadText(UI_PublicTelephone instance)
    {
        try
        {
            var numberText = instance.phoneNumText;
            if (numberText is null)
            {
                return "<null>";
            }

            var parts = new string[numberText.Count];
            for (var index = 0; index < numberText.Count; index++)
            {
                parts[index] = numberText[index]?.text ?? string.Empty;
            }

            return string.Concat(parts);
        }
        catch (Exception exception)
        {
            return $"<unavailable: {exception.GetType().Name}>";
        }
    }
}

[HarmonyPatch(typeof(StoragePhoneInfo), nameof(StoragePhoneInfo.GetPhoneBookList))]
internal static class CustomPhoneBookPatch
{
    [HarmonyPostfix]
    private static void Postfix(ref Il2CppSystem.Collections.Generic.List<c_phone> __result)
    {
        if (__result is null)
        {
            __result = new Il2CppSystem.Collections.Generic.List<c_phone>();
        }

        foreach (var row in PhoneRuntime.GetPhoneBookRows())
        {
            __result.Add(row);
        }
    }
}

[HarmonyPatch(typeof(UI_PhoneBookUnit), nameof(UI_PhoneBookUnit.InitPhoneBookUnit))]
internal static class CustomPhoneBookDisplayPatch
{
    [HarmonyPostfix]
    private static void Postfix(UI_PhoneBookUnit __instance, int id)
    {
        if (!PhoneRuntime.TryGetPhoneBookEntry(id, out var entry))
        {
            return;
        }

        Text? nameText = __instance.phoneNameText;
        Text? numberText = __instance.phoneNumber;
        if (nameText is not null)
        {
            nameText.text = entry.Name;
        }

        if (numberText is not null)
        {
            numberText.text = entry.Number;
        }
    }
}

[HarmonyPatch(typeof(TalkString), "get_content")]
internal static class PhoneDialogueTextPatch
{
    [HarmonyPostfix]
    private static void Postfix(TalkString __instance, ref string __result)
    {
        var stringKey = __instance.stringKey;
        if (!string.IsNullOrEmpty(stringKey) &&
            PhoneRuntime.TryGetNativeDialogueText(stringKey, out var nativeDialogueText))
        {
            __result = nativeDialogueText;
            return;
        }

        if (!string.IsNullOrEmpty(stringKey) &&
            PhoneRuntime.TryOverrideLine(stringKey, out var replacementText))
        {
            __result = replacementText;
        }
    }
}

[HarmonyPatch(typeof(ActionPhone), nameof(ActionPhone.StartPhoneCall))]
internal static class NativePhoneCallStartedPatch
{
    [HarmonyPostfix]
    private static void Postfix() => PhoneRuntime.NativePhoneCallStarted();
}

[HarmonyPatch(typeof(ActionPhone), nameof(ActionPhone.HangUpPhone))]
internal static class NativePhoneCallEndedPatch
{
    [HarmonyPrefix]
    private static bool Prefix()
    {
        var shouldSuppress = PhoneRuntime.ShouldSuppressNativeHangUp;
        Plugin.Logger?.LogInfo(
            $"[PhoneRuntime] Native HangUpPhone entered (customCallActive={shouldSuppress}).");
        if (!shouldSuppress)
        {
            return true;
        }

        Plugin.Logger?.LogInfo(
            "[PhoneRuntime] Suppressed native HangUpPhone while a custom call is active.");
        return false;
    }

    [HarmonyPostfix]
    private static void Postfix(bool __runOriginal)
    {
        if (__runOriginal)
        {
            PhoneRuntime.NativePhoneCallEnded();
        }
    }
}

[HarmonyPatch(typeof(ActionPhone), nameof(ActionPhone.PlayHangUpPhone))]
internal static class NativePhoneHangUpAnimationPatch
{
    [HarmonyPrefix]
    private static bool Prefix(ref bool __result)
    {
        if (!PhoneRuntime.ShouldSuppressNativeHangUp)
        {
            return true;
        }

        __result = false;
        Plugin.Logger?.LogInfo(
            "[PhoneRuntime] Suppressed native PlayHangUpPhone while a custom call is active.");
        return false;
    }
}

[HarmonyPatch(typeof(ActionPhone), nameof(ActionPhone.PlayPhoneEndAnimation))]
internal static class NativePhoneEndAnimationPatch
{
    [HarmonyPrefix]
    private static bool Prefix(ref bool __result)
    {
        if (!PhoneRuntime.ShouldSuppressNativeHangUp)
        {
            return true;
        }

        __result = false;
        Plugin.Logger?.LogInfo(
            "[PhoneRuntime] Suppressed native PlayPhoneEndAnimation while a custom call is active.");
        return false;
    }
}

[HarmonyPatch(typeof(ActionPhone), nameof(ActionPhone.PlayPhoneEndAnimationAndCallBack))]
internal static class NativePhoneEndAnimationCallbackPatch
{
    [HarmonyPrefix]
    private static bool Prefix()
    {
        if (!PhoneRuntime.ShouldSuppressNativeHangUp)
        {
            return true;
        }

        Plugin.Logger?.LogInfo(
            "[PhoneRuntime] Suppressed native PlayPhoneEndAnimationAndCallBack while a custom call is active.");
        return false;
    }
}

[HarmonyPatch(typeof(TalkPhone), nameof(TalkPhone.HangUpPhone))]
internal static class NativeTalkPhoneHangUpPatch
{
    [HarmonyPrefix]
    private static bool Prefix(TalkPhone __instance)
    {
        if (!PhoneRuntime.ShouldPreserveNativeTalkPhone(__instance))
        {
            return true;
        }

        Plugin.Logger?.LogInfo(
            "[PhoneRuntime] Suppressed TalkPhone.HangUpPhone for the active custom call.");
        return false;
    }
}

[HarmonyPatch(typeof(InteractionTalk), nameof(InteractionTalk.UnShowTalkPhone))]
internal static class NativeInteractionTalkUnShowPhonePatch
{
    [HarmonyPrefix]
    private static bool Prefix(InteractionTalk __instance)
    {
        if (!PhoneRuntime.ShouldPreserveNativeTalkPhoneInteraction(__instance))
        {
            return true;
        }

        Plugin.Logger?.LogInfo(
            "[PhoneRuntime] Suppressed InteractionTalk.UnShowTalkPhone for the active custom call.");
        return false;
    }
}

[HarmonyPatch(typeof(ActionBase), nameof(ActionBase.DoNextProcess))]
internal static class NativeActionBaseDoNextProcessPatch
{
    [HarmonyPrefix]
    private static bool Prefix()
    {
        if (!PhoneRuntime.ShouldDeferNativeActionProgress)
        {
            return true;
        }

        PhoneRuntime.DeferNativeActionProgress();
        return false;
    }
}

[HarmonyPatch(typeof(TalkWord), nameof(TalkWord.Say))]
internal static class NativePhoneLineObservedPatch
{
    [HarmonyPrefix]
    private static void Prefix(TalkString talkString) => PhoneRuntime.ObserveNativeLine(talkString);

    [HarmonyPostfix]
    private static void Postfix(TalkWord __instance, TalkString talkString) =>
        PhoneRuntime.EnsureNativeDialogueText(__instance, talkString);
}

[HarmonyPatch(typeof(TalkWord), nameof(TalkWord.Text_TypeText))]
internal static class PhoneDialogueTypingPatch
{
    [HarmonyPrefix]
    private static void Prefix(TalkWord __instance, ref string str) =>
        PhoneRuntime.ForceNativeDialogueText(__instance, ref str);
}

[HarmonyPatch(typeof(UIImageAnimator), nameof(UIImageAnimator.SetImageSprite))]
internal static class PhoneCharacterAnimationSpritePatch
{
    [HarmonyPostfix]
    private static void Postfix(UIImageAnimator __instance) =>
        PhoneRuntime.ReapplyPhoneCharacterIcon(__instance);
}

public sealed class PhoneConversationOverlay : MonoBehaviour
{
    private bool _loggedActiveCall;
    private Vector2 _optionScroll;

    private void Update() => PhoneRuntime.TryAdvancePlayerDialogueFromInput();

    private void OnGUI()
    {
        if (!PhoneRuntime.HasActiveCall)
        {
            _loggedActiveCall = false;
            _optionScroll = Vector2.zero;
            return;
        }

        if (PhoneRuntime.NativeDialogueDisplayed)
        {
            _loggedActiveCall = false;
            return;
        }

        if (!_loggedActiveCall)
        {
            Plugin.Logger?.LogInfo(
                $"[PhoneRuntime] Drawing conversation window for '{PhoneRuntime.ActiveDisplayName}' " +
                $"(speaker='{PhoneRuntime.ActiveSpeaker}').");
            _loggedActiveCall = true;
        }

        var width = Mathf.Min(Screen.width - 48f, 860f);
        var height = Mathf.Min(Screen.height - 48f, PhoneRuntime.WaitingForPhoneOption ? 420f : 300f);
        var panel = new Rect(
            (Screen.width - width) / 2f,
            Screen.height - height - 24f,
            width,
            height);
        GUI.Box(panel, PhoneRuntime.ActiveDisplayName);
        GUI.Label(
            new Rect(panel.x + 24f, panel.y + 44f, panel.width - 48f, 30f),
            PhoneRuntime.ActiveSpeaker);
        GUI.Label(
            new Rect(panel.x + 24f, panel.y + 78f, panel.width - 48f,
                PhoneRuntime.WaitingForPhoneOption ? 60f : panel.height - 132f),
            PhoneRuntime.ActiveText);

        if (PhoneRuntime.WaitingForPhoneOption)
        {
            var options = PhoneRuntime.ActivePhoneOptions;
            var area = new Rect(panel.x + 24f, panel.y + 146f, panel.width - 48f, panel.height - 170f);
            _optionScroll = GUI.BeginScrollView(area, _optionScroll,
                new Rect(0f, 0f, area.width - 20f, options.Count * 40f));
            var selected = -1;
            for (var index = 0; index < options.Count; index++)
            {
                if (GUI.Button(new Rect(0f, index * 40f, area.width - 20f, 34f), options[index].Text))
                {
                    selected = index;
                }
            }
            GUI.EndScrollView();
            if (selected >= 0)
            {
                PhoneRuntime.SelectPhoneOption(selected);
            }
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            {
                PhoneRuntime.EndCall();
                Event.current.Use();
            }
            return;
        }
        _optionScroll = Vector2.zero;

        var buttonWidth = 120f;
        var buttonY = panel.yMax - 44f;
        if (GUI.Button(
                new Rect(panel.xMax - buttonWidth - 24f, buttonY, buttonWidth, 28f),
                PhoneRuntime.IsLastLine ? "挂断" : "继续"))
        {
            PhoneRuntime.AdvanceCall();
        }

        if (Event.current.type == EventType.KeyDown &&
            (Event.current.keyCode is KeyCode.Space or KeyCode.Return or KeyCode.KeypadEnter))
        {
            PhoneRuntime.AdvanceCall();
            Event.current.Use();
        }
    }

    private void LateUpdate()
    {
        PhoneRuntime.KeepNativePlayerDialogueTextVisible();
        PhoneRuntime.KeepNativePhoneIconVisible();
    }
}
