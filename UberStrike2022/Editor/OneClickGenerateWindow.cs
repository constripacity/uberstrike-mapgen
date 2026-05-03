#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityAI;

/// <summary>
/// One-click MapGen entry point. Chains:
///   WFC layout -> Voronoi themes -> Build geometry -> NavMesh -> Flow analysis -> Save.
/// SA placement is currently skipped (BuildFromBlueprint handles spawn placement from
/// the WFC yellow-pixel pass; standalone item-placement requires a MapGameplaySet,
/// see carry-forward issue #2).
///
/// Output goes to Assets/_UberStrike/Generated/&lt;mapName&gt;/:
///   - 6 layer PNGs (layout / height / flow / theme / lighting / collision)
///   - &lt;mapName&gt;.stack.json
///   - &lt;mapName&gt;.flow_metrics.json
///   - &lt;mapName&gt;.unity
/// </summary>
public class OneClickGenerateWindow : EditorWindow
{
    private const string OUT_ROOT = "Assets/_UberStrike/Generated";

    private const string PREF_SIZE = "UGen.OneClick.Size";
    private const string PREF_STYLE = "UGen.OneClick.Style";
    private const string PREF_SEED = "UGen.OneClick.Seed";
    private const string PREF_MPP = "UGen.OneClick.MPP";
    private const string PREF_SPAWNS = "UGen.OneClick.Spawns";

    private static readonly int[] SIZES = { 32, 48, 64, 96 };
    private static readonly string[] SIZE_LABELS = { "32 x 32", "48 x 48", "64 x 64", "96 x 96" };
    private static readonly string[] STYLE_LABELS =
    {
        "Industrial", "SciFi", "Outdoor", "Tech", "Mixed",
    };

    private int _sizeIdx = 2;
    private int _styleIdx = 4;
    private int _seed = 1337;
    private float _mpp = 1.0f;
    private int _spawns = 6;
    private string _lastOutput = "";

    [MenuItem("Tools/UnityAI/Generate Map (One-Click)", priority = 50)]
    public static void Open()
    {
        var w = GetWindow<OneClickGenerateWindow>(false, "Generate Map");
        w.minSize = new Vector2(360, 280);
        w.Show();
    }

    private void OnEnable()
    {
        _sizeIdx = EditorPrefs.GetInt(PREF_SIZE, 2);
        _styleIdx = EditorPrefs.GetInt(PREF_STYLE, 4);
        _seed = EditorPrefs.GetInt(PREF_SEED, 1337);
        _mpp = EditorPrefs.GetFloat(PREF_MPP, 1.0f);
        _spawns = EditorPrefs.GetInt(PREF_SPAWNS, 6);
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("One-Click MapGen", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Chains WFC -> Voronoi -> Build -> NavMesh -> Flow -> Save into one click. " +
            "Output: 6 layer PNGs, stack JSON, flow metrics, .unity scene.",
            MessageType.Info);

        EditorGUI.BeginChangeCheck();
        _sizeIdx = EditorGUILayout.Popup("Size", _sizeIdx, SIZE_LABELS);
        _styleIdx = EditorGUILayout.Popup("Style", _styleIdx, STYLE_LABELS);
        _seed = EditorGUILayout.IntField("Seed", _seed);
        _mpp = EditorGUILayout.Slider("Meters / Pixel", _mpp, 0.1f, 2.0f);
        _spawns = EditorGUILayout.IntSlider("Spawn Count", _spawns, 2, 8);
        if (EditorGUI.EndChangeCheck())
        {
            EditorPrefs.SetInt(PREF_SIZE, _sizeIdx);
            EditorPrefs.SetInt(PREF_STYLE, _styleIdx);
            EditorPrefs.SetInt(PREF_SEED, _seed);
            EditorPrefs.SetFloat(PREF_MPP, _mpp);
            EditorPrefs.SetInt(PREF_SPAWNS, _spawns);
        }

        EditorGUILayout.Space();
        if (GUILayout.Button("Randomize Seed", GUILayout.Height(22)))
        {
            _seed = UnityEngine.Random.Range(1, int.MaxValue);
            EditorPrefs.SetInt(PREF_SEED, _seed);
        }

        EditorGUILayout.Space();
        if (GUILayout.Button("Generate Map", GUILayout.Height(34)))
        {
            try
            {
                _lastOutput = RunPipeline();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[OneClick] Pipeline failed: {ex}");
                EditorUtility.DisplayDialog("MapGen Failed", ex.Message, "OK");
            }
        }

        if (!string.IsNullOrEmpty(_lastOutput))
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Last Output", EditorStyles.boldLabel);
            EditorGUILayout.SelectableLabel(_lastOutput, EditorStyles.textField, GUILayout.Height(36));
        }
    }

    // -------------------------------------------------------------- Pipeline

    private string RunPipeline()
    {
        var active = EditorSceneManager.GetActiveScene();
        if (active.isDirty)
        {
            bool keep = EditorUtility.DisplayDialog(
                "Generate Map",
                $"The active scene '{active.name}' has unsaved changes. Generation creates a fresh scene; unsaved work will be lost.",
                "Continue", "Cancel");
            if (!keep) return "";
        }

        try
        {
            return RunPipelineInner();
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private string RunPipelineInner()
    {
        int size = SIZES[_sizeIdx];
        string style = STYLE_LABELS[_styleIdx];
        string mapName = $"OneClick_{size}_{style}_{_seed}_{DateTime.Now:yyyyMMdd_HHmmss}";
        string mapDir = $"{OUT_ROOT}/{mapName}";
        Directory.CreateDirectory(mapDir);

        EditorUtility.DisplayProgressBar("MapGen", "WFC: collapsing wave...", 0.1f);
        var layoutColors = GenerateLayoutColors(size, _seed, _spawns, out string wfcStrategy);
        PaintBorder(layoutColors, size);
        var layoutTex = MakeReadableTexture(size, size, layoutColors, mapName);
        Debug.Log($"[OneClick] WFC strategy: {wfcStrategy}.");

        EditorUtility.DisplayProgressBar("MapGen", "Voronoi: theme regions...", 0.25f);
        var stackDef = MakeStackDefinition(mapName, mapDir, layoutTex, style);
        var voronoi = VoronoiThemeGenerator.GenerateForStack(
            stackDef, layoutTex, desiredRegions: ChooseRegionCount(size), smoothing: 0.4f, seed: _seed);
        var themeTex = voronoi.texture;
        if (themeTex != null) themeTex.hideFlags = HideFlags.HideAndDontSave;
        Debug.Log($"[OneClick] Voronoi ok: {voronoi.swatches.Count} themes.");

        EditorUtility.DisplayProgressBar("MapGen", "Deriving aux layers...", 0.35f);
        var heightTex = MakeBlankTexture(size, size, new Color32(0, 0, 0, 255), mapName + "_height");
        var lightingTex = MakeBlankTexture(size, size, new Color32(0, 0, 0, 255), mapName + "_lighting");
        var collisionTex = DeriveCollision(layoutColors, size, mapName + "_collision");
        var flowSpawnTex = DeriveFlowFromLayout(layoutColors, size, mapName + "_flow");

        var bundle = new StackDefinition.StackLayerBundle
        {
            layout = layoutTex,
            height = heightTex,
            flow = flowSpawnTex,
            theme = themeTex,
            lighting = lightingTex,
            collision = collisionTex,
        };
        stackDef.SetLayers(bundle);

        EditorUtility.DisplayProgressBar("MapGen", "Build: geometry...", 0.5f);
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        BuildFromBlueprint.ClearOverrides();
        BuildFromBlueprint.BUILD_NAVMESH = true;
        BuildFromBlueprint.WALL_HEIGHT = EditorPrefs.GetFloat("UGen.WallH", 4.0f);
        BuildFromBlueprint.MAX_TOTAL_OBJECTS = EditorPrefs.GetInt("UGen.MaxObjs", 2000);
        BuildFromBlueprint.BuildFromTexture(layoutTex, _mpp);

        var arenaRoot = GameObject.Find($"Arena_{mapName}");
        if (!arenaRoot)
        {
            // fallback search
            arenaRoot = UnityEngine.Object.FindObjectsOfType<GameObject>(true)
                .FirstOrDefault(go => go.transform.parent == null && go.name.StartsWith("Arena_"));
        }

        EditorUtility.DisplayProgressBar("MapGen", "Flow analysis...", 0.7f);
        FlowAnalysisCore.FlowMetrics metrics = null;
        if (arenaRoot != null)
        {
            metrics = FlowAnalysisCore.Analyze(arenaRoot);
            Debug.Log($"[OneClick] Flow: {metrics.Summary()}");
            UpdateFlowTextureFromMetrics(flowSpawnTex, layoutColors, metrics, _mpp);
        }
        else
        {
            Debug.LogWarning("[OneClick] No Arena_* root found; skipping flow analysis.");
        }

        EditorUtility.DisplayProgressBar("MapGen", "Saving layers...", 0.85f);
        var written = new List<string>();
        written.Add(WritePng(layoutTex, $"{mapDir}/{mapName}.layout.png"));
        written.Add(WritePng(heightTex, $"{mapDir}/{mapName}.height.png"));
        written.Add(WritePng(flowSpawnTex, $"{mapDir}/{mapName}.flow.png"));
        written.Add(WritePng(themeTex, $"{mapDir}/{mapName}.theme.png"));
        written.Add(WritePng(lightingTex, $"{mapDir}/{mapName}.lighting.png"));
        written.Add(WritePng(collisionTex, $"{mapDir}/{mapName}.collision.png"));

        // Update paths and persist stack JSON
        stackDef.directory = mapDir;
        stackDef.layoutPath = written[0];
        stackDef.heightPath = written[1];
        stackDef.flowPath = written[2];
        stackDef.themePath = written[3];
        stackDef.lightingPath = written[4];
        stackDef.collisionPath = written[5];
        stackDef.metersPerPixel = _mpp;
        string stackPath = $"{mapDir}/{mapName}.stack.json";
        File.WriteAllText(stackPath, JsonUtility.ToJson(stackDef, true));

        if (metrics != null)
        {
            string metricsPath = $"{mapDir}/{mapName}.flow_metrics.json";
            File.WriteAllText(metricsPath, JsonUtility.ToJson(SerializableMetrics.From(metrics), true));
        }

        EditorUtility.DisplayProgressBar("MapGen", "Saving scene...", 0.95f);
        string scenePath = $"{mapDir}/{mapName}.unity";
        bool savedOk = EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), scenePath);
        if (!savedOk) throw new Exception($"Failed to save scene to {scenePath}");

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[OneClick] DONE -> {scenePath}");
        EditorUtility.DisplayDialog("MapGen Done", $"Saved:\n{scenePath}", "Open");
        return scenePath;
    }

    // --------------------------------------------------------------- Helpers

    /// <summary>
    /// WFC arena generation with cascading fallbacks. Strategy 1 (real WFC) is
    /// expected to converge after the WallInterior tile + floor-biased weight
    /// rework; strategies 2 and 3 stay as safety nets.
    /// </summary>
    private static Color[] GenerateLayoutColors(int size, int seed, int spawns, out string strategy)
    {
        // Strategy 1: GenerateArenaLayout — the canonical path.
        var wfc = new WFCCore(size, size, seed) { MaxRestarts = 5 };
        if (wfc.GenerateArenaLayout(spawns, maxRestarts: 5))
        {
            strategy = $"arena ({wfc.LastRestartCount} restarts, {wfc.LastElapsedSeconds:F2}s)";
            return wfc.ToBlueprintColors();
        }

        // Strategy 2: unconstrained Collapse — borders get painted on top.
        Debug.LogWarning("[OneClick] GenerateArenaLayout did not converge; retrying unconstrained.");
        wfc = new WFCCore(size, size, seed + 7919) { MaxRestarts = 10 };
        if (wfc.Collapse(maxRestarts: 10))
        {
            strategy = $"unconstrained ({wfc.LastRestartCount} restarts, {wfc.LastElapsedSeconds:F2}s)";
            return wfc.ToBlueprintColors();
        }

        // Strategy 3: synthetic floor + spawns (always succeeds).
        Debug.LogWarning("[OneClick] WFC contradicted on all paths; falling back to synthetic layout.");
        strategy = "synthetic";
        return SyntheticArenaLayout(size, spawns);
    }

    /// <summary>
    /// Floor-everywhere with quadrant-distributed spawns. Borders are added
    /// later by PaintBorder.
    /// </summary>
    private static Color[] SyntheticArenaLayout(int size, int spawns)
    {
        var floor = (Color)new Color32(128, 128, 128, 255);
        var spawn = (Color)new Color32(255, 255, 0, 255);
        var colors = new Color[size * size];
        for (int i = 0; i < colors.Length; i++) colors[i] = floor;

        var positions = new[]
        {
            new Vector2Int(size / 4, size / 4),
            new Vector2Int(3 * size / 4, 3 * size / 4),
            new Vector2Int(size / 4, 3 * size / 4),
            new Vector2Int(3 * size / 4, size / 4),
            new Vector2Int(size / 2, size / 4),
            new Vector2Int(size / 2, 3 * size / 4),
            new Vector2Int(size / 4, size / 2),
            new Vector2Int(3 * size / 4, size / 2),
        };
        int n = Mathf.Clamp(spawns, 0, positions.Length);
        for (int i = 0; i < n; i++)
        {
            var p = positions[i];
            p.x = Mathf.Clamp(p.x, 2, size - 3);
            p.y = Mathf.Clamp(p.y, 2, size - 3);
            colors[p.y * size + p.x] = spawn;
        }
        return colors;
    }

    /// <summary>
    /// Paint a 1-cell black wall border around the layout. Idempotent and
    /// safe to call after any WFC strategy.
    /// </summary>
    private static void PaintBorder(Color[] colors, int size)
    {
        var wall = (Color)new Color32(0, 0, 0, 255);
        for (int x = 0; x < size; x++)
        {
            colors[0 * size + x] = wall;
            colors[(size - 1) * size + x] = wall;
        }
        for (int y = 0; y < size; y++)
        {
            colors[y * size + 0] = wall;
            colors[y * size + (size - 1)] = wall;
        }
    }

    private static int ChooseRegionCount(int size)
    {
        if (size <= 32) return 4;
        if (size <= 48) return 5;
        if (size <= 64) return 6;
        return 8;
    }

    private StackDefinition MakeStackDefinition(string mapName, string mapDir, Texture2D layout, string style)
    {
        var def = new StackDefinition
        {
            name = mapName,
            sourceName = mapName,
            directory = mapDir,
            metersPerPixel = _mpp,
            wallHeight = EditorPrefs.GetFloat("UGen.WallH", 4.0f),
            navmesh = true,
            pairTeleporters = true,
        };
        def.SetLayers(new StackDefinition.StackLayerBundle { layout = layout });
        def.themeMap = ThemeMapForStyle(style);
        return def;
    }

    private static Dictionary<string, string> ThemeMapForStyle(string style)
    {
        // Voronoi reads themeMap.Values for its palette. Empty = full 6-theme palette.
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        switch (style)
        {
            case "Industrial":
                map["#1"] = "Industrial"; map["#2"] = "Warehouse"; break;
            case "SciFi":
                map["#1"] = "SciFi"; map["#2"] = "Tech"; break;
            case "Outdoor":
                map["#1"] = "Outdoor"; map["#2"] = "Clean"; break;
            case "Tech":
                map["#1"] = "Tech"; map["#2"] = "SciFi"; map["#3"] = "Clean"; break;
            case "Mixed":
            default:
                break;
        }
        return map;
    }

    /// <summary>
    /// Creates an editor-only Texture2D tagged HideAndDontSave so it survives
    /// EditorSceneManager.NewScene / AssetDatabase.Refresh cycles. Without
    /// this flag, runtime-allocated Texture2Ds get swept up when the scene
    /// is replaced, and downstream consumers (BuildFromTexture, PNG write)
    /// hit MissingReferenceException on `tex.name`.
    /// </summary>
    private static Texture2D MakeReadableTexture(int w, int h, Color[] colors, string name)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, false)
        {
            name = name,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave,
        };
        tex.SetPixels(colors);
        tex.Apply(false, false);
        return tex;
    }

    private static Texture2D MakeBlankTexture(int w, int h, Color32 fill, string name)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, false)
        {
            name = name,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave,
        };
        var pix = new Color32[w * h];
        for (int i = 0; i < pix.Length; i++) pix[i] = fill;
        tex.SetPixels32(pix);
        tex.Apply(false, false);
        return tex;
    }

    private static Texture2D DeriveCollision(Color[] layoutColors, int size, string name)
    {
        var pix = new Color32[size * size];
        var walkable = new Color32(0, 255, 0, 255);
        var blocked = new Color32(0, 0, 0, 255);
        for (int i = 0; i < layoutColors.Length; i++)
        {
            Color c = layoutColors[i];
            // Floor (gray 128) and Spawn (yellow) are walkable; everything else blocked
            bool isWalkable =
                (Mathf.Abs(c.r - 0.5019f) < 0.05f && Mathf.Abs(c.g - 0.5019f) < 0.05f && Mathf.Abs(c.b - 0.5019f) < 0.05f) ||
                (c.r > 0.95f && c.g > 0.95f && c.b < 0.1f) ||
                (c.r > 0.4f && c.r < 0.6f && c.g < 0.1f && c.b > 0.4f); // bridge purple
            pix[i] = isWalkable ? walkable : blocked;
        }
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, false)
        {
            name = name,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave,
        };
        tex.SetPixels32(pix);
        tex.Apply(false, false);
        return tex;
    }

    private static Texture2D DeriveFlowFromLayout(Color[] layoutColors, int size, string name)
    {
        // Cyan background, yellow spawn dots from layout's yellow pixels
        var pix = new Color32[size * size];
        var bg = new Color32(0, 255, 255, 255);
        var spawn = new Color32(255, 0, 0, 255); // FlowMarker.Spawn = red per StackDefinition.ClassifyFlow
        for (int i = 0; i < pix.Length; i++) pix[i] = bg;
        for (int i = 0; i < layoutColors.Length; i++)
        {
            Color c = layoutColors[i];
            if (c.r > 0.95f && c.g > 0.95f && c.b < 0.1f)
            {
                pix[i] = spawn;
            }
        }
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, false)
        {
            name = name,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave,
        };
        tex.SetPixels32(pix);
        tex.Apply(false, false);
        return tex;
    }

    private static void UpdateFlowTextureFromMetrics(
        Texture2D flowTex, Color[] layoutColors, FlowAnalysisCore.FlowMetrics metrics, float mpp)
    {
        if (flowTex == null || metrics == null) return;
        if (metrics.chokepoints == null || metrics.chokepoints.Count == 0) return;

        int w = flowTex.width, h = flowTex.height;
        var pix = flowTex.GetPixels32();

        var choke = new Color32(255, 255, 0, 255); // FlowMarker.Choke = yellow
        float halfW = w * mpp * 0.5f;
        float halfH = h * mpp * 0.5f;

        foreach (var pos in metrics.chokepoints)
        {
            int px = Mathf.RoundToInt((pos.x + halfW - mpp * 0.5f) / mpp);
            int py = Mathf.RoundToInt((halfH - pos.z - mpp * 0.5f) / mpp);
            if (px < 0 || px >= w || py < 0 || py >= h) continue;
            pix[py * w + px] = choke;
        }

        flowTex.SetPixels32(pix);
        flowTex.Apply(false, false);
    }

    private static string WritePng(Texture2D tex, string path)
    {
        var bytes = tex.EncodeToPNG();
        File.WriteAllBytes(path, bytes);
        AssetDatabase.ImportAsset(path);
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti != null)
        {
            ti.isReadable = true;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.mipmapEnabled = false;
            ti.filterMode = FilterMode.Point;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.sRGBTexture = true;
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }
        return path;
    }

    [Serializable]
    private class SerializableMetrics
    {
        public int chokepointCount;
        public int deadZoneCount;
        public float spawnBalance;
        public float averageEngagementDistance;
        public float mapOpenness;
        public int loopCount;
        public int strategicPositionCount;
        public int campingSpotCount;
        public string summary;

        public static SerializableMetrics From(FlowAnalysisCore.FlowMetrics m) => new SerializableMetrics
        {
            chokepointCount = m.chokepoints?.Count ?? 0,
            deadZoneCount = m.deadZones?.Count ?? 0,
            spawnBalance = m.spawnBalance,
            averageEngagementDistance = m.averageEngagementDistance,
            mapOpenness = m.mapOpenness,
            loopCount = m.circulationLoops?.Count ?? 0,
            strategicPositionCount = m.strategicPositions?.Count ?? 0,
            campingSpotCount = m.campingSpots?.Count ?? 0,
            summary = m.Summary(),
        };
    }
}
#endif
