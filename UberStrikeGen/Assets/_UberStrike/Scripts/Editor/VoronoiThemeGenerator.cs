#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityAI
{
    public struct ThemeGenerationResult
    {
        public Texture2D texture;
        public List<(Color color, string theme)> swatches;
        public Dictionary<string, string> themeMap;
    }

    public class VoronoiThemeGenerator : EditorWindow
    {
        private static readonly Dictionary<string, Color32> ThemeColors = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Industrial", new Color32(34, 34, 34, 255) },
            { "Warehouse", new Color32(85, 68, 51, 255) },
            { "SciFi", new Color32(51, 68, 85, 255) },
            { "Outdoor", new Color32(68, 85, 51, 255) },
            { "Tech", new Color32(85, 51, 68, 255) },
            { "Clean", new Color32(200, 200, 200, 255) },
        };

        private int mapWidth = 256;
        private int mapHeight = 256;
        private int regionCount = 7;
        private float smoothing = 1.0f;
        private int randomSeed = 1337;
        private Texture2D layoutMask;
        private Texture2D preview;

        [MenuItem("Tools/UberStrike/MapGen/Voronoi Theme Generator")]
        public static void ShowWindow()
        {
            GetWindow<VoronoiThemeGenerator>("Voronoi Themes");
        }

        private void OnGUI()
        {
            GUILayout.Label("Voronoi Theme Generation", EditorStyles.boldLabel);
            mapWidth = EditorGUILayout.IntField("Width", mapWidth);
            mapHeight = EditorGUILayout.IntField("Height", mapHeight);
            regionCount = EditorGUILayout.IntSlider("Regions", regionCount, 3, 15);
            smoothing = EditorGUILayout.Slider("Smoothing", smoothing, 0f, 5f);
            randomSeed = EditorGUILayout.IntField("Seed", randomSeed);
            layoutMask = (Texture2D)EditorGUILayout.ObjectField("Layout Mask (optional)", layoutMask, typeof(Texture2D), false);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Generate"))
                {
                    GeneratePreview();
                }
                if (preview && GUILayout.Button("Save As..."))
                {
                    SavePreview(preview);
                }
            }

            if (preview)
            {
                GUILayout.Space(8f);
                GUILayout.Label("Preview", EditorStyles.boldLabel);
                Rect rect = GUILayoutUtility.GetRect(256, 256, GUILayout.ExpandWidth(false));
                EditorGUI.DrawPreviewTexture(rect, preview, null, ScaleMode.ScaleToFit);
            }
        }

        private void GeneratePreview()
        {
            var result = GenerateForStack(null, layoutMask, regionCount, smoothing, randomSeed, mapWidth, mapHeight);
            preview = result.texture;
        }

        private void SavePreview(Texture2D tex)
        {
            var path = EditorUtility.SaveFilePanel("Save Theme Map", "Assets", "theme_voronoi.png", "png");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            File.WriteAllBytes(path, tex.EncodeToPNG());
            var meta = new
            {
                width = mapWidth,
                height = mapHeight,
                regions = regionCount,
                smoothing,
                seed = randomSeed,
                themes = ThemeColors.Keys.ToArray()
            };
            File.WriteAllText(Path.ChangeExtension(path, ".json"), JsonUtility.ToJson(meta, true));
            AssetDatabase.Refresh();
            Debug.Log($"[Voronoi] Theme map saved to {path}");
        }

        public static ThemeGenerationResult GenerateForStack(
            StackDefinition stack,
            Texture2D layoutMask,
            int regions,
            float smoothing,
            int seed,
            int widthOverride = -1,
            int heightOverride = -1)
        {
            int width = widthOverride > 0 ? widthOverride : (layoutMask ? layoutMask.width : stack?.Width ?? 256);
            int height = heightOverride > 0 ? heightOverride : (layoutMask ? layoutMask.height : stack?.Height ?? 256);
            width = Mathf.Max(4, width);
            height = Mathf.Max(4, height);
            regions = Mathf.Clamp(regions, 3, 32);

            var preferredThemes = stack?.themeMap?.Values?.ToList();
            var palette = preferredThemes != null && preferredThemes.Count > 0
                ? preferredThemes
                : ThemeColors.Keys.ToList();

            var seeds = GenerateSeedsPoisson(width, height, regions, seed);
            var regionMap = ComputeVoronoiRegions(width, height, seeds);
            if (smoothing > 0.001f)
            {
                regionMap = SmoothBoundaries(regionMap, smoothing);
            }

            var assignments = AssignThemes(regions, palette, seed);
            var themeTexture = CreateThemeTexture(regionMap, assignments, layoutMask);
            var swatches = assignments.Select(a => ((Color)ThemeColors[a], a)).ToList();
            var themeMap = BuildThemeMap(assignments);

            return new ThemeGenerationResult
            {
                texture = themeTexture,
                swatches = swatches,
                themeMap = themeMap
            };
        }

        private static Dictionary<string, string> BuildThemeMap(IReadOnlyList<string> assignments)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var theme in assignments.Distinct())
            {
                if (!ThemeColors.TryGetValue(theme, out var color))
                {
                    continue;
                }
                string hex = "#" + ColorUtility.ToHtmlStringRGB(color);
                map[hex] = theme;
            }
            return map;
        }

        private static Vector2[] GenerateSeedsPoisson(int width, int height, int regions, int seed)
        {
            var rng = new System.Random(seed);
            float minDist = Mathf.Min(width, height) / Mathf.Max(3f, regions * 0.8f);
            var seeds = new List<Vector2> { new(rng.Next(width), rng.Next(height)) };
            int attempts = 32;
            while (seeds.Count < regions && seeds.Count > 0)
            {
                var baseSeed = seeds[rng.Next(seeds.Count)];
                bool added = false;
                for (int i = 0; i < attempts; i++)
                {
                    float angle = (float)(rng.NextDouble() * Math.PI * 2.0);
                    float radius = minDist + (float)rng.NextDouble() * minDist;
                    var candidate = baseSeed + new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
                    if (candidate.x < 0 || candidate.y < 0 || candidate.x >= width || candidate.y >= height)
                    {
                        continue;
                    }

                    bool ok = true;
                    foreach (var s in seeds)
                    {
                        if (Vector2.Distance(candidate, s) < minDist)
                        {
                            ok = false;
                            break;
                        }
                    }

                    if (ok)
                    {
                        seeds.Add(candidate);
                        added = true;
                        break;
                    }
                }

                if (!added)
                {
                    seeds.RemoveAt(0);
                }
            }

            while (seeds.Count < regions)
            {
                seeds.Add(new Vector2(rng.Next(width), rng.Next(height)));
            }

            return seeds.Take(regions).ToArray();
        }

        private static int[,] ComputeVoronoiRegions(int width, int height, IReadOnlyList<Vector2> seeds)
        {
            var regions = new int[height, width];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float best = float.MaxValue;
                    int bestIdx = 0;
                    for (int i = 0; i < seeds.Count; i++)
                    {
                        float dist = Vector2.SqrMagnitude(new Vector2(x, y) - seeds[i]);
                        if (dist < best)
                        {
                            best = dist;
                            bestIdx = i;
                        }
                    }
                    regions[y, x] = bestIdx;
                }
            }
            return regions;
        }

        private static int[,] SmoothBoundaries(int[,] regions, float sigma)
        {
            int h = regions.GetLength(0);
            int w = regions.GetLength(1);
            int regionCount = 0;
            foreach (int r in regions)
            {
                regionCount = Math.Max(regionCount, r + 1);
            }

            int radius = Mathf.Max(1, Mathf.CeilToInt(sigma * 2f));
            var smoothed = new int[h, w];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    var votes = new Dictionary<int, float>();
                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        for (int dx = -radius; dx <= radius; dx++)
                        {
                            int nx = Mathf.Clamp(x + dx, 0, w - 1);
                            int ny = Mathf.Clamp(y + dy, 0, h - 1);
                            float weight = Mathf.Exp(-(dx * dx + dy * dy) / (2f * sigma * sigma));
                            int region = regions[ny, nx];
                            if (!votes.ContainsKey(region))
                            {
                                votes[region] = 0f;
                            }
                            votes[region] += weight;
                        }
                    }

                    int bestRegion = regions[y, x];
                    float bestWeight = -1f;
                    foreach (var pair in votes)
                    {
                        if (pair.Value > bestWeight)
                        {
                            bestWeight = pair.Value;
                            bestRegion = pair.Key;
                        }
                    }
                    smoothed[y, x] = Mathf.Clamp(bestRegion, 0, regionCount - 1);
                }
            }
            return smoothed;
        }

        private static string[] AssignThemes(int regions, IReadOnlyList<string> palette, int seed)
        {
            var rng = new System.Random(seed * 17 + 3);
            string[] assignments = new string[regions];
            for (int i = 0; i < regions; i++)
            {
                assignments[i] = palette[rng.Next(palette.Count)];
            }
            return assignments;
        }

        private static Texture2D CreateThemeTexture(int[,] regionMap, IReadOnlyList<string> assignments, Texture2D layoutMask)
        {
            int h = regionMap.GetLength(0);
            int w = regionMap.GetLength(1);
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color32[w * h];
            Color32[] layoutPx = layoutMask ? layoutMask.GetPixels32() : null;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int idx = y * w + x;
                    if (layoutPx != null && idx < layoutPx.Length && layoutPx[idx].grayscale < 0.05f)
                    {
                        pixels[idx] = new Color32(0, 0, 0, 255);
                        continue;
                    }

                    int region = Mathf.Clamp(regionMap[y, x], 0, assignments.Count - 1);
                    string theme = assignments[region];
                    Color32 color = ThemeColors.TryGetValue(theme, out var c) ? c : new Color32(128, 128, 128, 255);
                    pixels[idx] = color;
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return tex;
        }
    }
}
#endif
