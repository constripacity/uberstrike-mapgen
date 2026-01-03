#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityAI
{
    /// <summary>
    /// Smart layer synthesiser for StackDefinition v0.6 workflows.
    /// </summary>
    public static class StackGeneratorV6
    {
        [MenuItem("Tools/UberStrike/MapGen/Stack Generator v0.6/Upgrade Stack JSON...")]
        public static void UpgradeStackJson()
        {
            string jsonPath = EditorUtility.OpenFilePanel("Stack Definition", Application.dataPath, "json");
            if (string.IsNullOrEmpty(jsonPath))
            {
                return;
            }

            var def = StackDefinition.LoadFromJSON(jsonPath);
            if (def == null)
            {
                return;
            }

            def.Prepare();
            var flow = FlowAnalyser.Analyse(def);
            var bundle = AutoGenerateMissingLayers(def, flow);
            SaveBundle(def, bundle, jsonPath);
            Debug.Log($"[StackGeneratorV6] Upgraded {def.name} at {jsonPath}");
        }

        [MenuItem("Tools/UberStrike/MapGen/Stack Generator v0.6/Generate Lighting From Layout", priority = 50)]
        public static void GenerateLightingOnly()
        {
            var layout = Selection.activeObject as Texture2D;
            if (!layout)
            {
                Debug.LogWarning("[StackGeneratorV6] Select a layout Texture2D to seed lighting generation.");
                return;
            }

            var tex = GenerateLightingTexture(layout, new List<Vector2Int>());
            var path = EditorUtility.SaveFilePanelInProject("Lighting Layer", "lighting_v0p6", "png", "Choose output path");
            if (!string.IsNullOrEmpty(path))
            {
                File.WriteAllBytes(path, tex.EncodeToPNG());
                AssetDatabase.Refresh();
            }
        }

        public static StackDefinition.StackLayerBundle AutoGenerateMissingLayers(StackDefinition definition, FlowAnalysisResult flow)
        {
            var bundle = definition.GetLayers();
            if (!bundle.layout)
            {
                bundle.layout = CreateFallbackLayout();
            }

            var walkable = ExtractWalkableCells(bundle.layout);
            if (!bundle.height)
            {
                bundle.height = GenerateHeightTexture(bundle.layout, flow);
            }
            if (!bundle.flow)
            {
                bundle.flow = GenerateFlowTexture(bundle.layout, walkable);
            }
            if (!bundle.theme)
            {
                bundle.theme = GenerateThemeTexture(bundle.layout, definition, walkable);
            }
            if (!bundle.lighting)
            {
                bundle.lighting = GenerateLightingTexture(bundle.layout, walkable);
            }
            if (!bundle.collision)
            {
                bundle.collision = GenerateCollisionTexture(bundle.layout, definition);
            }

            definition.SetLayers(bundle);
            return bundle;
        }

        private static Texture2D CreateFallbackLayout()
        {
            var tex = new Texture2D(64, 64, TextureFormat.RGBA32, false, true) { name = "layout_auto" };
            var pixels = Enumerable.Repeat(new Color32(180, 180, 180, 255), 64 * 64).ToArray();
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        private static Texture2D GenerateHeightTexture(Texture2D layout, FlowAnalysisResult flow)
        {
            var tex = new Texture2D(layout.width, layout.height, TextureFormat.RFloat, false, true)
            {
                name = "height_auto"
            };
            var pixels = new Color[layout.width * layout.height];
            float chokeBias = Mathf.Clamp(flow.chokePixels / 32f, 0.1f, 2f);
            for (int y = 0; y < layout.height; y++)
            {
                for (int x = 0; x < layout.width; x++)
                {
                    float nx = (float)x / layout.width;
                    float ny = (float)y / layout.height;
                    float perlin = Mathf.PerlinNoise(nx * 3f, ny * 3f);
                    float ridge = Mathf.Abs(0.5f - nx) + Mathf.Abs(0.5f - ny);
                    float value = Mathf.Lerp(perlin, 1f - ridge, 0.35f) * chokeBias;
                    pixels[y * layout.width + x] = new Color(value, value, value, 1f);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        private static Texture2D GenerateFlowTexture(Texture2D layout, List<Vector2Int> walkable)
        {
            var tex = new Texture2D(layout.width, layout.height, TextureFormat.RGBA32, false, true)
            {
                name = "flow_auto"
            };
            var colors = new Color32[layout.width * layout.height];
            foreach (var cell in walkable)
            {
                colors[cell.y * layout.width + cell.x] = (cell.x + cell.y) % 3 == 0 ? new Color32(255, 255, 0, 255) : new Color32(255, 0, 0, 255);
            }
            tex.SetPixels32(colors);
            tex.Apply();
            return tex;
        }

        private static Texture2D GenerateThemeTexture(Texture2D layout, StackDefinition definition, List<Vector2Int> walkable)
        {
            var tex = new Texture2D(layout.width, layout.height, TextureFormat.RGBA32, false, true)
            {
                name = "theme_auto"
            };
            var colors = new Color32[layout.width * layout.height];
            if (walkable.Count == 0)
            {
                for (int i = 0; i < colors.Length; i++) colors[i] = new Color32(60, 60, 60, 255);
                tex.SetPixels32(colors);
                tex.Apply();
                return tex;
            }
            int clusters = Mathf.Clamp(walkable.Count / 512, 2, 4);
            var centers = new List<Vector2>(clusters);
            var rand = new System.Random(1234);
            for (int i = 0; i < clusters; i++)
            {
                var pick = walkable[rand.Next(walkable.Count)];
                centers.Add(new Vector2(pick.x, pick.y));
            }

            var palette = definition.themeMap.Count > 0 ? definition.themeMap.Values.ToArray() : new[] { "Industrial", "Warehouse", "SciFi" };
            for (int i = 0; i < walkable.Count; i++)
            {
                var cell = walkable[i];
                int best = 0;
                float bestDist = float.MaxValue;
                for (int c = 0; c < centers.Count; c++)
                {
                    float dist = Vector2.Distance(centers[c], cell);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        best = c;
                    }
                }

                string themeName = palette[best % palette.Length];
                var color = ThemeColor(themeName);
                colors[cell.y * layout.width + cell.x] = color;
            }
            tex.SetPixels32(colors);
            tex.Apply();
            return tex;
        }

        private static Texture2D GenerateLightingTexture(Texture2D layout, List<Vector2Int> walkable)
        {
            var tex = new Texture2D(layout.width, layout.height, TextureFormat.RGBA32, false, true)
            {
                name = "lighting_auto"
            };
            var pixels = new Color32[layout.width * layout.height];
            foreach (var cell in walkable)
            {
                float intensity = Mathf.PerlinNoise(cell.x * 0.1f, cell.y * 0.1f);
                byte value = (byte)(Mathf.Lerp(64f, 255f, intensity));
                pixels[cell.y * layout.width + cell.x] = new Color32(value, value, 0, 255);
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        private static Texture2D GenerateCollisionTexture(Texture2D layout, StackDefinition definition)
        {
            var tex = new Texture2D(layout.width, layout.height, TextureFormat.RGBA32, false, true)
            {
                name = "collision_auto"
            };
            var pixels = new Color32[layout.width * layout.height];
            var walkable = ExtractWalkableCells(layout);
            var walkableSet = new HashSet<Vector2Int>(walkable);
            Color32 climbable = ParseHtmlColor(definition.collision.climbable, new Color32(0, 255, 0, 255));
            Color32 destructible = ParseHtmlColor(definition.collision.destructible, new Color32(255, 0, 0, 255));
            for (int y = 0; y < layout.height; y++)
            {
                for (int x = 0; x < layout.width; x++)
                {
                    var cell = new Vector2Int(x, y);
                    pixels[y * layout.width + x] = walkableSet.Contains(cell) ? climbable : destructible;
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        private static List<Vector2Int> ExtractWalkableCells(Texture2D layout)
        {
            var walkable = new List<Vector2Int>();
            var pixels = layout.GetPixels32();
            for (int y = 0; y < layout.height; y++)
            {
                for (int x = 0; x < layout.width; x++)
                {
                    var c = pixels[y * layout.width + x];
                    if (c.r > 100 && c.g > 100 && c.b > 100)
                    {
                        walkable.Add(new Vector2Int(x, y));
                    }
                }
            }
            return walkable;
        }

        private static void SaveBundle(StackDefinition definition, StackDefinition.StackLayerBundle bundle, string jsonPath)
        {
            string baseDir = Path.Combine(Path.GetDirectoryName(jsonPath) ?? Application.dataPath, "auto_layers");
            Directory.CreateDirectory(baseDir);

            definition.layoutPath = SaveTexture(bundle.layout, baseDir, "layout.png");
            definition.heightPath = SaveTexture(bundle.height, baseDir, "height.png");
            definition.flowPath = SaveTexture(bundle.flow, baseDir, "flow.png");
            definition.themePath = SaveTexture(bundle.theme, baseDir, "theme.png");
            definition.lightingPath = SaveTexture(bundle.lighting, baseDir, "lighting.png");
            definition.collisionPath = SaveTexture(bundle.collision, baseDir, "collision.png");

            string upgradedJson = Path.Combine(baseDir, Path.GetFileNameWithoutExtension(jsonPath) + "_v0p6.json");
            File.WriteAllText(upgradedJson, JsonUtility.ToJson(definition, true));
            AssetDatabase.Refresh();
        }

        private static string SaveTexture(Texture2D tex, string directory, string filename)
        {
            if (!tex) return string.Empty;
            string path = Path.Combine(directory, filename);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            return ToProjectRelative(path);
        }

        private static string ToProjectRelative(string absolutePath)
        {
            var dataPath = Application.dataPath.Replace('/', Path.DirectorySeparatorChar);
            if (absolutePath.StartsWith(dataPath, StringComparison.OrdinalIgnoreCase))
            {
                string rel = "Assets" + absolutePath.Substring(dataPath.Length);
                return rel.Replace(Path.DirectorySeparatorChar, '/');
            }
            return absolutePath.Replace(Path.DirectorySeparatorChar, '/');
        }

        private static Color32 ThemeColor(string themeName)
        {
            themeName = themeName?.ToLowerInvariant() ?? "default";
            return themeName switch
            {
                "industrial" => new Color32(80, 80, 90, 255),
                "warehouse" => new Color32(120, 90, 60, 255),
                "scifi" => new Color32(0, 170, 255, 255),
                _ => new Color32(200, 200, 200, 255)
            };
        }

        private static Color32 ParseHtmlColor(string hex, Color32 fallback)
        {
            if (ColorUtility.TryParseHtmlString(hex, out var color))
            {
                return color;
            }
            return fallback;
        }
    }
}
#endif
