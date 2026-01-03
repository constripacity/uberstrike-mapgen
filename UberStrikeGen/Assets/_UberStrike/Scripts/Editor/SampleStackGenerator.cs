#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Utility menu command that (re)creates the ArenaStack_Sample asset pack so artists
/// can regenerate the demo stack locally without manually editing image layers.
/// </summary>
public static class SampleStackGenerator
{
    private const string OutputDirectory = "Assets/_UberStrike/Blueprints/Stacks";
    private const string StackName = "ArenaStack_Sample";
    private const int TextureSize = 256;

    private static readonly Color32 ColorWhite = new Color32(255, 255, 255, 255);
    private static readonly Color32 ColorBlack = new Color32(0, 0, 0, 255);
    private static readonly Color32 ColorGray = new Color32(160, 160, 160, 255);
    private static readonly Color32 ColorDarkGray = new Color32(96, 96, 96, 255);
    private static readonly Color32 ColorPurple = new Color32(128, 0, 128, 255);
    private static readonly Color32 ColorCyan = new Color32(0, 255, 255, 255);
    private static readonly Color32 ColorSpawnYellow = new Color32(255, 255, 0, 255);
    private static readonly Color32 ColorSpawnRed = new Color32(255, 0, 0, 255);
    private static readonly Color32 ColorSpawnGreen = new Color32(0, 255, 0, 255);
    private static readonly Color32 ColorChoke = new Color32(255, 165, 0, 255);
    private static readonly Color32 ColorCover = new Color32(128, 128, 128, 255);
    private static readonly Color32 ColorPointLight = new Color32(255, 255, 255, 255);
    private static readonly Color32 ColorSpotLight = new Color32(255, 208, 128, 255);
    private static readonly Color32 ColorClimbable = new Color32(0, 170, 255, 255);
    private static readonly Color32 ColorDestructible = new Color32(255, 0, 255, 255);

    [MenuItem("Tools/UnityAI/Generate Sample Stack", priority = 42)]
    public static void Generate()
    {
        try
        {
            Directory.CreateDirectory(OutputDirectory);

            GenerateLayoutTexture();
            GenerateHeightTexture();
            GenerateFlowTexture();
            GenerateThemeTexture();
            GenerateLightingTexture();
            GenerateCollisionTexture();
            GenerateStackJson();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("Sample Stack", "ArenaStack_Sample regenerated in Assets/_UberStrike/Blueprints/Stacks", "Nice!");
        }
        catch (Exception ex)
        {
            Debug.LogError("[SampleStackGenerator] Failed: " + ex);
            EditorUtility.DisplayDialog("Sample Stack", "Generation failed. See console for details.", "OK");
        }
    }

    private static void GenerateLayoutTexture()
    {
        var tex = CreateTexture(ColorWhite);

        // Cyan 1px perimeter border.
        for (int i = 0; i < TextureSize; i++)
        {
            tex.SetPixel(i, 0, ColorCyan);
            tex.SetPixel(i, TextureSize - 1, ColorCyan);
            tex.SetPixel(0, i, ColorCyan);
            tex.SetPixel(TextureSize - 1, i, ColorCyan);
        }

        // Base play area floor.
        int innerMin = 24;
        int innerMax = TextureSize - 24;
        for (int y = innerMin; y < innerMax; y++)
        {
            for (int x = innerMin; x < innerMax; x++)
            {
                tex.SetPixel(x, y, ColorGray);
            }
        }

        // Raised platform area (dark gray) on the west side.
        for (int y = innerMin + 24; y < innerMax - 48; y++)
        {
            for (int x = innerMin + 8; x < innerMin + 48; x++)
            {
                tex.SetPixel(x, y, ColorDarkGray);
            }
        }

        // Interior wall cross with a 4-pixel door opening on the south segment.
        int mid = TextureSize / 2;
        for (int y = innerMin; y < innerMax; y++)
        {
            tex.SetPixel(mid, y, ColorBlack);
        }
        for (int x = innerMin; x < innerMax; x++)
        {
            if (x >= mid - 2 && x <= mid + 1) continue; // door gap
            tex.SetPixel(x, mid, ColorBlack);
        }

        // Bridge that spans over the central gap.
        for (int y = mid - 6; y <= mid + 6; y++)
        {
            for (int x = mid - 32; x <= mid - 8; x++)
            {
                tex.SetPixel(x, y, ColorPurple);
            }
        }

        SaveTexture(tex, Path.Combine(OutputDirectory, StackName + ".layout.png"));
    }

    private static void GenerateHeightTexture()
    {
        var tex = CreateTexture(ColorBlack);

        // Gentle radial slope towards the centre.
        Vector2 centre = new Vector2(TextureSize / 2f, TextureSize / 2f);
        for (int y = 0; y < TextureSize; y++)
        {
            for (int x = 0; x < TextureSize; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), centre);
                float t = Mathf.InverseLerp(TextureSize * 0.5f, 0f, dist);
                byte height = (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(32f, 200f, t)), 0, 255);
                tex.SetPixel(x, y, new Color32(height, height, height, 255));
            }
        }

        // Extra lift for the western platform.
        for (int y = 80; y < 160; y++)
        {
            for (int x = 36; x < 100; x++)
            {
                tex.SetPixel(x, y, new Color32(220, 220, 220, 255));
            }
        }

        SaveTexture(tex, Path.Combine(OutputDirectory, StackName + ".height.png"));
    }

    private static void GenerateFlowTexture()
    {
        var tex = CreateTexture(new Color32(0, 0, 0, 255));

        // Place spawns around the quadrants.
        tex.SetPixel(80, 80, ColorSpawnYellow);
        tex.SetPixel(176, 80, ColorSpawnYellow);
        tex.SetPixel(80, 176, ColorSpawnRed);
        tex.SetPixel(176, 176, ColorSpawnGreen);

        // Choke strip through the bridge zone.
        for (int x = 96; x <= 160; x++)
        {
            tex.SetPixel(x, TextureSize / 2, ColorChoke);
            tex.SetPixel(x, TextureSize / 2 + 1, ColorChoke);
        }

        // Cover pockets on each side of the bridge.
        for (int y = 118; y <= 138; y++)
        {
            tex.SetPixel(90, y, ColorCover);
            tex.SetPixel(166, y, ColorCover);
        }

        SaveTexture(tex, Path.Combine(OutputDirectory, StackName + ".flow.png"));
    }

    private static void GenerateThemeTexture()
    {
        var tex = CreateTexture(ColorWhite);

        // Three vertical regions matching themeMap.
        int third = TextureSize / 3;
        for (int y = 0; y < TextureSize; y++)
        {
            for (int x = 0; x < TextureSize; x++)
            {
                if (x < third)
                {
                    tex.SetPixel(x, y, HexToColor32("#222222"));
                }
                else if (x < third * 2)
                {
                    tex.SetPixel(x, y, HexToColor32("#554433"));
                }
                else
                {
                    tex.SetPixel(x, y, HexToColor32("#334455"));
                }
            }
        }

        SaveTexture(tex, Path.Combine(OutputDirectory, StackName + ".theme.png"));
    }

    private static void GenerateLightingTexture()
    {
        var tex = CreateTexture(new Color32(0, 0, 0, 255));

        // Point lights arranged in a cross pattern.
        tex.SetPixel(128, 64, ColorPointLight);
        tex.SetPixel(128, 128, ColorPointLight);
        tex.SetPixel(128, 192, ColorPointLight);
        tex.SetPixel(64, 128, ColorPointLight);
        tex.SetPixel(192, 128, ColorPointLight);

        // Spot lights aimed at choke approaches.
        tex.SetPixel(100, 112, ColorSpotLight);
        tex.SetPixel(156, 112, ColorSpotLight);
        tex.SetPixel(100, 144, ColorSpotLight);
        tex.SetPixel(156, 144, ColorSpotLight);

        SaveTexture(tex, Path.Combine(OutputDirectory, StackName + ".lighting.png"));
    }

    private static void GenerateCollisionTexture()
    {
        var tex = CreateTexture(ColorBlack);

        // Walkable floor region.
        for (int y = 48; y < TextureSize - 48; y++)
        {
            for (int x = 48; x < TextureSize - 48; x++)
            {
                tex.SetPixel(x, y, ColorWhite);
            }
        }

        // Climbable ladder strip on east wall.
        for (int y = 96; y <= 160; y++)
        {
            for (int x = TextureSize - 56; x < TextureSize - 52; x++)
            {
                tex.SetPixel(x, y, ColorClimbable);
            }
        }

        // Destructible obstacle near the southern choke exit.
        for (int y = 180; y <= 188; y++)
        {
            for (int x = 120; x <= 136; x++)
            {
                tex.SetPixel(x, y, ColorDestructible);
            }
        }

        SaveTexture(tex, Path.Combine(OutputDirectory, StackName + ".collision.png"));
    }

    private static void GenerateStackJson()
    {
        string jsonPath = Path.Combine(OutputDirectory, StackName + ".stack.json");
        string json = "{\n" +
                      "  \"metersPerPixel\": 1.0,\n" +
                      "  \"wallHeight\": 4.0,\n" +
                      "  \"heightScale\": 0.05,\n" +
                      "  \"stairsRise\": 0.25,\n" +
                      "  \"rampMaxSlopeDeg\": 25,\n" +
                      "  \"doorWidthMeters\": 2.0,\n" +
                      "  \"bridgeWidthMeters\": 3.0,\n" +
                      "  \"pairTeleporters\": true,\n" +
                      "  \"navmesh\": true,\n" +
                      "  \"themeDefault\": \"DefaultTheme\",\n" +
                      "  \"themeMap\": {\n" +
                      "    \"#222222\": \"Industrial\",\n" +
                      "    \"#554433\": \"Warehouse\",\n" +
                      "    \"#334455\": \"BlueSteel\"\n" +
                      "  },\n" +
                      "  \"flow\": {\n" +
                      "    \"spawnColorYellow\": \"#FFFF00\",\n" +
                      "    \"spawnColorRed\": \"#FF0000\",\n" +
                      "    \"spawnColorGreen\": \"#00FF00\",\n" +
                      "    \"chokeColor\": \"#FFA500\",\n" +
                      "    \"coverColor\": \"#808080\",\n" +
                      "    \"arrowColor\": \"#00FFFF\"\n" +
                      "  },\n" +
                      "  \"lighting\": {\n" +
                      "    \"pointColor\": \"#FFFFFF\",\n" +
                      "    \"spotColor\": \"#FFD080\",\n" +
                      "    \"sunDirDeg\": [50, -30, 0],\n" +
                      "    \"fogDensity\": 0.02\n" +
                      "  },\n" +
                      "  \"collision\": {\n" +
                      "    \"walkable\": \"#FFFFFF\",\n" +
                      "    \"blocked\": \"#000000\",\n" +
                      "    \"climbable\": \"#00AAFF\",\n" +
                      "    \"destructible\": \"#FF00FF\"\n" +
                      "  }\n" +
                      "}\n";
        File.WriteAllText(jsonPath, json, Encoding.UTF8);
    }

    private static Texture2D CreateTexture(Color32 fill)
    {
        var tex = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };

        var pixels = tex.GetPixels32();
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = fill;
        }
        tex.SetPixels32(pixels);
        tex.Apply(false);
        return tex;
    }

    private static void SaveTexture(Texture2D texture, string pngPath)
    {
        byte[] png = texture.EncodeToPNG();
        File.WriteAllBytes(pngPath, png);

        // Produce a sidecar .png.txt so the repository can store a text variant too.
        string base64 = Convert.ToBase64String(png);
        var sb = new StringBuilder(base64.Length + base64.Length / 64 + 8);
        const int wrap = 76;
        for (int i = 0; i < base64.Length; i += wrap)
        {
            int len = Math.Min(wrap, base64.Length - i);
            sb.Append(base64, i, len);
            if (i + len < base64.Length)
                sb.Append('\n');
        }
        File.WriteAllText(pngPath + ".txt", sb.ToString(), Encoding.UTF8);

        UnityEngine.Object.DestroyImmediate(texture);
    }

    private static Color32 HexToColor32(string hex)
    {
        if (ColorUtility.TryParseHtmlString(hex, out var c))
            return (Color32)c;
        return ColorWhite;
    }
}
#endif
