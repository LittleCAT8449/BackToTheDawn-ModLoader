#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BackToTheDawn.AssetBundleProbe.Editor;

public static class BuildProbeBundle
{
    private const string ExpectedUnityVersion = "2020.3.2f1c1";
    private const string PrefabAssetPath =
        "Assets/BackToTheDawnAssetBundleProbe/ResourceProbePrefab.prefab";
    private const string BundleName = "test.bundle";
    private const string AssetAddress = "ResourceProbePrefab";

    [MenuItem("Tools/Back To The Dawn/Build AssetBundle Probe")]
    private static void Build()
    {
        if (!Application.unityVersion.StartsWith("2020.3.", StringComparison.Ordinal))
        {
            EditorUtility.DisplayDialog(
                "Wrong Unity version",
                $"The game uses Unity {ExpectedUnityVersion}. This editor is " +
                $"{Application.unityVersion}; build with Unity 2020.3 to avoid an " +
                "incompatible AssetBundle.",
                "OK");
            return;
        }

        if (Application.unityVersion != ExpectedUnityVersion &&
            !EditorUtility.DisplayDialog(
                "Unity patch version differs",
                $"The game uses Unity {ExpectedUnityVersion}, but this editor is " +
                $"{Application.unityVersion}. Continue only if you accept that the game " +
                "may reject the bundle.",
                "Continue",
                "Cancel"))
        {
            return;
        }

        try
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var prefabFullPath = Path.Combine(projectRoot, PrefabAssetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(prefabFullPath)!);

            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabAssetPath) is null)
            {
                if (File.Exists(prefabFullPath))
                {
                    throw new IOException(
                        $"A file already exists at '{PrefabAssetPath}', but it is not a Prefab. " +
                        "Move it manually before running this builder.");
                }

                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = AssetAddress;
                try
                {
                    if (PrefabUtility.SaveAsPrefabAsset(cube, PrefabAssetPath) is null)
                    {
                        throw new InvalidOperationException("Unity could not save the probe Prefab.");
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(cube);
                }
            }

            AssetDatabase.Refresh();
            var build = new AssetBundleBuild
            {
                assetBundleName = BundleName,
                assetNames = new[] { PrefabAssetPath },
                addressableNames = new[] { AssetAddress }
            };
            var outputDirectory = Path.Combine(projectRoot, "AssetBundleProbeOutput");
            var manifest = BuildPipeline.BuildAssetBundles(
                outputDirectory,
                new[] { build },
                BuildAssetBundleOptions.None,
                BuildTarget.StandaloneWindows64);
            if (manifest is null)
            {
                throw new InvalidOperationException(
                    "Unity failed to build the AssetBundle. Check the Console for details.");
            }

            var bundlePath = Path.Combine(outputDirectory, BundleName);
            if (!File.Exists(bundlePath))
            {
                throw new FileNotFoundException("The built AssetBundle was not produced.", bundlePath);
            }

            var resourceDirectory = EditorUtility.OpenFolderPanel(
                "Select BackToTheDawn.AssetBundleProbe/resource",
                projectRoot,
                string.Empty);
            if (string.IsNullOrWhiteSpace(resourceDirectory))
            {
                Debug.Log("AssetBundle built, but deployment was cancelled: " + bundlePath);
                return;
            }

            var targetPath = Path.Combine(resourceDirectory, "probe", BundleName);
            if (File.Exists(targetPath) &&
                !EditorUtility.DisplayDialog(
                    "Replace existing probe bundle?",
                    $"This will overwrite:\n{targetPath}",
                    "Replace",
                    "Cancel"))
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(bundlePath, targetPath, overwrite: true);
            Debug.Log(
                $"Built Unity {Application.unityVersion} AssetBundle and copied it to: {targetPath}");
            EditorUtility.DisplayDialog(
                "AssetBundle probe built",
                $"The test bundle is ready:\n{targetPath}\n\nRestart Back To The Dawn through Steam to run the probe.",
                "OK");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog("AssetBundle build failed", exception.Message, "OK");
        }
    }
}
#endif
