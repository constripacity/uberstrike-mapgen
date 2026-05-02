#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityAI;

/// <summary>
/// Voronoi-based theme texture generator. Seeds via Bridson-style Poisson disk sampling,
/// computes nearest-seed regions, optionally smooths boundaries with a Gaussian-weighted
/// majority vote, and paints regions with theme colors. Honors a layout mask: pixels whose
/// mask grayscale &lt; 0.05 are forced black (non-walkable).
/// </summary>
public static class VoronoiThemeGenerator
{
    public struct VoronoiResult
    {
        public Texture2D texture;
        public List<(Color color, string theme)> swatches;
        public Dictionary<string, string> themeMap;
    }

    private static readonly Dictionary<string, Color32> ThemeColors = new Dictionary<string, Color32>(StringComparer.OrdinalIgnoreCase)
    {
        { "Industrial", new Color32(34, 34, 34, 255) },
        { "Warehouse",  new Color32(85, 68, 51, 255) },
        { "SciFi",      new Color32(51, 68, 85, 255) },
        { "Outdoor",    new Color32(68, 85, 51, 255) },
        { "Tech",       new Color32(85, 51, 68, 255) },
        { "Clean",      new Color32(200, 200, 200, 255) },
    };

    public static VoronoiResult GenerateForStack(StackDefinition definition, Texture2D layout, int desiredRegions, float smoothing, int seed)
    {
        int w = layout ? layout.width : 64;
        int h = layout ? layout.height : 64;
        w = Mathf.Max(4, w);
        h = Mathf.Max(4, h);
        int regions = Mathf.Clamp(desiredRegions, 3, 32);

        var preferredThemes = definition?.themeMap?.Values?.Distinct().ToList();
        var palette = (preferredThemes != null && preferredThemes.Count > 0)
            ? preferredThemes
            : ThemeColors.Keys.ToList();

        var seeds = GenerateSeedsPoisson(w, h, regions, seed);
        var regionMap = ComputeVoronoiRegions(w, h, seeds);
        if (smoothing > 0.001f)
        {
            regionMap = SmoothBoundaries(regionMap, smoothing);
        }

        var assignments = AssignThemes(regions, palette, seed);
        var tex = CreateThemeTexture(regionMap, assignments, layout);
        tex.name = "ThemeVoronoi";

        var swatches = assignments
            .Distinct()
            .Select(t => ((Color)ThemeColors[t], t))
            .ToList();
        var themeMap = BuildThemeMap(assignments);

        Debug.Log($"[VoronoiThemeGenerator] Generated {w}x{h} theme map: {regions} regions, {swatches.Count} unique themes, smoothing={smoothing:F2}.");

        return new VoronoiResult
        {
            texture = tex,
            swatches = swatches,
            themeMap = themeMap,
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
        var samples = new List<Vector2> { new Vector2(rng.Next(width), rng.Next(height)) };
        const int attempts = 32;

        while (samples.Count < regions && samples.Count > 0)
        {
            var baseSeed = samples[rng.Next(samples.Count)];
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
                foreach (var s in samples)
                {
                    if (Vector2.Distance(candidate, s) < minDist)
                    {
                        ok = false;
                        break;
                    }
                }

                if (ok)
                {
                    samples.Add(candidate);
                    added = true;
                    break;
                }
            }

            if (!added)
            {
                samples.RemoveAt(0);
            }
        }

        while (samples.Count < regions)
        {
            samples.Add(new Vector2(rng.Next(width), rng.Next(height)));
        }

        return samples.Take(regions).ToArray();
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
            if (r + 1 > regionCount) regionCount = r + 1;
        }

        int radius = Mathf.Max(1, Mathf.CeilToInt(sigma * 2f));
        float twoSigmaSq = 2f * sigma * sigma;
        var smoothed = new int[h, w];
        var votes = new Dictionary<int, float>();

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                votes.Clear();
                for (int dy = -radius; dy <= radius; dy++)
                {
                    int ny = Mathf.Clamp(y + dy, 0, h - 1);
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        int nx = Mathf.Clamp(x + dx, 0, w - 1);
                        float weight = Mathf.Exp(-(dx * dx + dy * dy) / twoSigmaSq);
                        int region = regions[ny, nx];
                        if (votes.TryGetValue(region, out var existing))
                        {
                            votes[region] = existing + weight;
                        }
                        else
                        {
                            votes[region] = weight;
                        }
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
        var assignments = new string[regions];
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
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false, true)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };

        var pixels = new Color32[w * h];
        Color32[] layoutPx = null;
        bool layoutSizeMatches = false;
        if (layoutMask != null)
        {
            layoutPx = layoutMask.GetPixels32();
            layoutSizeMatches = (layoutMask.width == w && layoutMask.height == h);
        }

        var fallback = new Color32(128, 128, 128, 255);

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int idx = y * w + x;

                if (layoutPx != null && layoutSizeMatches)
                {
                    var lp = layoutPx[idx];
                    float gs = (lp.r + lp.g + lp.b) / (3f * 255f);
                    if (gs < 0.05f)
                    {
                        pixels[idx] = new Color32(0, 0, 0, 255);
                        continue;
                    }
                }

                int region = Mathf.Clamp(regionMap[y, x], 0, assignments.Count - 1);
                string theme = assignments[region];
                pixels[idx] = ThemeColors.TryGetValue(theme, out var c) ? c : fallback;
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply(false, false);
        return tex;
    }
}
#endif
