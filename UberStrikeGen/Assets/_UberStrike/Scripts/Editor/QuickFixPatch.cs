#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public static class QuickFixPatch
{
    public static GameObject CreateProperWall(Vector3 position, float width, float height)
    {
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "Wall";
        wall.transform.position = position + Vector3.up * (height * 0.5f);
        wall.transform.localScale = new Vector3(width, height, width);

        var renderer = wall.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            var mat = new Material(Shader.Find("Standard")) { color = new Color(0.3f, 0.3f, 0.3f) };
            renderer.sharedMaterial = mat;
        }

        return wall;
    }

    public static ElementType ClassifyPixelFixed(Color pixel)
    {
        float tolerance = 0.1f;

        if (IsCyan(pixel))
        {
            return ElementType.Skip;
        }

        if (pixel.r < tolerance && pixel.g < tolerance && pixel.b < tolerance)
        {
            return ElementType.Wall;
        }

        if (Mathf.Abs(pixel.r - pixel.g) < tolerance && Mathf.Abs(pixel.g - pixel.b) < tolerance && pixel.r > 0.3f && pixel.r < 0.8f)
        {
            return ElementType.Floor;
        }

        if (pixel.b > 0.8f && pixel.r < 0.2f && pixel.g < 0.2f)
        {
            return ElementType.Water;
        }

        if (pixel.r > 0.8f && pixel.g < 0.2f && pixel.b < 0.2f)
        {
            return ElementType.SpawnRed;
        }

        if (pixel.g > 0.8f && pixel.r < 0.2f && pixel.b < 0.2f)
        {
            return ElementType.SpawnGreen;
        }

        if (pixel.r > 0.8f && pixel.g > 0.8f && pixel.b < 0.2f)
        {
            return ElementType.SpawnNeutral;
        }

        if (pixel.r > 0.4f && pixel.r < 0.6f && pixel.g < 0.1f && pixel.b > 0.4f && pixel.b < 0.6f)
        {
            return ElementType.Bridge;
        }

        return ElementType.Empty;
    }

    private static bool IsCyan(Color c) => c.r < 0.1f && c.g > 0.9f && c.b > 0.9f;

    public static void ProcessBlueprintFixed(Texture2D blueprint, float metersPerPixel = 1.0f)
    {
        if (blueprint == null)
        {
            Debug.LogWarning("[QuickFixPatch] Blueprint texture is null.");
            return;
        }

        GameObject mapRoot = new GameObject("GeneratedMap");
        var wallCombines = new List<CombineInstance>();
        var floorCombines = new List<CombineInstance>();
        float wallHeight = 4.0f;

        for (int x = 0; x < blueprint.width; x++)
        {
            for (int y = 0; y < blueprint.height; y++)
            {
                Color pixel = blueprint.GetPixel(x, y);
                ElementType type = ClassifyPixelFixed(pixel);
                if (type == ElementType.Skip)
                {
                    continue;
                }

                Vector3 world = new Vector3(x * metersPerPixel, 0f, y * metersPerPixel);
                switch (type)
                {
                    case ElementType.Wall:
                        {
                            Mesh wallMesh = CreateCubeMesh(metersPerPixel, wallHeight);
                            CombineInstance ci = new CombineInstance
                            {
                                mesh = wallMesh,
                                transform = Matrix4x4.TRS(world + Vector3.up * (wallHeight * 0.5f), Quaternion.identity, Vector3.one)
                            };
                            wallCombines.Add(ci);
                            break;
                        }
                    case ElementType.Floor:
                        {
                            Mesh floorMesh = CreatePlaneMesh(metersPerPixel);
                            CombineInstance ci = new CombineInstance
                            {
                                mesh = floorMesh,
                                transform = Matrix4x4.TRS(world, Quaternion.identity, Vector3.one)
                            };
                            floorCombines.Add(ci);
                            break;
                        }
                    case ElementType.SpawnRed:
                    case ElementType.SpawnGreen:
                    case ElementType.SpawnNeutral:
                        {
                            GameObject spawn = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                            spawn.transform.SetParent(mapRoot.transform, false);
                            spawn.transform.position = world + Vector3.up;
                            spawn.transform.localScale = Vector3.one * 0.5f;
                            var renderer = spawn.GetComponent<MeshRenderer>();
                            if (renderer != null)
                            {
                                renderer.sharedMaterial = new Material(Shader.Find("Standard"))
                                {
                                    color = type switch
                                    {
                                        ElementType.SpawnRed => Color.red,
                                        ElementType.SpawnGreen => Color.green,
                                        _ => Color.yellow
                                    }
                                };
                            }
                            break;
                        }
                }
            }
        }

        if (wallCombines.Count > 0)
        {
            var wallsObj = new GameObject("CombinedWalls");
            wallsObj.transform.SetParent(mapRoot.transform, false);
            var mesh = new Mesh { name = "Walls" };
            mesh.CombineMeshes(wallCombines.ToArray(), true, true);
            mesh.RecalculateNormals();
            wallsObj.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = wallsObj.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = new Material(Shader.Find("Standard")) { color = new Color(0.4f, 0.4f, 0.4f) };
            wallsObj.AddComponent<MeshCollider>();
        }

        if (floorCombines.Count > 0)
        {
            var floorsObj = new GameObject("CombinedFloors");
            floorsObj.transform.SetParent(mapRoot.transform, false);
            var mesh = new Mesh { name = "Floors" };
            mesh.CombineMeshes(floorCombines.ToArray(), true, true);
            mesh.RecalculateNormals();
            floorsObj.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = floorsObj.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = new Material(Shader.Find("Standard")) { color = new Color(0.7f, 0.7f, 0.7f) };
            floorsObj.AddComponent<MeshCollider>();
        }

        Debug.Log($"[QuickFixPatch] Map Generation Complete. Walls:{wallCombines.Count} Floors:{floorCombines.Count}");
    }

    private static Mesh CreateCubeMesh(float size, float height)
    {
        float half = size * 0.5f;
        Vector3[] vertices =
        {
            new Vector3(-half, -height * 0.5f, -half),
            new Vector3(half, -height * 0.5f, -half),
            new Vector3(half, -height * 0.5f, half),
            new Vector3(-half, -height * 0.5f, half),
            new Vector3(-half, height * 0.5f, -half),
            new Vector3(half, height * 0.5f, -half),
            new Vector3(half, height * 0.5f, half),
            new Vector3(-half, height * 0.5f, half)
        };

        int[] triangles =
        {
            0,2,1,0,3,2,
            4,5,6,4,6,7,
            0,1,5,0,5,4,
            2,3,7,2,7,6,
            0,4,7,0,7,3,
            1,2,6,1,6,5
        };

        Mesh mesh = new Mesh();
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        return mesh;
    }

    private static Mesh CreatePlaneMesh(float size)
    {
        float half = size * 0.5f;
        Vector3[] vertices =
        {
            new Vector3(-half, 0f, -half),
            new Vector3(half, 0f, -half),
            new Vector3(half, 0f, half),
            new Vector3(-half, 0f, half)
        };

        int[] triangles = { 0,2,1,0,3,2 };
        Vector2[] uvs =
        {
            new Vector2(0f,0f),
            new Vector2(1f,0f),
            new Vector2(1f,1f),
            new Vector2(0f,1f)
        };

        Mesh mesh = new Mesh();
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uvs;
        mesh.RecalculateNormals();
        return mesh;
    }

    public enum ElementType
    {
        Empty,
        Wall,
        Floor,
        Water,
        SpawnRed,
        SpawnGreen,
        SpawnNeutral,
        Bridge,
        Skip
    }

    [MenuItem("Tools/UnityAI/Test Quick Fix")]
    public static void TestQuickFix()
    {
        Texture2D tex = new Texture2D(32, 32);
        Color[] pixels = Enumerable.Repeat(Color.white, 32 * 32).ToArray();
        for (int i = 0; i < 32; i++)
        {
            pixels[i] = Color.cyan;
            pixels[(31 * 32) + i] = Color.cyan;
            pixels[(i * 32)] = Color.cyan;
            pixels[(i * 32) + 31] = Color.cyan;
        }

        for (int x = 8; x < 16; x++)
        {
            for (int y = 8; y < 16; y++)
            {
                pixels[y * 32 + x] = Color.black;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();

        ProcessBlueprintFixed(tex, 1f);
        Debug.Log("[QuickFixPatch] Test complete. Inspect scene for results.");
    }
}
#endif
