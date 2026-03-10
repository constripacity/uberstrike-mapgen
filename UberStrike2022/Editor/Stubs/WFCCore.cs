#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Stub for WFCCore — full Wave Function Collapse solver not yet ported from Unity 6.
/// Returns failure so BuildFromBlueprint falls back to the original texture.
/// </summary>
public class WFCCore
{
    private int _width;
    private int _height;
    private int _seed;

    public WFCCore(int width, int height, int seed)
    {
        _width = width;
        _height = height;
        _seed = seed;
    }

    public void ApplyConstraints(Dictionary<Vector2Int, WFCTileType> constraints)
    {
        // Stub — no constraint application
    }

    public bool Collapse()
    {
        Debug.Log("[WFCCore] Stub: WFC solver not yet ported to Unity 2022. Returning false to use original texture.");
        return false;
    }

    public bool EnsureConnectivity()
    {
        return false;
    }

    public Color[] ToBlueprintColors()
    {
        return new Color[_width * _height];
    }
}
#endif
