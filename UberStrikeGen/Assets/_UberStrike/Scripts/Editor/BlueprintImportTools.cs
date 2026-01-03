using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.IO;
using System;
using System.Linq;

/// <summary>
/// Editor utilities:
/// - Tools/UberStrike/Fix Blueprint Import Settings
/// - Tools/UberStrike/Test Complex_Test_map_1
/// - Tools/UberStrike/Test Auto_28086
/// - Tools/UberStrike/Test Specific (callable)
/// </summary>
public static class BlueprintImportTools
{
    [MenuItem("Tools/UberStrike/Fix Blueprint Import Settings")]
    public static void FixBlueprintSettings()
    {
        string blueprintsFolder = "Assets/_UberStrike/Blueprints";
        var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { blueprintsFolder });
        if (guids == null || guids.Length == 0)
        {
            Debug.LogWarning($"[BlueprintImportTools] No textures found in: {blueprintsFolder}");
            return;
        }

        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) continue;

            bool changed = false;
            if (!importer.isReadable) { importer.isReadable = true; changed = true; }
            if (importer.textureCompression != TextureImporterCompression.Uncompressed) { importer.textureCompression = TextureImporterCompression.Uncompressed; changed = true; }
            if (importer.filterMode != FilterMode.Point) { importer.filterMode = FilterMode.Point; changed = true; }
            if (importer.mipmapEnabled) { importer.mipmapEnabled = false; changed = true; }
            if (importer.sRGBTexture) { importer.sRGBTexture = false; changed = true; }
            if (importer.textureType != TextureImporterType.Default) { importer.textureType = TextureImporterType.Default; changed = true; }
            if (importer.npotScale != TextureImporterNPOTScale.None) { importer.npotScale = TextureImporterNPOTScale.None; changed = true; }

            if (changed)
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                Debug.Log($"[BlueprintImportTools] Fixed import settings for: {path}");
            }
        }

        AssetDatabase.Refresh();
        Debug.Log("[BlueprintImportTools] Blueprint import settings fix complete.");
    }

    [MenuItem("Tools/UberStrike/Test Complex_Test_map_1")]
    public static void TestBlueprint1()
    {
        TestSpecificBlueprint("Complex_Test_map_1.png");
    }

    [MenuItem("Tools/UberStrike/Test Auto_28086")]
    public static void TestBlueprint2()
    {
        TestSpecificBlueprint("Auto_28086.png");
    }

    public static void TestSpecificBlueprint(string filename)
    {
        string path = $"Assets/_UberStrike/Blueprints/MapLayouts/{filename}";
        Debug.Log($"=== TESTING {filename} === path={path}");

        if (!File.Exists(path) && AssetDatabase.LoadAssetAtPath<Texture2D>(path) == null)
        {
            Debug.LogError($"[BlueprintImportTools] Blueprint not found at: {path}");
            return;
        }

        // New empty scene
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects);

        // Clear previously generated arenas or objects named with typical prefixes
        var all = UnityEngine.Object.FindObjectsOfType<GameObject>();
        foreach (var obj in all)
        {
            if (obj == null) continue;
            string n = obj.name ?? "";
            if (n.StartsWith("Arena_") || n.Contains("Generated") || n.Contains("Walls_Combined") || n.Contains("Floors") || n.Contains("Spawns"))
            {
                UnityEngine.Object.DestroyImmediate(obj);
            }
        }

        try
        {
            // Ensure import settings are correct before building
            var assetImporter = AssetImporter.GetAtPath(path) as TextureImporter;
            if (assetImporter != null)
            {
                // Apply critical blueprint import fixes
                if (!assetImporter.isReadable || assetImporter.filterMode != FilterMode.Point || assetImporter.textureCompression != TextureImporterCompression.Uncompressed)
                {
                    assetImporter.isReadable = true;
                    assetImporter.filterMode = FilterMode.Point;
                    assetImporter.textureCompression = TextureImporterCompression.Uncompressed;
                    assetImporter.mipmapEnabled = false;
                    assetImporter.sRGBTexture = false;
                    assetImporter.npotScale = TextureImporterNPOTScale.None;
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                    Debug.Log($"[BlueprintImportTools] Applied import fixes to {path} before testing.");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[BlueprintImportTools] Exception while fixing import settings: {ex}");
        }

        // Call the builder (use 1.0 mpp)
        try
        {
            BuildFromBlueprint.BuildFromPNGPath(path, 1.0f);
            Debug.Log($"=== COMPLETED {filename} at {DateTime.Now} ===");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[BlueprintImportTools] BuildFromPNGPath threw: {ex}");
        }
    }
}
