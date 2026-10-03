using BackToTheDawn.PhoneAPI;
using HarmonyLib;
using Il2CppInterop.Runtime;

namespace BackToTheDawn.Loader;

internal static partial class PhoneRuntime
{
    private static bool _waitingForPhoneOption;
    private static int _phoneOptionLineIndex = -1;
    private static int _phoneOptionGeneration;
    private static int _nativeLineGeneration;
    private static bool _optionResumeNativeDialogue;
    private static InteractionList? _nativePhoneOptionList;
    private static Il2CppSystem.Action<int>? _nativePhoneOptionPicked;
    private static Il2CppSystem.Action? _nativePhoneOptionCanceled;
    private static Il2CppSystem.Action? _previousPhoneOptionCancel;

    internal static bool WaitingForPhoneOption => _waitingForPhoneOption;

    internal static IReadOnlyList<PhoneDialogueOption> ActivePhoneOptions =>
        _waitingForPhoneOption && _activeCall is { } call && _phoneOptionLineIndex >= 0
            ? call.Conversation.Definition.Lines[_phoneOptionLineIndex].Options
            : Array.Empty<PhoneDialogueOption>();

    private static void CompletePhoneDialogueLine()
    {
        if (_activeCall is null || _waitingForPhoneOption)
        {
            return;
        }
        var lines = _activeCall.Conversation.Definition.Lines;
        if (lines[_activeLineIndex].Options.Count > 0)
        {
            ShowPhoneOptions();
            return;
        }
        MoveToPhoneLine(PhoneDialogueGraph.Next(lines, _activeLineIndex));
    }

    private static void MoveToPhoneLine(int next)
    {
        if (_activeCall is null)
        {
            return;
        }
        if (next < 0)
        {
            EndCall();
            return;
        }
        _activeLineIndex = next;
        _nativeDialogueLineIndex = next;
        if (_nativeDialogueDisplayed && _activeNativeDialogueTarget is not null &&
            NativeDialogueTalkStrings.Count > next)
        {
            // The first line was published by BeginCall. Publish it again only
            // when a branch returns to it; other native lines are observed by Say.
            if (next == 0)
            {
                RaiseActiveLine();
            }
            PresentNativeDialogueLine();
        }
        else
        {
            _nativeDialogueDisplayed = false;
            RaiseActiveLine();
        }
    }

    private static void ShowPhoneOptions()
    {
        if (_activeCall is not { } call)
        {
            return;
        }
        _waitingForPhoneOption = true;
        _phoneOptionLineIndex = _activeLineIndex;
        _optionResumeNativeDialogue = _nativeDialogueDisplayed;
        var generation = ++_phoneOptionGeneration;
        _nativeLineGeneration++;
        var options = ActivePhoneOptions;
        try
        {
            CloseActivePlayerDialogue();
            if (_activeNativeDialogueTarget?.CallerTalkWord is { } callerWord)
            {
                callerWord.endWordCallBack = null;
            }
            _activeNativeDialogueTarget?.CallerInteractionTalk?.UnShowWord();

            var playerTalk = _activeNativeDialogueTarget?.PlayerCharacterTalk ??
                             (CharacterManage.currentControlCharacter ?? CharacterManage.protagonist)?.characterTalk;
            if (playerTalk is null)
            {
                throw new InvalidOperationException("The player option presenter is unavailable.");
            }
            _nativePhoneOptionList = playerTalk.GetInteractionList();
            if (_nativePhoneOptionList is null)
            {
                throw new InvalidOperationException("The native InteractionList is unavailable.");
            }
            _previousPhoneOptionCancel = _nativePhoneOptionList.onCancelCallBack;

            var choices = new Il2CppSystem.Collections.Generic.Dictionary<int, string>();
            for (var index = 0; index < options.Count; index++)
            {
                choices.Add(index + 1, options[index].Text);
            }
            _nativePhoneOptionPicked = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<int>>((int key) =>
            {
                if (ReferenceEquals(_activeCall, call) && generation == _phoneOptionGeneration)
                {
                    SelectPhoneOption(key - 1);
                }
            });
            _nativePhoneOptionCanceled = DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(() =>
            {
                if (ReferenceEquals(_activeCall, call) && generation == _phoneOptionGeneration &&
                    _waitingForPhoneOption)
                {
                    EndCall();
                }
            });
            playerTalk.ShowTalkOptionsList(choices, _nativePhoneOptionPicked);
            _nativePhoneOptionList.onCancelCallBack = _nativePhoneOptionCanceled;
            _nativeDialogueDisplayed = true;
            Plugin.Logger?.LogInfo(
                $"[PhoneRuntime] Showing {options.Count} native phone options through " +
                $"CharacterTalk.ShowTalkOptionsList (number='{call.Phone.Number}', " +
                $"line='{call.Conversation.Definition.Lines[_phoneOptionLineIndex].Id}', " +
                $"active={_nativePhoneOptionList.gameObject.activeInHierarchy}).");
        }
        catch (Exception exception)
        {
            CloseNativePhoneOptionList();
            _nativeDialogueDisplayed = false;
            Plugin.Logger?.LogWarning(
                $"[PhoneRuntime] Native phone options failed; showing Loader option buttons: {exception.Message}");
        }
    }

    internal static void SelectPhoneOption(int index)
    {
        if (!_waitingForPhoneOption || _activeCall is not { } call ||
            index < 0 || index >= ActivePhoneOptions.Count)
        {
            return;
        }
        var line = call.Conversation.Definition.Lines[_phoneOptionLineIndex];
        var option = line.Options[index];
        var next = PhoneDialogueGraph.Next(call.Conversation.Definition.Lines, _phoneOptionLineIndex, option);
        var resumeNative = _optionResumeNativeDialogue;
        ClearPhoneOptions();
        _nativeDialogueDisplayed = resumeNative;
        // The input that chose the option must not also skip the next line.
        _lastNativeDialogueAdvanceFrame = UnityEngine.Time.frameCount;
        Plugin.Logger?.LogInfo(
            $"[PhoneRuntime] Selected phone option '{option.Id}' for '{call.Phone.Number}' " +
            $"(nextLineIndex={next}).");
        PhoneApi.PublishOptionSelected(new PhoneOptionSelectedEvent(
            call.Phone.Number, call.Conversation.Definition.Key, line.Id, option.Id, option.Text));
        if (ReferenceEquals(_activeCall, call))
        {
            MoveToPhoneLine(next);
        }
    }

    internal static bool CancelNativePhoneOptions(InteractionList list)
    {
        if (!_waitingForPhoneOption || _nativePhoneOptionList is null ||
            list.GetInstanceID() != _nativePhoneOptionList.GetInstanceID())
        {
            return false;
        }
        Plugin.Logger?.LogInfo("[PhoneRuntime] Canceled native phone options; hanging up.");
        EndCall();
        return true;
    }

    private static void ClearPhoneOptions()
    {
        _waitingForPhoneOption = false;
        _phoneOptionLineIndex = -1;
        _phoneOptionGeneration++;
        _optionResumeNativeDialogue = false;
        CloseNativePhoneOptionList();
    }

    private static void CloseNativePhoneOptionList()
    {
        var list = _nativePhoneOptionList;
        var previousCancel = _previousPhoneOptionCancel;
        _nativePhoneOptionList = null;
        _previousPhoneOptionCancel = null;
        try
        {
            if (list is not null)
            {
                list.onCancelCallBack = previousCancel;
                list.UnShowList(isPlaySound: false);
            }
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogWarning($"[PhoneRuntime] Could not close native phone options: {exception.Message}");
        }
        finally
        {
            _nativePhoneOptionPicked = null;
            _nativePhoneOptionCanceled = null;
        }
    }
}

[HarmonyPatch(typeof(InteractionList), nameof(InteractionList.OnClickCancel))]
internal static class PhoneOptionsCancelPatch
{
    [HarmonyPrefix]
    private static bool Prefix(InteractionList __instance) =>
        !PhoneRuntime.CancelNativePhoneOptions(__instance);
}

[HarmonyPatch(typeof(InteractionList), nameof(InteractionList.OnClickCancelSelect))]
internal static class PhoneOptionsCancelSelectPatch
{
    [HarmonyPrefix]
    private static bool Prefix(InteractionList __instance) =>
        !PhoneRuntime.CancelNativePhoneOptions(__instance);
}
