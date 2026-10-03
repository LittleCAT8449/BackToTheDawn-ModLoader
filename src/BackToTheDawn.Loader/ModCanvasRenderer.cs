using System.IO;
using BackToTheDawn.ModAPI;
using UnityEngine;
using UnityEngine.UI;
using UiImage = UnityEngine.UI.Image;

namespace BackToTheDawn.Loader;

internal sealed class ModCanvasRenderer : MonoBehaviour
{
    private readonly Dictionary<string, GameObject> _roots = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<RectTransform, Action> _clickTargets = new();
    private int _version = -1;
    private RectTransform? _dragRoot;
    private Vector3 _lastMouse;

    public ModCanvasRenderer(System.IntPtr pointer) : base(pointer)
    {
    }

    private void Update()
    {
        if (_version != ModApi.Canvas.Version)
        {
            Rebuild();
        }

        UpdateDragging();
    }

    private void Rebuild()
    {
        foreach (var root in _roots.Values)
        {
            if (root is not null)
            {
                Destroy(root);
            }
        }

        _roots.Clear();
        _dragRoot = null;
        _version = ModApi.Canvas.Version;

        foreach (var registration in ModApi.Canvas.Registered)
        {
            try
            {
                _roots[registration.Id] = BuildCanvas(registration);
            }
            catch (System.Exception exception)
            {
                Plugin.Logger?.LogError(
                    $"[Gui] Canvas '{registration.Id}' failed to build: {exception}");
            }
        }
    }

    private GameObject BuildCanvas(CanvasRegistration registration)
    {
        var options = registration.Options;
        var style = options.Style ?? new CanvasStyle();
        var root = new GameObject("BackToTheDawn.Canvas." + registration.Id);
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 4000;
        root.AddComponent<CanvasScaler>();
        root.AddComponent<GraphicRaycaster>();

        var rootRect = root.GetComponent<RectTransform>();
        rootRect.anchorMin = new Vector2(0f, 1f);
        rootRect.anchorMax = new Vector2(0f, 1f);
        rootRect.pivot = new Vector2(0f, 1f);
        rootRect.anchoredPosition = new Vector2(options.X, -options.Y);
        rootRect.sizeDelta = new Vector2(options.Width, options.Height);

        var background = root.AddComponent<UiImage>();
        background.color = ParseColor(style.BackgroundColor, new Color(0.1f, 0.12f, 0.18f, 0.9f));
        var content = CreateContainer(root.transform, CanvasLayout.Vertical, style.Spacing);
        var contentRect = (RectTransform)content.transform;
        contentRect.anchorMin = Vector2.zero;
        contentRect.anchorMax = Vector2.one;
        contentRect.offsetMin = new Vector2(style.Padding, style.Padding);
        contentRect.offsetMax = new Vector2(-style.Padding, -style.Padding);

        var builder = new CanvasBuilder();
        registration.Build(builder);
        foreach (var element in builder.Elements)
        {
            CreateElement(content.transform, element, style);
        }

        return root;
    }

    private void CreateElement(Transform parent, CanvasElement element, CanvasStyle inherited)
    {
        switch (element)
        {
            case CanvasLabel label:
                CreateLabel(parent, label.Text, label.Style ?? inherited);
                break;
            case CanvasButton button:
                CreateButton(parent, button, button.Style ?? inherited);
                break;
            case CanvasImage image:
                CreateImage(parent, image);
                break;
            case BackToTheDawn.ModAPI.CanvasGroup group:
                var container = CreateContainer(parent, group.Layout, group.Spacing);
                foreach (var child in group.Children)
                {
                    CreateElement(container.transform, child, inherited);
                }

                break;
        }
    }

    private static GameObject CreateContainer(
        Transform parent,
        CanvasLayout layout,
        float spacing)
    {
        var container = new GameObject("Layout");
        container.transform.SetParent(parent, false);
        var rect = container.AddComponent<RectTransform>();
        var layoutGroup = layout == CanvasLayout.Horizontal
            ? (HorizontalOrVerticalLayoutGroup)container.AddComponent<HorizontalLayoutGroup>()
            : container.AddComponent<VerticalLayoutGroup>();
        layoutGroup.spacing = spacing;
        layoutGroup.childForceExpandWidth = true;
        layoutGroup.childForceExpandHeight = false;
        layoutGroup.childControlWidth = true;
        layoutGroup.childControlHeight = true;
        var fitter = container.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        return container;
    }

    private static void CreateLabel(Transform parent, string value, CanvasStyle style)
    {
        var text = CreateText(parent, value, style);
        var layout = text.gameObject.AddComponent<LayoutElement>();
        layout.preferredHeight = style.FontSize + 8f;
    }

    private void CreateButton(Transform parent, CanvasButton definition, CanvasStyle style)
    {
        var buttonObject = new GameObject("Button." + definition.Text);
        buttonObject.transform.SetParent(parent, false);
        var image = buttonObject.AddComponent<UiImage>();
        image.color = ParseColor(style.AccentColor, new Color(0.2f, 0.55f, 0.9f, 1f));
        buttonObject.AddComponent<Button>();
        var layout = buttonObject.AddComponent<LayoutElement>();
        layout.preferredHeight = style.FontSize + 14f;
        var buttonRect = buttonObject.GetComponent<RectTransform>() ?? buttonObject.AddComponent<RectTransform>();
        _clickTargets[buttonRect] = definition.OnClick;
        var text = CreateText(buttonObject.transform, definition.Text, style);
        var textRect = text.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
    }

    private static void CreateImage(Transform parent, CanvasImage definition)
    {
        var imageObject = new GameObject("Image." + Path.GetFileName(definition.Path));
        imageObject.transform.SetParent(parent, false);
        var image = imageObject.AddComponent<UiImage>();
        image.preserveAspect = true;
        var layout = imageObject.AddComponent<LayoutElement>();
        layout.preferredWidth = definition.Width;
        layout.preferredHeight = definition.Height;

        try
        {
            if (!File.Exists(definition.Path))
            {
                return;
            }

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (texture.LoadImage(File.ReadAllBytes(definition.Path)))
            {
                image.sprite = Sprite.Create(
                    texture,
                    new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f));
            }
        }
        catch (System.Exception exception)
        {
            Plugin.Logger?.LogWarning(
                $"[Gui] Failed to load image '{definition.Path}': {exception.Message}");
        }
    }

    private static Text CreateText(Transform parent, string value, CanvasStyle style)
    {
        var textObject = new GameObject("Text");
        textObject.transform.SetParent(parent, false);
        var text = textObject.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.fontSize = Mathf.Max(8, style.FontSize);
        text.color = ParseColor(style.TextColor, Color.white);
        text.alignment = TextAnchor.MiddleLeft;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.text = value;
        return text;
    }

    private void UpdateDragging()
    {
        if (!Input.GetMouseButtonDown(0) && !Input.GetMouseButton(0))
        {
            return;
        }

        if (Input.GetMouseButtonDown(0))
        {
            foreach (var target in _clickTargets)
            {
                if (RectTransformUtility.RectangleContainsScreenPoint(target.Key, Input.mousePosition))
                {
                    try { target.Value(); }
                    catch (System.Exception exception) { Plugin.Logger?.LogError($"[Gui] Button callback failed: {exception}"); }
                    return;
                }
            }
            _dragRoot = null;
            foreach (var root in _roots.Values)
            {
                var rect = root.GetComponent<RectTransform>();
                var registration = ModApi.Canvas.Registered.FirstOrDefault(item =>
                    string.Equals(item.Id, root.name["BackToTheDawn.Canvas.".Length..], StringComparison.OrdinalIgnoreCase));
                if (registration?.Options.Draggable == true &&
                    RectTransformUtility.RectangleContainsScreenPoint(rect, Input.mousePosition))
                {
                    _dragRoot = rect;
                    _lastMouse = Input.mousePosition;
                    break;
                }
            }
        }

        if (_dragRoot is not null && Input.GetMouseButton(0))
        {
            var current = Input.mousePosition;
            var delta = current - _lastMouse;
            _dragRoot.anchoredPosition += new Vector2(delta.x, -delta.y);
            _lastMouse = current;
        }

        if (Input.GetMouseButtonUp(0))
        {
            _dragRoot = null;
        }
    }

    private static Color ParseColor(string value, Color fallback)
    {
        return ColorUtility.TryParseHtmlString(value, out var color)
            ? color
            : fallback;
    }
}
