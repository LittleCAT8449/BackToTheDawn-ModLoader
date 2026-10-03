namespace BackToTheDawn.ModAPI;

public sealed class CanvasApi
{
    private readonly object _gate = new();
    private readonly Dictionary<string, CanvasRegistration> _canvases =
        new(StringComparer.OrdinalIgnoreCase);
    private int _version;

    internal CanvasApi()
    {
    }

    public int Version
    {
        get
        {
            lock (_gate)
            {
                return _version;
            }
        }
    }

    public IReadOnlyList<CanvasRegistration> Registered
    {
        get
        {
            lock (_gate)
            {
                return _canvases.Values.ToArray();
            }
        }
    }

    public IDisposable RegisterCanvas(
        string id,
        Action<CanvasBuilder> build,
        CanvasOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Canvas id cannot be empty.", nameof(id));
        }

        ArgumentNullException.ThrowIfNull(build);
        var registration = new CanvasRegistration(id.Trim(), build, options ?? new());
        lock (_gate)
        {
            _canvases[registration.Id] = registration;
            _version++;
        }

        return new RegistrationHandle(this, registration);
    }

    /// <summary>Creates a retained-mode, fluent Canvas window.</summary>
    public CanvasWindow CreateWindow(string id, CanvasOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Canvas id cannot be empty.", nameof(id));
        }

        var window = new CanvasWindow(this, id.Trim(), options ?? new());
        lock (_gate)
        {
            _canvases[window.Registration.Id] = window.Registration;
            _version++;
        }

        return window;
    }

    internal void Touch()
    {
        lock (_gate)
        {
            _version++;
        }
    }
    internal void Unregister(CanvasRegistration registration)
    {
        lock (_gate)
        {
            if (_canvases.TryGetValue(registration.Id, out var current) &&
                ReferenceEquals(current, registration))
            {
                _canvases.Remove(registration.Id);
                _version++;
            }
        }
    }

    private sealed class RegistrationHandle : IDisposable
    {
        private CanvasApi? _owner;
        private readonly CanvasRegistration _registration;

        internal RegistrationHandle(CanvasApi owner, CanvasRegistration registration)
        {
            _owner = owner;
            _registration = registration;
        }

        public void Dispose() =>
            Interlocked.Exchange(ref _owner, null)?.Unregister(_registration);
    }
}

/// <summary>Retained-mode Canvas window with a fluent construction API.</summary>
public sealed class CanvasWindow : IDisposable
{
    private readonly CanvasApi _owner;
    private readonly CanvasBuilder _builder = new();
    private readonly CanvasRegistration _registration;
    private int _disposed;

    internal CanvasWindow(CanvasApi owner, string id, CanvasOptions options)
    {
        _owner = owner;
        _registration = new CanvasRegistration(id, Build, options);
    }

    internal CanvasRegistration Registration => _registration;
    public string Id => _registration.Id;

    public CanvasWindow Label(string text, CanvasStyle? style = null)
    {
        _builder.Label(text, style);
        _owner.Touch();
        return this;
    }

    public CanvasWindow Button(string text, Action onClick, CanvasStyle? style = null)
    {
        _builder.Button(text, onClick, style);
        _owner.Touch();
        return this;
    }

    public CanvasWindow Image(string path, float width = 64f, float height = 64f)
    {
        _builder.Image(path, width, height);
        _owner.Touch();
        return this;
    }

    public CanvasWindow BeginVertical(float spacing = 6f)
    {
        _builder.BeginVertical(spacing);
        _owner.Touch();
        return this;
    }

    public CanvasWindow BeginHorizontal(float spacing = 6f)
    {
        _builder.BeginHorizontal(spacing);
        _owner.Touch();
        return this;
    }

    public CanvasWindow EndLayout()
    {
        _builder.EndLayout();
        _owner.Touch();
        return this;
    }

    private void Build(CanvasBuilder target)
    {
        foreach (var element in _builder.Elements)
        {
            target.AddElement(element);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _owner.Unregister(_registration);
        }
    }
}
public sealed class CanvasRegistration
{
    internal CanvasRegistration(
        string id,
        Action<CanvasBuilder> build,
        CanvasOptions options)
    {
        Id = id;
        Build = build;
        Options = options;
    }

    public string Id { get; }
    public Action<CanvasBuilder> Build { get; }
    public CanvasOptions Options { get; }
}

public sealed record CanvasOptions(
    float Width = 360f,
    float Height = 260f,
    float X = 24f,
    float Y = 24f,
    bool Draggable = true,
    CanvasStyle? Style = null);

public sealed record CanvasStyle(
    string BackgroundColor = "#18202DEE",
    string TextColor = "#FFFFFFFF",
    string AccentColor = "#4AA3FFFF",
    int FontSize = 14,
    float Padding = 10f,
    float Spacing = 6f);

public sealed class CanvasBuilder
{
    private readonly List<CanvasElement> _elements = new();
    private readonly Stack<CanvasGroup> _groups = new();

    public IReadOnlyList<CanvasElement> Elements => _elements;

    public void Label(string text, CanvasStyle? style = null) =>
        Add(new CanvasLabel(text ?? string.Empty, style));

    public void Button(string text, Action onClick, CanvasStyle? style = null) =>
        Add(new CanvasButton(text ?? string.Empty, onClick ?? (() => { }), style));

    public void Image(string path, float width = 64f, float height = 64f) =>
        Add(new CanvasImage(path ?? string.Empty, width, height));

    public void BeginVertical(float spacing = 6f) =>
        BeginGroup(CanvasLayout.Vertical, spacing);

    public void BeginHorizontal(float spacing = 6f) =>
        BeginGroup(CanvasLayout.Horizontal, spacing);

    public void EndLayout()
    {
        if (_groups.Count > 0)
        {
            _groups.Pop();
        }
    }

    private void BeginGroup(CanvasLayout layout, float spacing)
    {
        var group = new CanvasGroup(layout, spacing);
        Add(group);
        _groups.Push(group);
    }

    private void Add(CanvasElement element)
    {
        if (_groups.Count > 0)
        {
            _groups.Peek().Children.Add(element);
        }
        else
        {
            _elements.Add(element);
        }
    }

    internal void AddElement(CanvasElement element) => Add(element);
}

public abstract record CanvasElement;

public sealed record CanvasLabel(
    string Text,
    CanvasStyle? Style = null) : CanvasElement;

public sealed record CanvasButton(
    string Text,
    Action OnClick,
    CanvasStyle? Style = null) : CanvasElement;

public sealed record CanvasImage(
    string Path,
    float Width,
    float Height) : CanvasElement;

public sealed record CanvasGroup(CanvasLayout Layout, float Spacing) : CanvasElement
{
    public List<CanvasElement> Children { get; } = new();
}

public enum CanvasLayout
{
    Vertical,
    Horizontal,
}
