using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class PNGDebugTester
{
    [MenuItem("Tools/Debug/Test PNG Loading NOW")]
    public static void TestPNGLoading()
    {
        string[] blueprints = {
            "Complex_Test_map_1.png",
            "Auto_28086.png",
            "simple_arena.png"
        };

        foreach (string filename in blueprints)
        {
            string fullPath = $"C:/UberStrikeGen/Assets/_UberStrike/Blueprints/MapLayouts/{filename}";

            Debug.Log($"[PNG DEBUG] Checking: {fullPath}");
            if (!File.Exists(fullPath))
            {
                Debug.LogError($"FILE NOT FOUND: {fullPath}");
                continue;
            }

            try
            {
                byte[] bytes = File.ReadAllBytes(fullPath);
                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                bool ok = tex.LoadImage(bytes);
                if (!ok)
                {
                    Debug.LogError($"LoadImage failed for {filename}");
                    continue;
                }

                // Build color histogram sampling up to 100x100 region
                Dictionary<Color32, int> colors = new Dictionary<Color32, int>();
                int sampleW = Math.Min(100, tex.width);
                int sampleH = Math.Min(100, tex.height);
                for (int i = 0; i < sampleW; i++)
                {
                    for (int j = 0; j < sampleH; j++)
                    {
                        Color ccol = tex.GetPixel(i, j);
                        Color32 c = ccol;
                        int qR = ((int)c.r / 50) * 50;
                        int qG = ((int)c.g / 50) * 50;
                        int qB = ((int)c.b / 50) * 50;
                        Color32 qc = new Color32((byte)qR, (byte)qG, (byte)qB, 255);
                        if (!colors.ContainsKey(qc)) colors[qc] = 0;
                        colors[qc]++;
                    }
                }

                var top = colors.OrderByDescending(x => x.Value).Take(5)
                                .Select(x => $"{x.Key.r},{x.Key.g},{x.Key.b}:{x.Value}");

                Debug.Log($"=== {filename} ===");
                Debug.Log($"Size: {tex.width}x{tex.height}");
                Debug.Log($"Sampled Unique colors: {colors.Count}");
                Debug.Log($"Top colors: {string.Join(", ", top)}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Exception reading {fullPath}: {ex}");
            }
        }
    }
}
