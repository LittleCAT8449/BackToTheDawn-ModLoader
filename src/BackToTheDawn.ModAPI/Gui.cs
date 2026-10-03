namespace BackToTheDawn.ModAPI;

public sealed class GuiApi
{
    private readonly object _gate = new();
    private readonly Dictionary<string, GuiPanelRegistration> _panels =
        new(StringComparer.OrdinalIgnoreCase);

    internal GuiApi()
    {
    }

    public IReadOnlyList<GuiPanelRegistration> Registered
    {
        get
        {
            lock (_gate)
            {
                return _panels.Values.ToArray();
            }
        }
    }

    public IDisposable RegisterPanel(string id, Action<GuiPanelContext> draw)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("GUI panel id cannot be empty.", nameof(id));
        }

        ArgumentNullException.ThrowIfNull(draw);
        var key = id.Trim();
        lock (_gate)
        {
            _panels[key] = new GuiPanelRegistration(key, draw);
        }

        return new RegistrationHandle(this, key);
    }

    internal void Unregister(string id)
    {
        lock (_gate)
        {
            _panels.Remove(id);
        }
    }

    private sealed class RegistrationHandle : IDisposable
    {
        private GuiApi? _owner;
        private readonly string _id;

        internal RegistrationHandle(GuiApi owner, string id)
        {
            _owner = owner;
            _id = id;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, null)?.Unregister(_id);
        }
    }
}

public sealed record GuiPanelRegistration(
    string Id,
    Action<GuiPanelContext> Draw);

public sealed class GuiPanelContext
{
    private readonly Action<string> _label;
    private readonly Func<string, bool> _button;
    private readonly Func<string, bool, bool> _toggle;
    private readonly Func<string, string> _textField;

    internal GuiPanelContext(
        Action<string> label,
        Func<string, bool> button,
        Func<string, bool, bool> toggle,
        Func<string, string> textField)
    {
        _label = label;
        _button = button;
        _toggle = toggle;
        _textField = textField;
    }

    public void Label(string text) => _label(text ?? string.Empty);

    public bool Button(string text) => _button(text ?? string.Empty);

    public bool Toggle(string label, bool value) =>
        _toggle(label ?? string.Empty, value);

    public string TextField(string value) => _textField(value ?? string.Empty);
}
