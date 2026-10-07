using BackToTheDawn.ModAPI;

namespace BackToTheDawn.PhoneAPI;

/// <summary>
/// Phone registration and dialogue API packaged independently from the core ModAPI.
/// </summary>
public sealed class PhoneApi
{
    internal static Func<string, PhoneConversationDefinition, PhoneConversationRegistrationResult>?
        ConversationRegistrationProvider { get; set; }

    internal static Func<string, string, Func<PhoneCallContext, bool>, PhoneConditionRegistrationResult>?
        ConditionRegistrationProvider { get; set; }

    internal static Func<string, string, PhoneCallContext, bool?>? ConditionEvaluationProvider { get; set; }

    internal static Func<string, string, string, string, bool, PhoneNumberRegistrationResult>?
        NumberRegistrationProvider { get; set; }

    internal static Func<string, string, string, PhoneLineOverrideResult>?
        LineOverrideProvider { get; set; }

    private static event Action<PhoneLineDisplayedEvent>? LineDisplayed;
    private static event Action<PhoneOptionSelectedEvent>? OptionSelected;
    internal static Action<Exception>? SubscriberErrorLogger { get; set; }

    private readonly string _ownerId;
    private readonly HashSet<string> _accessibleNamespaces;

    private PhoneApi(string ownerId, IEnumerable<string> accessibleNamespaces)
    {
        _ownerId = ownerId;
        _accessibleNamespaces = new HashSet<string>(accessibleNamespaces, StringComparer.OrdinalIgnoreCase)
        {
            ownerId,
        };
    }

    /// <summary>Creates a phone API instance scoped to the supplied Mod context.</summary>
    public static PhoneApi For(ModContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new PhoneApi(context.Manifest.Id, context.Manifest.Dependencies);
    }

    /// <summary>
    /// Registers a named predicate that can be referenced by conditional JSON phone routes.
    /// The key is scoped to this Mod and is evaluated when the player places a call.
    /// </summary>
    public PhoneConditionRegistrationResult RegisterCondition(
        string key,
        Func<PhoneCallContext, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var normalizedKey = NormalizeKey(key);
        var separator = normalizedKey.IndexOf(':');
        if (separator != normalizedKey.LastIndexOf(':') || separator == normalizedKey.Length - 1)
        {
            throw new ArgumentException(
                "A phone condition key must have the form 'namespace:key'.",
                nameof(key));
        }
        return ConditionRegistrationProvider?.Invoke(_ownerId, normalizedKey, predicate)
               ?? PhoneConditionRegistrationResult.Unavailable(normalizedKey);
    }

    /// <summary>
    /// Evaluates a condition registered by this Mod or one of its declared dependencies.
    /// Returns false when the key is inaccessible or was not registered.
    /// </summary>
    public bool TryEvaluateCondition(
        string key,
        PhoneCallContext context,
        out bool result)
    {
        ArgumentNullException.ThrowIfNull(context);
        var normalizedKey = NormalizeConditionReference(key);
        var separator = normalizedKey.IndexOf(':');
        if (separator <= 0 || !_accessibleNamespaces.Contains(normalizedKey[..separator]))
        {
            result = false;
            return false;
        }

        var evaluated = ConditionEvaluationProvider?.Invoke(_ownerId, normalizedKey, context);
        if (evaluated is null)
        {
            result = false;
            return false;
        }

        result = evaluated.Value;
        return true;
    }

    /// <summary>
    /// Registers a sequence of lines that can be attached to a custom number.
    /// Keys are automatically prefixed with this Mod's manifest ID when they
    /// do not already contain a namespace.
    /// </summary>
    public PhoneConversationRegistrationResult RegisterConversation(
        string key,
        IEnumerable<PhoneDialogueLine> lines,
        string? interactionIconPath = null)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var normalizedKey = NormalizeKey(key);
        var lineArray = lines.ToArray();
        if (PhoneDialogueGraph.Validate(lineArray) is { } error)
        {
            throw new ArgumentException(error, nameof(lines));
        }
        lineArray = PhoneDialogueGraph.Snapshot(lineArray);

        if (interactionIconPath is not null && string.IsNullOrWhiteSpace(interactionIconPath))
        {
            throw new ArgumentException(
                "A phone interaction icon path cannot be empty.",
                nameof(interactionIconPath));
        }

        return ConversationRegistrationProvider?.Invoke(
                   _ownerId,
                   new PhoneConversationDefinition(normalizedKey, lineArray)
                   {
                       InteractionIconPath = interactionIconPath,
                   })
               ?? PhoneConversationRegistrationResult.Unavailable(normalizedKey);
    }

    /// <summary>
    /// Registers a conversation whose lines are created separately for each call.
    /// The factory runs when the player places the call, using that call's time snapshot.
    /// </summary>
    public PhoneConversationRegistrationResult RegisterDynamicConversation(
        string key,
        Func<PhoneCallContext, IEnumerable<PhoneDialogueLine>> lineFactory,
        string? interactionIconPath = null)
    {
        ArgumentNullException.ThrowIfNull(lineFactory);
        var normalizedKey = NormalizeKey(key);
        if (interactionIconPath is not null && string.IsNullOrWhiteSpace(interactionIconPath))
        {
            throw new ArgumentException(
                "A phone interaction icon path cannot be empty.",
                nameof(interactionIconPath));
        }

        return ConversationRegistrationProvider?.Invoke(
                   _ownerId,
                   new PhoneConversationDefinition(normalizedKey, Array.Empty<PhoneDialogueLine>())
                   {
                       DynamicLinesFactory = lineFactory,
                       InteractionIconPath = interactionIconPath,
                   })
               ?? PhoneConversationRegistrationResult.Unavailable(normalizedKey);
    }

    /// <summary>
    /// Registers a five-digit number in the in-game phone book and routes calls
    /// to a conversation previously registered by this Mod.
    /// </summary>
    public PhoneNumberRegistrationResult RegisterNumber(
        string number,
        string displayName,
        string conversationKey,
        bool addToPhoneBook = true)
    {
        if (string.IsNullOrWhiteSpace(number))
        {
            throw new ArgumentException("A phone number is required.", nameof(number));
        }
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("A display name is required.", nameof(displayName));
        }
        var normalizedConversationKey = NormalizeKey(conversationKey);
        if (number.Length != 5 || number.Any(character => character is < '0' or > '9'))
        {
            throw new ArgumentException(
                "Phone numbers must contain exactly five digits.",
                nameof(number));
        }

        return NumberRegistrationProvider?.Invoke(
                   _ownerId,
                   number,
                   displayName.Trim(),
                   normalizedConversationKey,
                   addToPhoneBook)
               ?? PhoneNumberRegistrationResult.Unavailable(number);
    }

    /// <summary>
    /// Replaces the displayed text for a native dialogue line. Use
    /// SubscribeLineDisplayed to inspect native string keys while making a call.
    /// </summary>
    public PhoneLineOverrideResult OverrideLine(string stringKey, string replacementText)
    {
        if (string.IsNullOrWhiteSpace(stringKey))
        {
            throw new ArgumentException("A native dialogue string key is required.", nameof(stringKey));
        }
        ArgumentNullException.ThrowIfNull(replacementText);
        return LineOverrideProvider?.Invoke(_ownerId, stringKey.Trim(), replacementText)
               ?? PhoneLineOverrideResult.Unavailable(stringKey.Trim());
    }

    /// <summary>Observes native or Mod-provided phone dialogue lines.</summary>
    public IDisposable SubscribeLineDisplayed(Action<PhoneLineDisplayedEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        LineDisplayed += handler;
        return new Subscription(() => LineDisplayed -= handler);
    }

    internal static void PublishLineDisplayed(PhoneLineDisplayedEvent value)
    {
        var subscribers = LineDisplayed;
        if (subscribers is null)
        {
            return;
        }

        foreach (var subscriber in subscribers.GetInvocationList().Cast<Action<PhoneLineDisplayedEvent>>())
        {
            try
            {
                subscriber(value);
            }
            catch (Exception exception)
            {
                SubscriberErrorLogger?.Invoke(exception);
            }
        }
    }

    /// <summary>Observes choices made in Mod-provided phone conversations.</summary>
    public IDisposable SubscribeOptionSelected(Action<PhoneOptionSelectedEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        OptionSelected += handler;
        return new Subscription(() => OptionSelected -= handler);
    }

    internal static void PublishOptionSelected(PhoneOptionSelectedEvent value)
    {
        var subscribers = OptionSelected;
        if (subscribers is null)
        {
            return;
        }

        foreach (var subscriber in subscribers.GetInvocationList().Cast<Action<PhoneOptionSelectedEvent>>())
        {
            try
            {
                subscriber(value);
            }
            catch (Exception exception)
            {
                SubscriberErrorLogger?.Invoke(exception);
            }
        }
    }

    internal static void ClearSubscribers()
    {
        LineDisplayed = null;
        OptionSelected = null;
    }

    private string NormalizeKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("A phone key is required.", nameof(key));
        }
        var trimmed = key.Trim();
        if (!trimmed.Contains(':'))
        {
            return $"{_ownerId}:{trimmed}";
        }

        if (!trimmed.StartsWith(_ownerId + ":", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Phone keys must use this Mod's namespace '{_ownerId}:'.",
                nameof(key));
        }

        return trimmed;
    }

    private string NormalizeConditionReference(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("A phone condition key is required.", nameof(key));
        }

        var trimmed = key.Trim();
        if (!trimmed.Contains(':'))
        {
            return $"{_ownerId}:{trimmed}";
        }

        var separator = trimmed.IndexOf(':');
        if (separator <= 0 || separator != trimmed.LastIndexOf(':') || separator == trimmed.Length - 1)
        {
            throw new ArgumentException("A phone condition key must have the form 'namespace:key'.", nameof(key));
        }

        return trimmed;
    }

    private sealed class Subscription : IDisposable
    {
        private Action? _dispose;

        internal Subscription(Action dispose) => _dispose = dispose;

        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}

public enum PhoneDialogueSpeakerType
{
    Caller,
    Player,
}

public sealed record PhoneDialogueLine(
    string Speaker,
    string Text,
    PhoneDialogueSpeakerType SpeakerType)
{
    /// <summary>Optional identifier used as a branch destination within this conversation.</summary>
    public string? Id { get; init; }
    /// <summary>Destination after this line, or the following line when unset.</summary>
    public string? NextLineId { get; init; }
    /// <summary>Hang up after this line has finished.</summary>
    public bool EndCall { get; init; }
    /// <summary>Show these options when the line finishes, and wait for a selection.</summary>
    public IReadOnlyList<PhoneDialogueOption> Options { get; init; } = Array.Empty<PhoneDialogueOption>();

    // Preserve the original constructor so existing Mods continue to treat
    // their two-argument lines as caller dialogue.
    public PhoneDialogueLine(string speaker, string text)
        : this(speaker, text, PhoneDialogueSpeakerType.Caller)
    {
    }

    public void Deconstruct(out string speaker, out string text)
    {
        speaker = Speaker;
        text = Text;
    }
}

/// <summary>
/// An option shown in the game's native dialogue list. An unset destination
/// follows the containing line's route; EndCall hangs up immediately on selection.
/// </summary>
public sealed record PhoneDialogueOption(
    string Id,
    string Text,
    string? NextLineId = null,
    bool EndCall = false);

public sealed record PhoneConversationDefinition(
    string Key,
    IReadOnlyList<PhoneDialogueLine> Lines)
{
    /// <summary>Optional factory evaluated once when each call begins.</summary>
    internal Func<PhoneCallContext, IEnumerable<PhoneDialogueLine>>? DynamicLinesFactory { get; init; }

    /// <summary>
    /// Optional PNG path relative to this Mod's resource directory. When set,
    /// it replaces the visible TalkPhone caller picture while this conversation is active.
    /// </summary>
    public string? InteractionIconPath { get; init; }
}

/// <summary>Game state captured when a registered phone call begins.</summary>
public sealed record PhoneCallContext(
    string Number,
    string DisplayName,
    GameTimeSnapshot? Time);

public enum PhoneConditionRegistrationStatus
{
    Succeeded,
    AlreadyRegistered,
    Unavailable,
}

public sealed record PhoneConditionRegistrationResult(
    string Key,
    PhoneConditionRegistrationStatus Status,
    string Message)
{
    public bool Succeeded => Status == PhoneConditionRegistrationStatus.Succeeded;

    internal static PhoneConditionRegistrationResult Unavailable(string key) =>
        new(key, PhoneConditionRegistrationStatus.Unavailable,
            "The loader phone runtime is not available.");
}

public enum PhoneConversationRegistrationStatus
{
    Succeeded,
    AlreadyRegistered,
    InvalidDefinition,
    Unavailable,
}

public sealed record PhoneConversationRegistrationResult(
    string Key,
    PhoneConversationRegistrationStatus Status,
    string Message)
{
    public bool Succeeded => Status == PhoneConversationRegistrationStatus.Succeeded;

    internal static PhoneConversationRegistrationResult Unavailable(string key) =>
        new(key, PhoneConversationRegistrationStatus.Unavailable,
            "The loader phone runtime is not available.");
}

public enum PhoneNumberRegistrationStatus
{
    Succeeded,
    DuplicateNumber,
    ConversationNotFound,
    Unavailable,
}

public sealed record PhoneNumberRegistrationResult(
    string Number,
    PhoneNumberRegistrationStatus Status,
    string Message)
{
    public bool Succeeded => Status == PhoneNumberRegistrationStatus.Succeeded;

    internal static PhoneNumberRegistrationResult Unavailable(string number) =>
        new(number, PhoneNumberRegistrationStatus.Unavailable,
            "The loader phone runtime is not available.");
}

public enum PhoneLineOverrideStatus
{
    Succeeded,
    Unavailable,
}

public sealed record PhoneLineOverrideResult(
    string StringKey,
    PhoneLineOverrideStatus Status,
    string Message)
{
    public bool Succeeded => Status == PhoneLineOverrideStatus.Succeeded;

    internal static PhoneLineOverrideResult Unavailable(string stringKey) =>
        new(stringKey, PhoneLineOverrideStatus.Unavailable,
            "The loader phone runtime is not available.");
}

public sealed record PhoneLineDisplayedEvent(
    string? Number,
    string? ConversationKey,
    string? StringKey,
    int SpeakerId,
    string Text,
    bool IsCustomConversation);

public sealed record PhoneOptionSelectedEvent(
    string Number,
    string ConversationKey,
    string? LineId,
    string OptionId,
    string Text);
