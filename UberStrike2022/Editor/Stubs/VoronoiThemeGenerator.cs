#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityAI;

/// <summary>
/// Stub for VoronoiThemeGenerator — full Voronoi implementation not yet ported from Unity 6.
/// Generates a simple uniform theme texture as a fallback.
/// </summary>
public static class VoronoiThemeGenerator
{
    public struct VoronoiResult
    {
        public Texture2D texture;
        public List<(Color color, string theme)> swatches;
        public Dictionary<string, string> themeMap;
    }

    public static VoronoiResult GenerateForStack(StackDefinition definition, Texture2D layout, int desiredRegions, float weight, int seed)
    {
        int w = layout ? layout.width : 64;
        int h = layout ? layout.height : 64;

        // Generate a simple single-color theme texture as fallback
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false, true)
        {
            name = "ThemeFallback",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };

        var defaultColor = new Color(0.5f, 0.5f, 0.5f);
        var pixels = new Color[w * h];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = defaultColor;
        tex.SetPixels(pixels);
        tex.Apply();

        Debug.Log($"[VoronoiThemeGenerator] Stub: generated uniform fallback theme ({w}x{h}). Full Voronoi not yet ported.");

        return new VoronoiResult
        {
            texture = tex,
            swatches = new List<(Color, string)> { (defaultColor, "Default") },
            themeMap = new Dictionary<string, string> { { "#808080", "Default" } }
        };
    }
}
#endif
