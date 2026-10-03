using BackToTheDawn.ModAPI;
using UnityEngine;
using UnityEngine.Rendering;

namespace BackToTheDawn.Loader;

internal static class AssetBundlePrefabDiagnostics
{
    internal static void Inspect(
        GameObject prefab,
        string bundlePath,
        string assetName,
        IModLogger logger)
    {
        try
        {
            var renderers = prefab.GetComponentsInChildren<Renderer>(includeInactive: true);
            for (var rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                var renderer = renderers[rendererIndex];
                if (renderer is null)
                {
                    continue;
                }

                var materials = renderer.sharedMaterials;
                for (var materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    var material = materials[materialIndex];
                    if (material is null)
                    {
                        logger.Warning(
                            $"Prefab '{assetName}' in '{bundlePath}' has an empty material slot " +
                            $"(renderer {rendererIndex}, slot {materialIndex}).");
                        continue;
                    }

                    var shader = material.shader;
                    if (shader is null)
                    {
                        logger.Warning(
                            $"Material '{material.name}' on prefab '{assetName}' in '{bundlePath}' " +
                            "has no shader. The shader may be missing or unsupported by the game.");
                        continue;
                    }

                    InspectTextureProperties(
                        shader,
                        material,
                        rendererIndex,
                        materialIndex,
                        bundlePath,
                        assetName,
                        logger);
                }
            }
        }
        catch (Exception exception)
        {
            // Diagnostics must never turn a successfully loaded asset into a failed load.
            logger.Warning(
                $"Material diagnostics for prefab '{assetName}' in '{bundlePath}' could not finish: " +
                $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private static void InspectTextureProperties(
        Shader shader,
        Material material,
        int rendererIndex,
        int materialIndex,
        string bundlePath,
        string assetName,
        IModLogger logger)
    {
        for (var propertyIndex = 0; propertyIndex < shader.GetPropertyCount(); propertyIndex++)
        {
            if (shader.GetPropertyType(propertyIndex) != ShaderPropertyType.Texture)
            {
                continue;
            }

            var propertyName = shader.GetPropertyName(propertyIndex);
            if (string.IsNullOrWhiteSpace(propertyName) || material.GetTexture(propertyName) is not null)
            {
                continue;
            }

            logger.Warning(
                $"Material '{material.name}' on prefab '{assetName}' in '{bundlePath}' has no " +
                $"texture assigned to shader property '{propertyName}' " +
                $"(renderer {rendererIndex}, slot {materialIndex}).");
        }
    }
}
