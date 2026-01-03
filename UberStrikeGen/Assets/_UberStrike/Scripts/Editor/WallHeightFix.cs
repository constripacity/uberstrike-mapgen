#if UNITY_EDITOR
using UnityEngine;

public static class WallHeightFix
{
    private const float WallHeight = 4.0f;

    public static void CreateWall(Vector3 position, float metersPerPixel)
    {
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.transform.position = position + Vector3.up * (WallHeight * 0.5f);
        wall.transform.localScale = new Vector3(metersPerPixel, WallHeight, metersPerPixel);
    }

    public static bool ShouldSkipPixel(Color pixel)
    {
        return pixel.r < 0.1f && pixel.g > 0.9f && pixel.b > 0.9f;
    }
}
#endif
