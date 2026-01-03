#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.IO;

public static class WorkingBlueprintLoader
{
    [MenuItem("Tools/Test/Working Blueprint Loader")]
    public static void WorkingLoader()
    {
        string defaultDir = Path.Combine(Application.dataPath, "_UberStrike/Blueprints/MapLayouts");
        string path = EditorUtility.OpenFilePanel("Select PNG", defaultDir, "png");
        if (string.IsNullOrEmpty(path)) return;

        // FORCE fresh load
        if (File.Exists(path))
        {
            try
            {
                byte[] data = File.ReadAllBytes(path);
                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                bool ok = tex.LoadImage(data);
                Debug.Log($"[WorkingLoader] LoadImage ok={ok}, path={path}, size={tex.width}x{tex.height}");

                // Create unique root
                string filename = Path.GetFileNameWithoutExtension(path);
                GameObject root = new GameObject($"WORKING_{filename}_{System.DateTime.Now.Ticks}_{Random.Range(1000,9999)}");

                if (!ok || tex.width == 0 || tex.height == 0)
                {
                    Debug.LogError("[WorkingLoader] Failed to load texture or invalid dimensions.");
                    return;
                }

                // Use a default meters-per-pixel that matches QuickBuildSelect defaults (0.2m/px).
                const float DEFAULT_MPP = 0.2f;
                const float WALL_HEIGHT = 4.0f; // preview wall height to match BuildFromBlueprint
                Debug.Log($"[WorkingLoader] Preview using mpp={DEFAULT_MPP} m/px, wallHeight={WALL_HEIGHT}m");

                // Simple generation: place cubes for dark pixels sampled every 8 px, scaled by DEFAULT_MPP
                for (int px = 0; px < tex.width; px += 8)
                {
                    for (int py = 0; py < tex.height; py += 8)
                    {
                        Color c;
                        try { c = tex.GetPixel(px, py); }
                        catch { c = tex.GetPixel(Mathf.Clamp(px, 0, tex.width - 1), Mathf.Clamp(py, 0, tex.height - 1)); }

                            if (c.r < 0.5f) // treat darker pixels as walls (preview)
                            {
                                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);

                                // Convert pixel coords -> centered world coords consistent with BuildFromBlueprint
                                float worldX = (px - (tex.width * 0.5f) + 0.5f) * DEFAULT_MPP;
                                float worldZ = ((tex.height * 0.5f) - py - 0.5f) * DEFAULT_MPP;

                                // Make preview wall tall so it matches final build visuals
                                cube.transform.localScale = new Vector3(DEFAULT_MPP, WALL_HEIGHT, DEFAULT_MPP);
                                cube.transform.position = new Vector3(worldX, WALL_HEIGHT * 0.5f, worldZ);
                                cube.transform.parent = root.transform;

                                // Apply a simple dark material for visual clarity
                                var rend = cube.GetComponent<Renderer>();
                                if (rend != null) rend.sharedMaterial = new Material(Shader.Find("Standard")) { color = new Color(0.12f, 0.12f, 0.12f) };
                            }
                    }
                }

                Debug.Log($"[WorkingLoader] SUCCESS! Loaded {filename} ({tex.width}x{tex.height}) -> Created root '{root.name}' with {root.transform.childCount} cubes.");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[WorkingLoader] Exception: {ex}");
            }
        }
        else
        {
            Debug.LogError($"[WorkingLoader] File not found: {path}");
        }
    }
}
#endif
