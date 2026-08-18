using BackToTheDawn.ModAPI;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace BackToTheDawn.Loader;

/// <summary>
/// Loads Mod-owned item assets at runtime. Unity's item table stores only a
/// string for an icon, so custom PNGs are applied after the game's WidgetItem
/// has initialized its normal image.
/// </summary>
internal static class RuntimeItemAssets
{
    private static readonly Dictionary<string, Sprite> LoadedIcons =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> MissingIcons =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> MissingResources =
        new(StringComparer.OrdinalIgnoreCase);

    internal static void ApplyLocalization(Item item)
    {
        var (name, description) = ReadTextResources(item);
        RuntimeItemInjection.AddLocalizationValues(item, name, description);
    }

    internal static bool TryApplyIcon(WidgetItem widget)
    {
        try
        {
            if (widget is null)
            {
                return false;
            }

            var config = widget.itemConfig;
            if (config is null || !ItemIdResolver.TryGetKey(config.item_id, out var key))
            {
                return false;
            }

            var item = ItemRegistry.All.FirstOrDefault(candidate =>
                candidate.Key.Equals(key));
            if (item is null || !item.Resources.HasIcon)
            {
                return false;
            }

            if (!TryGetIcon(item, out var sprite) || sprite is null)
            {
                return false;
            }

            var image = widget.itemImage;
            if (image is null)
            {
                return false;
            }

            image.sprite = sprite;
            image.enabled = true;
            return true;
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogWarning(
                $"[ItemAssets] Applying icon for WidgetItem failed: {exception.Message}");
            return false;
        }
    }

    internal static void Reset()
    {
        foreach (var sprite in LoadedIcons.Values)
        {
            try
            {
                var texture = sprite.texture;
                UnityEngine.Object.Destroy(sprite);
                if (texture is not null)
                {
                    UnityEngine.Object.Destroy(texture);
                }
            }
            catch
            {
                // Unity objects may already have been destroyed during unload.
            }
        }

        LoadedIcons.Clear();
        MissingIcons.Clear();
        MissingResources.Clear();
    }

    private static bool TryGetIcon(Item item, out Sprite? sprite)
    {
        var key = item.Key.ToString();
        if (LoadedIcons.TryGetValue(key, out sprite))
        {
            return true;
        }

        if (MissingIcons.Contains(key))
        {
            sprite = null;
            return false;
        }

        var path = ResolveResourcePath(item, item.Resources.ResolveIconPath);
        if (path is null || !File.Exists(path))
        {
            MissingIcons.Add(key);
            Plugin.Logger?.LogWarning(
                $"[ItemAssets] Icon resource missing for {key}: " +
                $"{item.Resources.IconPath}");
            sprite = null;
            return false;
        }

        try
        {
            var bytes = File.ReadAllBytes(path);
            var texture = new Texture2D(2, 2, TextureFormat.ARGB32, false);
            var data = new Il2CppStructArray<byte>(bytes);
            if (!ImageConversion.LoadImage(texture, data, true))
            {
                UnityEngine.Object.Destroy(texture);
                throw new InvalidDataException("Unity rejected the image bytes.");
            }

            sprite = Sprite.Create(
                texture,
                new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f));
            sprite.name = key + ".icon";
            LoadedIcons[key] = sprite;
            Plugin.Logger?.LogInfo(
                $"[ItemAssets] Loaded icon for {key} from '{path}'.");
            return true;
        }
        catch (Exception exception)
        {
            MissingIcons.Add(key);
            Plugin.Logger?.LogWarning(
                $"[ItemAssets] Could not load icon for {key}: {exception.Message}");
            sprite = null;
            return false;
        }
    }

    private static (string Name, string Description) ReadTextResources(Item item)
    {
        var name = item.DisplayName;
        var description = item.BackgroundDescription;
        if (!TryGetDescriptor(item, out var descriptor))
        {
            return (name, description);
        }

        var resources = new ModResources(descriptor.ResourceDirectory);
        if (item.Resources.HasName &&
            TryReadText(item.Key, resources, item.Resources.NamePath, out var nameText))
        {
            name = nameText;
        }

        if (item.Resources.HasDescription &&
            TryReadText(
                item.Key,
                resources,
                item.Resources.DescriptionPath,
                out var descriptionText))
        {
            description = descriptionText;
        }

        return (name, description);
    }

    private static bool TryReadText(
        ItemKey key,
        ModResources resources,
        string relativePath,
        out string value)
    {
        value = string.Empty;
        try
        {
            var path = resources.GetPath(relativePath);
            if (!File.Exists(path))
            {
                WarnMissingResource(key, relativePath);
                return false;
            }

            value = File.ReadAllText(path).Trim();
            return value.Length > 0;
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogWarning(
                $"[ItemAssets] Could not read text resource '{relativePath}' for {key}: " +
                exception.Message);
            return false;
        }
    }

    private static string? ResolveResourcePath(
        Item item,
        Func<ModResources, string?> resolver)
    {
        if (!TryGetDescriptor(item, out var descriptor))
        {
            WarnMissingResource(item.Key, "mod resource directory");
            return null;
        }

        try
        {
            return resolver(new ModResources(descriptor.ResourceDirectory));
        }
        catch (Exception exception)
        {
            Plugin.Logger?.LogWarning(
                $"[ItemAssets] Invalid resource path for {item.Key}: {exception.Message}");
            return null;
        }
    }

    private static bool TryGetDescriptor(Item item, out ModDescriptor descriptor)
    {
        descriptor = ModRegistry.DiscoveredMods.FirstOrDefault(candidate =>
            candidate.Manifest.Id.Equals(item.Key.Namespace, StringComparison.OrdinalIgnoreCase))!;
        return descriptor is not null;
    }

    private static void WarnMissingResource(ItemKey key, string path)
    {
        var token = key + ":" + path;
        if (MissingResources.Add(token))
        {
            Plugin.Logger?.LogWarning(
                $"[ItemAssets] Resource '{path}' for {key} could not be resolved.");
        }
    }
}

[HarmonyPatch(typeof(WidgetItem), "UpdateImage")]
internal static class WidgetItemUpdateImagePatch
{
    private static void Postfix(WidgetItem __instance) =>
        RuntimeItemAssets.TryApplyIcon(__instance);
}
