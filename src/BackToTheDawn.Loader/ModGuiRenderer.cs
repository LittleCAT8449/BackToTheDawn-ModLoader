using BackToTheDawn.ModAPI;
using UnityEngine;

namespace BackToTheDawn.Loader;

internal sealed class ModGuiRenderer : MonoBehaviour
{
    private const float PanelWidth = 360f;
    private const float PanelHeight = 240f;
    private const float PanelGap = 8f;

    public ModGuiRenderer(System.IntPtr pointer) : base(pointer)
    {
    }

    private void OnGUI()
    {
        var panels = ModApi.Gui.Registered;
        for (var index = 0; index < panels.Count; index++)
        {
            var panel = panels[index];
            var x = Mathf.Max(16f, Screen.width - PanelWidth - 16f);
            var y = 16f + index * (PanelHeight + PanelGap);
            var area = new Rect(x, y, PanelWidth, PanelHeight);
            GUI.Box(area, panel.Id);
            GUILayout.BeginArea(new Rect(area.x + 10f, area.y + 26f, area.width - 20f, area.height - 36f));
            var context = new GuiPanelContext(
                text => GUILayout.Label(text),
                text => GUILayout.Button(text),
                (label, value) => GUILayout.Toggle(value, label),
                value => GUILayout.TextField(value));
            try
            {
                panel.Draw(context);
            }
            catch (System.Exception exception)
            {
                GUILayout.Label("GUI error: " + exception.Message);
                Plugin.Logger?.LogError($"[Gui] Panel '{panel.Id}' failed: {exception}");
            }

            GUILayout.EndArea();
        }
    }
}
