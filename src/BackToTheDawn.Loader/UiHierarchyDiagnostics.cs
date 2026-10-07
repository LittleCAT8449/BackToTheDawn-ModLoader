using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

namespace BackToTheDawn.Loader;

/// <summary>
/// Dumps the current scene's UGUI Canvas trees to the BepInEx log on demand.
/// This is a discovery aid for building stable native-UI adapters.
/// </summary>
internal static class UiHierarchyDiagnostics
{
    private const int MaxNodes = 6000;
    private const int MaxCanvases = 64;
    private const int MaxTextLength = 120;

    internal static string DumpActiveScene(
        bool includeInactive,
        bool mainCanvasOnly,
        Action<string> consoleOutput)
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            const string unavailable = "Active scene is not available.";
            Plugin.Logger?.LogWarning($"[UIHierarchy] {unavailable}");
            consoleOutput(unavailable);
            return unavailable;
        }

        var canvasList = new List<Canvas>();
        var sceneRoots = scene.GetRootGameObjects();
        foreach (var sceneRoot in sceneRoots)
        {
            if (sceneRoot is null)
            {
                continue;
            }

            foreach (var canvas in sceneRoot.GetComponentsInChildren<Canvas>(true))
            {
                if (canvas is null || (!includeInactive && !canvas.isActiveAndEnabled))
                {
                    continue;
                }

                if (mainCanvasOnly && !canvas.gameObject.name.StartsWith("Main Canvas", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                canvasList.Add(canvas);
                if (!mainCanvasOnly && canvasList.Count >= MaxCanvases)
                {
                    break;
                }
            }

            if (!mainCanvasOnly && canvasList.Count >= MaxCanvases)
            {
                break;
            }
        }

        var includedCanvasIds = new HashSet<int>(canvasList.Select(canvas => canvas.GetInstanceID()));
        var canvasRoots = canvasList
            .Where(canvas => !HasIncludedCanvasAncestor(canvas.transform, includedCanvasIds))
            .ToArray();

        var mode = includeInactive ? "all, including inactive objects" : "active objects only";
        var scope = mainCanvasOnly ? "Main Canvas only" : "all Canvas roots";
        Log($"Scene='{scene.name}' scope={scope} mode={mode}; sceneRoots={sceneRoots.Length}; " +
            $"canvasComponents={canvasList.Count}; topLevelCanvasRoots={canvasRoots.Length}.");

        var visited = 0;
        foreach (var canvas in canvasRoots)
        {
            if (visited >= MaxNodes)
            {
                break;
            }

            Log($"Canvas root: {BuildPath(canvas.transform)} " +
                $"renderMode={canvas.renderMode} sortingLayer={canvas.sortingLayerName} " +
                $"sortingOrder={canvas.sortingOrder} active={canvas.isActiveAndEnabled}.");
            Visit(canvas.transform, 0, includeInactive, ref visited);
        }

        var truncated = visited >= MaxNodes;
        var summary = canvasRoots.Length == 0
            ? $"No {(includeInactive ? "Canvas" : "active Canvas")} found in scene '{scene.name}'" +
              (mainCanvasOnly ? " with a name starting 'Main Canvas'." : ".")
            : $"UI hierarchy written to BepInEx/LogOutput.log: {canvasRoots.Length} Canvas root(s), " +
              $"{visited} object(s), scope={scope}, mode={mode}" +
              (truncated ? $", stopped at the {MaxNodes}-object limit." : ".");

        if (!mainCanvasOnly && canvasList.Count >= MaxCanvases)
        {
            Log($"Canvas discovery stopped at the {MaxCanvases}-Canvas limit.");
        }

        if (truncated)
        {
            Log($"Object traversal stopped at the {MaxNodes}-object limit.");
        }

        Log(summary);
        consoleOutput(summary);
        return summary;
    }

    private static bool HasIncludedCanvasAncestor(Transform transform, HashSet<int> includedCanvasIds)
    {
        for (var parent = transform.parent; parent is not null; parent = parent.parent)
        {
            var parentCanvas = parent.GetComponent<Canvas>();
            if (parentCanvas is not null && includedCanvasIds.Contains(parentCanvas.GetInstanceID()))
            {
                return true;
            }
        }

        return false;
    }

    private static void Visit(Transform transform, int depth, bool includeInactive, ref int visited)
    {
        if (visited >= MaxNodes)
        {
            return;
        }

        var gameObject = transform.gameObject;
        if (!includeInactive && !gameObject.activeInHierarchy)
        {
            return;
        }

        visited++;
        var indent = new string(' ', Math.Min(depth * 2, 80));
        var sibling = transform.GetSiblingIndex();
        var state = $"activeSelf={gameObject.activeSelf}, active={gameObject.activeInHierarchy}, layer={gameObject.layer}";
        var rect = gameObject.GetComponent<RectTransform>();
        if (rect is not null)
        {
            state += $", pos={FormatVector(rect.anchoredPosition)}, size={FormatVector(rect.sizeDelta)}";
        }

        var componentNames = new List<string>();
        var visibleValues = new List<string>();
        foreach (var component in gameObject.GetComponents<Component>())
        {
            if (component is null)
            {
                continue;
            }

            componentNames.Add(component.GetIl2CppType().FullName ?? component.name);
        }

        // Query these through their generated component types instead of pattern-matching
        // Component proxies returned by GetComponents<Component>(). In IL2CPP the proxy
        // runtime type may not satisfy a managed `is Text` check even when GetIl2CppType()
        // reports UnityEngine.UI.Text.
        AddComponentValue<Text>(gameObject, "Text", text => text.text, visibleValues);
        AddComponentValue<InputField>(gameObject, "Input", input => input.text, visibleValues);
        AddComponentValue<TextMeshProUGUI>(gameObject, "TMP", text => text.text, visibleValues);
        AddComponentValue<TMP_InputField>(gameObject, "TMPInput", input => input.text, visibleValues);
        AddComponentValue<Button>(gameObject, "Button", button => $"interactable={button.interactable}", visibleValues);
        AddComponentValue<Image>(gameObject, "Sprite", image => image.sprite?.name, visibleValues);

        var components = componentNames.Count == 0 ? "none" : string.Join(", ", componentNames);
        var values = visibleValues.Count == 0 ? string.Empty : $" values=[{string.Join("; ", visibleValues)}]";
        Log($"{indent}{gameObject.name}[{sibling}] ({state}) components=[{components}]{values}");

        for (var index = 0; index < transform.childCount && visited < MaxNodes; index++)
        {
            var child = transform.GetChild(index);
            if (child is not null)
            {
                Visit(child, depth + 1, includeInactive, ref visited);
            }
        }
    }

    private static void AddComponentValue<T>(
        GameObject gameObject,
        string label,
        Func<T, string?> getValue,
        List<string> values)
        where T : Component
    {
        try
        {
            var component = gameObject.GetComponent<T>();
            if (component is null)
            {
                return;
            }

            var value = getValue(component);
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            values.Add($"{label}=\"{ClipAndEscape(value)}\"");
        }
        catch (Exception exception)
        {
            values.Add($"{label}=<read failed: {exception.GetType().Name}>");
        }
    }

    private static string BuildPath(Transform transform)
    {
        var parts = new Stack<string>();
        for (var current = transform; current is not null; current = current.parent)
        {
            parts.Push($"{current.name}[{current.GetSiblingIndex()}]");
        }

        return string.Join("/", parts);
    }

    private static string FormatVector(Vector2 value) => $"({value.x:0.##},{value.y:0.##})";

    private static string ClipAndEscape(string value)
    {
        var clipped = value.Length > MaxTextLength ? value[..MaxTextLength] + "…" : value;
        return clipped
            .Replace("\\", "\\\\")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n")
            .Replace("\"", "\\\"");
    }

    private static void Log(string message) => Plugin.Logger?.LogInfo($"[UIHierarchy] {message}");
}
