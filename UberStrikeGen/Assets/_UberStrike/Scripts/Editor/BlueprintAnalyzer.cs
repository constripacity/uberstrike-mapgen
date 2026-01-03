using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class BlueprintImport
{
    [MenuItem("Tools/UnityAI/Blueprints/Validate PNG Import...")]
    public static void ValidatePngImport()
    {
        var tex = Selection.activeObject as Texture2D;
        if (!tex)
        {
            Debug.LogError("Select your blueprint PNG Texture2D first.");
            return;
        }

        string path = AssetDatabase.GetAssetPath(tex);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        bool changed = false;

        if (!ti.isReadable) { ti.isReadable = true; changed = true; }
        if (ti.textureCompression != TextureImporterCompression.Uncompressed) { ti.textureCompression = TextureImporterCompression.Uncompressed; changed = true; }
        if (ti.mipmapEnabled) { ti.mipmapEnabled = false; changed = true; }
        if (ti.filterMode != FilterMode.Point) { ti.filterMode = FilterMode.Point; changed = true; }
        if (ti.wrapMode != TextureWrapMode.Clamp) { ti.wrapMode = TextureWrapMode.Clamp; changed = true; }
        if (!ti.sRGBTexture) { ti.sRGBTexture = true; changed = true; }

        if (changed)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            Debug.Log($"✅ Fixed import settings for {path}");
        }
        else
        {
            Debug.Log($"✅ Import settings already correct for {path}");
        }
    }

    [MenuItem("Tools/UnityAI/Blueprints/Analyze Colors...")]
    public static void AnalyzeColors()
    {
        var tex = Selection.activeObject as Texture2D;
        if (!tex)
        {
            Debug.LogError("Select your blueprint PNG Texture2D first.");
            return;
        }

        var pixels = tex.GetPixels32();
        var map = new Dictionary<Color32, int>(new Color32Comparer());
        foreach (var p in pixels)
        {
            if (map.TryGetValue(p, out int c)) map[p] = c + 1;
            else map[p] = 1;
        }

        var sorted = map.OrderByDescending(kv => kv.Value).ToList();

        string dir = "Assets/_UberStrike/Diagnostics";
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        string csvPath = Path.Combine(dir, $"{tex.name}_color_histogram.csv");

        using (var sw = new StreamWriter(csvPath))
        {
            sw.WriteLine("R,G,B,A,Count");
            foreach (var kv in sorted)
                sw.WriteLine($"{kv.Key.r},{kv.Key.g},{kv.Key.b},{kv.Key.a},{kv.Value}");
        }

        AssetDatabase.Refresh();
        Debug.Log($"🧪 Color histogram written: {csvPath}\nTop 10:");
        for (int i = 0; i < Mathf.Min(10, sorted.Count); i++)
        {
            var k = sorted[i];
            Debug.Log($"#{i + 1} RGB({k.Key.r},{k.Key.g},{k.Key.b}) -> {k.Value} px");
        }
    }

    private class Color32Comparer : IEqualityComparer<Color32>
    {
        public bool Equals(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
        public int GetHashCode(Color32 c) => (c.r << 24) ^ (c.g << 16) ^ (c.b << 8) ^ c.a;
    }
}
