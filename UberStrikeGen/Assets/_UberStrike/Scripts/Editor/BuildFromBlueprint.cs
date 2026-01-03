#if UNITY_EDITOR
using System;
using UnityAI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.IO;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Blueprint-to-scene builder for UberStrike map generation (REWRITTEN).
/// - 1 pixel == 1 meter
/// - Walls (black) -> merged 4m tall blocks
/// - Floors (gray variants) -> flood-fill -> one mesh per connected area at Y=0
/// - Perimeter walls around floor regions (skip where door/cyan exists)
/// - Spawn clusters -> one cylinder per cluster (2m radius, 5m tall)
/// - Color tolerance 30 to tolerate PNG artifacts
/// - Centers map at world origin and flips image Y -> world Z
/// - Combines static geometry using Mesh.CombineMeshes and groups objects
/// </summary>
public static class BuildFromBlueprint
{
    // === SETTINGS ===
    private const string PREFABS_DIR = "Assets/_UberStrike/Prefabs";
    private const float CELL_SIZE = 1.0f;     // 1 px → 1 meter (fixed)
    public static float WALL_HEIGHT = 4.0f;     // 4 meters tall walls (per spec)
    private const int COLOR_TOLERANCE = 30;     // tolerant for PNG artifacts
    private const int MAX_INSTANCES = 50000;    // safety cap
    private const int MAX_TRIM_LIGHTS = 100;
    private const int TRIM_SPACING = 8;
    public static int MAX_TOTAL_OBJECTS = 2000;     // increased for complex maps during testing
    private const int MAX_PLATFORMS = 20;
    private const int PROGRESS_STEP = 1000;
    private const int GEOMETRY_CHUNK_SIZE = 50;

    public static bool BUILD_NAVMESH = true;

    private static MapTheme s_ThemeOverride;
    private static MapGameplaySet s_GameplayOverride;

    // Cached prefabs (optional)
    private static GameObject p_Floor, p_Wall, p_Glass, p_Water, p_Spawn, p_Jump, p_Tele, p_Health, p_Armor;

    /// <summary>
    /// Clears any theme or gameplay overrides that were previously assigned.
    /// </summary>
    public static void ClearOverrides()
    {
        s_ThemeOverride = null;
        s_GameplayOverride = null;
    }

    /// <summary>
    /// Override the theme that will be used for subsequent builds.
    /// </summary>
    public static void SetThemeOverride(MapTheme theme)
    {
        s_ThemeOverride = theme;
    }

    /// <summary>
    /// Override the gameplay set that will be used for subsequent builds.
    /// </summary>
    public static void SetGameplayOverride(MapGameplaySet gameplaySet)
    {
        s_GameplayOverride = gameplaySet;
    }

    /// <summary>
    /// Entry point used by stack import workflows to invoke the enhanced builder.
    /// </summary>
    public static void BuildFromStack(StackDefinition definition)
    {
        if (definition == null)
        {
            throw new ArgumentNullException(nameof(definition));
        }

        BuildFromStackEnhanced.BuildFromStack(definition);
    }

    static BuildFromBlueprint()
    {
#if UNITY_EDITOR
        if (Application.isBatchMode)
        {
            return;
        }

        try
        {
            WALL_HEIGHT = EditorPrefs.GetFloat("UGen.WallH", WALL_HEIGHT);
            MAX_TOTAL_OBJECTS = EditorPrefs.GetInt("UGen.MaxObjs", MAX_TOTAL_OBJECTS);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[BuildFromBlueprint] Failed to read EditorPrefs: {ex.Message}");
        }
#endif
    }

    [Serializable]
    private class BlueprintMeta
    {
        public float mpp = -1f;
        public float wallHeight = -1f;
        public int maxObjects = -1;
        public string theme;
        public string gameplaySet;
        public string notes;
    }

    private static BlueprintMeta TryLoadBlueprintMeta(string fullPathOrAssetPath)
    {
        try
        {
            string metaCandidate = fullPathOrAssetPath + ".meta.json";
            string altCandidate = metaCandidate;

            if (!Path.IsPathRooted(metaCandidate))
            {
                var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
                if (!string.IsNullOrEmpty(projectRoot))
                {
                    altCandidate = Path.Combine(projectRoot, metaCandidate.Replace('/', Path.DirectorySeparatorChar));
                }
            }

            string finalPath = File.Exists(metaCandidate) ? metaCandidate : (File.Exists(altCandidate) ? altCandidate : null);
            if (string.IsNullOrEmpty(finalPath))
                return null;

            var json = File.ReadAllText(finalPath);
            if (string.IsNullOrWhiteSpace(json))
                return null;

            return JsonUtility.FromJson<BlueprintMeta>(json);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Meta] Failed to parse meta overrides: {ex.Message}");
            return null;
        }
    }

    private enum TileKind
    {
        Void,
        WallPixel,          // ground-level walls (black)
        Floor,              // ground walkable (light gray variants)
        FloorVariant,
        Platform_E1,        // dark gray (64) -> Y = 4
        Platform_Mid,       // medium gray (128) -> Y = 8
        Platform_Upper,     // light gray (192) -> Y = 12
        Water,
        Door,
        Ramp,               // brown -> ramp up
        Stairs,             // orange -> stairs
        Bridge,             // purple -> bridge/walkway
        SpawnNeutral,
        SpawnRed,
        SpawnGreen,
        PickupHealth,
        PickupArmor,
        Teleporter,
        JumpPad,
        Glass
    }

    private struct WallRect
    {
        public int x;
        public int y;
        public int width;
        public int height;

        public WallRect(int x, int y, int width, int height)
        {
            this.x = x;
            this.y = y;
            this.width = width;
            this.height = height;
        }
    }

    private struct WallPrism
    {
        public Vector3 center;
        public Vector3 size;

        public WallPrism(Vector3 center, Vector3 size)
        {
            this.center = center;
            this.size = size;
        }
    }

    // Colors (reference)
    private static readonly Color32 COL_BLACK = new Color32(0, 0, 0, 255);
    private static readonly Color32 COL_WHITE = new Color32(255, 255, 255, 255);
    private static readonly Color32 COL_VERYDARK = new Color32(64, 64, 64, 255);   // platform E1
    private static readonly Color32 COL_MEDGRAY = new Color32(128, 128, 128, 255); // mid platform
    private static readonly Color32 COL_LIGHTGRAY = new Color32(192, 192, 192, 255); // upper platform / floor
    private static readonly Color32 COL_BLUE = new Color32(0, 0, 255, 255);
    private static readonly Color32 COL_CYAN = new Color32(0, 255, 255, 255);
    private static readonly Color32 COL_RED = new Color32(255, 0, 0, 255);
    private static readonly Color32 COL_GREEN = new Color32(0, 255, 0, 255);
    private static readonly Color32 COL_YELLOW = new Color32(255, 255, 0, 255);
    private static readonly Color32 COL_ORANGE = new Color32(255, 165, 0, 255); // stairs
    private static readonly Color32 COL_BROWN = new Color32(139, 69, 19, 255);   // ramp
    private static readonly Color32 COL_PURPLE = new Color32(128, 0, 128, 255);  // bridge / walkway
    private static readonly Color32 COL_HEALTH = new Color32(255, 64, 64, 255);
    private static readonly Color32 COL_ARMOR = new Color32(255, 90, 0, 255);
    private static readonly Color32 COL_JUMPPAD = new Color32(255, 255, 128, 255);
    private static readonly Color32 COL_TELEPORTER = new Color32(255, 0, 255, 255);

    /// <summary>
    /// Replacement entry used by HeadlessBuilder.
    /// Builds from PNG and optionally saves the scene.
    /// </summary>
    public static void BuildFromPNG(string pngPath, float mpp, bool saveScene, out string scenePath)
    {
        scenePath = null;
        try
        {
            Debug.Log($"[BuildFromBlueprint] BuildFromPNG called with: {pngPath}, mpp={mpp}");
            BuildFromPNGPath(pngPath, mpp);

            if (saveScene)
            {
                string mapsDir = "Assets/_UberStrike/Maps/Playable";
                Directory.CreateDirectory(mapsDir);
                scenePath = $"{mapsDir}/Arena_Headless_{System.DateTime.Now:yyyyMMdd_HHmmss}.unity";
                EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), scenePath);
                Debug.Log($"[BuildFromBlueprint] Saved scene to: {scenePath}");
                Debug.Log($"[HEADLESS] BUILD_DONE path=\"{scenePath}\"");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[BuildFromBlueprint] Failed: {ex}");
            throw;
        }
    }

    /// <summary>
    /// Builds from an asset path or absolute PNG path (imports temp copy).
    /// </summary>
public static void BuildFromPNGPath(string fullPathOrAssetPath, float metersPerPixel)
{
    Debug.Log($"[BuildFromBlueprint] BuildFromPNGPath called with: {fullPathOrAssetPath}");
    Debug.Log($"[DEBUG] Selected file: {fullPathOrAssetPath}");
    Debug.Log($"[DEBUG] File exists: {File.Exists(fullPathOrAssetPath)}");
    Debug.Log($"[DEBUG] Timestamp: {DateTime.Now:HH:mm:ss.fff}");
    // Force clear any cached texture/asset DB state before loading
    Resources.UnloadUnusedAssets();
    AssetDatabase.Refresh();

    string assetPath = fullPathOrAssetPath;
    bool isTemporary = false;

    if (Path.IsPathRooted(fullPathOrAssetPath))
    {
        string assetsDir = "Assets/_UberStrike/Blueprints/MapLayouts";
        Directory.CreateDirectory(assetsDir);
        assetPath = Path.Combine(assetsDir, "__headless_input.png").Replace("\\", "/");
        File.Copy(fullPathOrAssetPath, assetPath, true);

        // Ensure import settings BEFORE trying to load the texture asset
        EnsureCorrectImportSettings(assetPath);
        AssetDatabase.ImportAsset(assetPath);
        isTemporary = true;
    }
    else
    {
        // For project-relative asset paths, ensure settings as well
        EnsureCorrectImportSettings(assetPath);
    }

    // Try to load via AssetDatabase first (preferred)
    var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);

    // Regardless of AssetDatabase caching, prefer a fresh, unique runtime instance loaded from disk when possible.
    // This avoids reusing an asset instance that may be cached/immutable and prevents stale content.
    if (File.Exists(assetPath))
    {
        try
        {
            byte[] imageBytes = File.ReadAllBytes(assetPath);
            Debug.Log($"[Blueprint] Read {imageBytes.Length} bytes from disk: {assetPath}");

            // Create unique runtime texture instance
            Texture2D newTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            newTexture.name = $"Blueprint_{DateTime.Now.Ticks}"; // Unique name
            bool loaded = newTexture.LoadImage(imageBytes);
            Debug.Log($"[DEBUG] Loaded texture: {newTexture.name}, Size: {newTexture.width}x{newTexture.height}, LoadImageSuccess={loaded}");

            // Sample corner/center pixels to verify correct file
            try
            {
                if (loaded && newTexture.width > 0 && newTexture.height > 0)
                {
                    Color topLeft = newTexture.GetPixel(0, 0);
                    Color topRight = newTexture.GetPixel(Mathf.Max(0, newTexture.width - 1), 0);
                    Color center = newTexture.GetPixel(newTexture.width / 2, newTexture.height / 2);
                    Debug.Log($"[DEBUG] Colors - TL:{topLeft} TR:{topRight} Center:{center}");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[DEBUG] Pixel sampling failed: {ex}");
            }

            if (loaded)
            {
                tex = newTexture;
            }
            else
            {
                Debug.LogError($"[Blueprint] Texture.LoadImage failed for raw bytes: {assetPath}");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[Blueprint] Raw file load failed: {ex}");
        }
    }
    else
    {
        // If the file is not present on disk (odd), fall back to AssetDatabase instance
        Debug.Log($"[Blueprint] File not found on disk, falling back to AssetDatabase load for: {assetPath}");
        tex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
    }

    // If AssetDatabase failed or texture is not readable AND raw load did not succeed, attempt original fallback
    if (tex == null || !tex.isReadable)
    {
        if (tex == null)
            Debug.LogError($"[Blueprint] AssetDatabase failed to load texture at: {assetPath}; attempting raw file load fallback path.");
        else
            Debug.LogWarning($"[Blueprint] Texture loaded but not readable (isReadable={tex.isReadable}). Proceeding with available instance: {assetPath}");

        if (File.Exists(assetPath))
        {
            try
            {
                byte[] imageBytes = File.ReadAllBytes(assetPath);
                Debug.Log($"[Blueprint] Loaded {imageBytes.Length} bytes from {assetPath}");
                var rawTex = new Texture2D(2, 2);
                if (rawTex.LoadImage(imageBytes))
                {
                    tex = rawTex;
                    Debug.Log($"[Blueprint] Raw-loaded texture size: {tex.width}x{tex.height}");
                }
                else
                {
                    Debug.LogError($"[Blueprint] Texture.LoadImage failed for: {assetPath}");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[Blueprint] Raw file load failed: {ex}");
            }
        }
        else
        {
            Debug.LogError($"[Blueprint] File does not exist on disk: {assetPath}");
        }
    }

    if (tex == null)
        throw new System.Exception("Could not load blueprint PNG at: " + assetPath);

    Debug.Log($"[Blueprint] Texture size: {tex.width}x{tex.height}");
    Debug.Log($"[Blueprint] Is readable: {tex.isReadable}");
    // Log file hash and sample pixels to ensure we're actually loading the intended PNG
    try
    {
        if (File.Exists(assetPath))
        {
            var bytes = File.ReadAllBytes(assetPath);
            using (var sha1 = System.Security.Cryptography.SHA1.Create())
            {
                var h = sha1.ComputeHash(bytes);
                string shortHash = BitConverter.ToString(h, 0, 6).Replace("-", "");
                Debug.Log($"[Blueprint] File SHA1(short): {shortHash}");
            }
        }
        else
        {
            Debug.Log($"[Blueprint] assetPath file not found for hash: {assetPath}");
        }
    }
    catch (System.Exception ex)
    {
        Debug.LogWarning($"[Blueprint] Hash/sample failed: {ex}");
    }

    // Sample a few pixels to verify content differs between files
    try
    {
        if (tex.width > 0 && tex.height > 0)
        {
            var tl = tex.GetPixel(0, 0);
            var centerSample = tex.GetPixel(tex.width / 2, tex.height / 2);
            Debug.Log($"[Blueprint] Sample pixels - TopLeft: {tl}, Center: {centerSample}");
        }
    }
    catch (System.Exception ex)
    {
        Debug.LogWarning($"[Blueprint] Pixel sampling failed: {ex}");
    }

    float originalWallHeight = WALL_HEIGHT;
    int originalMaxObjects = MAX_TOTAL_OBJECTS;
    var overrideNotes = new List<string>();

    try
    {
        var meta = TryLoadBlueprintMeta(fullPathOrAssetPath);
        if (meta != null)
        {
            if (meta.mpp > 0f)
            {
                metersPerPixel = meta.mpp;
                overrideNotes.Add($"mpp={meta.mpp:F2}");
            }

            if (meta.wallHeight > 0f)
            {
                WALL_HEIGHT = meta.wallHeight;
                overrideNotes.Add($"wallHeight={meta.wallHeight:F2}");
            }

            if (meta.maxObjects > 0)
            {
                MAX_TOTAL_OBJECTS = meta.maxObjects;
                overrideNotes.Add($"maxObjects={meta.maxObjects}");
            }

            if (!string.IsNullOrEmpty(meta.theme))
            {
                var theme = AssetDatabase.LoadAssetAtPath<MapTheme>(meta.theme);
                if (theme)
                {
                    s_ThemeOverride = theme;
                    overrideNotes.Add($"theme={meta.theme}");
                }
                else
                {
                    Debug.LogWarning($"[Meta] Theme asset not found: {meta.theme}");
                }
            }

            if (!string.IsNullOrEmpty(meta.gameplaySet))
            {
                var set = AssetDatabase.LoadAssetAtPath<MapGameplaySet>(meta.gameplaySet);
                if (set)
                {
                    s_GameplayOverride = set;
                    overrideNotes.Add($"gameplaySet={meta.gameplaySet}");
                }
                else
                {
                    Debug.LogWarning($"[Meta] GameplaySet asset not found: {meta.gameplaySet}");
                }
            }

            if (!string.IsNullOrEmpty(meta.notes))
            {
                overrideNotes.Add($"notes='{meta.notes}'");
            }
        }

        if (overrideNotes.Count > 0)
        {
            Debug.Log($"[Meta] Applied overrides: {string.Join(", ", overrideNotes)}");
        }

        // Always use 1 meter per pixel unless explicitly overridden > 0
        float mpp = metersPerPixel > 0 ? metersPerPixel : CELL_SIZE;

        // The core new implementation
        BuildFromTexture(tex, mpp);
    }
    finally
    {
        WALL_HEIGHT = originalWallHeight;
        MAX_TOTAL_OBJECTS = originalMaxObjects;
        s_ThemeOverride = null;
        s_GameplayOverride = null;

        if (isTemporary && Path.GetFileName(assetPath) == "__headless_input.png")
        {
            AssetDatabase.DeleteAsset(assetPath);
            Debug.Log("[BuildFromBlueprint] Cleaned up temporary blueprint");
        }
    }
}

    /// <summary>
    /// Auto-fix import settings to make texture readable/uncompressed with point filtering.
    /// </summary>
    private static void EnsureCorrectImportSettings(string assetPath)
    {
        var ti = (TextureImporter)AssetImporter.GetAtPath(assetPath);
        if (ti == null) return;

        bool needsReimport = false;

        if (!ti.isReadable)
        {
            ti.isReadable = true;
            needsReimport = true;
        }

        if (ti.textureCompression != TextureImporterCompression.Uncompressed)
        {
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            needsReimport = true;
        }

        if (ti.mipmapEnabled)
        {
            ti.mipmapEnabled = false;
            needsReimport = true;
        }

        if (ti.filterMode != FilterMode.Point)
        {
            ti.filterMode = FilterMode.Point;
            needsReimport = true;
        }

        if (needsReimport)
        {
            Debug.LogWarning("⚠️ Import settings not ideal; running auto-fix…");
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.sRGBTexture = true;
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            Debug.Log("[BuildFromBlueprint] ✓ Import settings fixed.");
        }
    }

    /// <summary>
    /// New BuildFromTexture implementation (full rewrite).
    /// </summary>
    public static void BuildFromTexture(Texture2D tex, float mpp)
    {
        Debug.Log($"[BuildFromBlueprint] (REWRITE) BuildFromTexture: '{tex.name}', mpp={mpp:F3}");

        // Load prefabs (optional)
        LoadPrefabs();
        var gameplaySet = ResolveGameplaySet();
        var theme = ResolveTheme();

        tex = WFCPreprocessor.PreprocessWithWFC(tex);

        Color32[] px = tex.GetPixels32();
        int w = tex.width, h = tex.height;

        if (px == null || px.Length != w * h)
            throw new System.Exception("Invalid texture pixels");

        float cell = mpp > 0 ? mpp : CELL_SIZE;

        // Map coordinate conversion: worldX = (pixelX - w/2) ; worldZ = (h/2 - pixelY)
        // We'll compute world position when placing objects.

        // Classify pixels into tile kinds (with progress, cancel and cyan-border skip)
        int totalPixels = w * h;
        int processed = 0;
        // Counters for diagnostics
        int cyanSkipped = 0;
        int wallsCreated = 0;
        int floorsCreated = 0;
        TileKind[,] grid = new TileKind[w, h];
        int floorTileCount = 0;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                Color32 c = px[y * w + x];
                processed++;
                if ((processed % PROGRESS_STEP) == 0)
                {
                    float prog = (float)processed / (float)totalPixels;
                    if (EditorUtility.DisplayCancelableProgressBar("Generating Map",
                        $"Processing pixel {processed}/{totalPixels}", prog))
                    {
                        EditorUtility.ClearProgressBar();
                        Debug.LogWarning("[BuildFromBlueprint] Generation cancelled by user.");
                        return;
                    }
                }

                // Skip cyan border pixels entirely (they are decorative)
                if (ColorsClose(c, COL_CYAN, COLOR_TOLERANCE))
                {
                    grid[x, y] = TileKind.Void;
                    cyanSkipped++;
                    continue;
                }

                var kind = ClassifyPixel(c);
                grid[x, y] = kind;
                if (kind == TileKind.Floor || kind == TileKind.FloorVariant)
                    floorTileCount++;
            }
        }
        EditorUtility.ClearProgressBar();

        // Create root and groups
        Transform root = new GameObject($"Arena_{tex.name}").transform;
        var floorsGroup = new GameObject("Floors").transform; floorsGroup.SetParent(root);
        var platformsGroup = new GameObject("Platforms").transform; platformsGroup.SetParent(root);
        var wallsGroup = new GameObject("Walls").transform; wallsGroup.SetParent(root);
        var bridgesGroup = new GameObject("Bridges").transform; bridgesGroup.SetParent(root);
        var spawnsGroup = new GameObject("Spawns").transform; spawnsGroup.SetParent(root);
        var pickupsGroup = new GameObject("Pickups").transform; pickupsGroup.SetParent(root);
        var waterGroup = new GameObject("Water").transform; waterGroup.SetParent(root);

        // Pre-create simple dark materials for UberStrike visual style
        Material matWall;
        Material matFloor;
        Material matPlatform;

        if (theme && theme.WallMat)
        {
            matWall = theme.WallMat;
        }
        else
        {
            matWall = new Material(Shader.Find("Standard")) { color = new Color(0.15f, 0.15f, 0.18f) };
            matWall.SetFloat("_Metallic", 0.8f); matWall.SetFloat("_Glossiness", 0.25f);
        }

        if (theme && theme.FloorMat)
        {
            matFloor = theme.FloorMat;
        }
        else
        {
            matFloor = new Material(Shader.Find("Standard")) { color = new Color(0.25f, 0.25f, 0.28f) };
            matFloor.SetFloat("_Metallic", 0.1f); matFloor.SetFloat("_Glossiness", 0.3f);
        }

        if (theme && theme.PlatformMat)
        {
            matPlatform = theme.PlatformMat;
        }
        else
        {
            matPlatform = new Material(Shader.Find("Standard")) { color = new Color(0.2f, 0.2f, 0.22f) };
            matPlatform.SetFloat("_Metallic", 0.9f); matPlatform.SetFloat("_Glossiness", 0.2f);
        }

        // Trim materials (emissive variants)
        Material matTrimCyan = new Material(Shader.Find("Unlit/Color")) { color = Color.cyan };
        matTrimCyan.EnableKeyword("_EMISSION");
        matTrimCyan.SetColor("_EmissionColor", Color.cyan * 2.0f);

        Material matTrimGreen = new Material(Shader.Find("Unlit/Color")) { color = Color.green };
        matTrimGreen.EnableKeyword("_EMISSION");
        matTrimGreen.SetColor("_EmissionColor", Color.green * 2.0f);

        Material matTrimYellow = new Material(Shader.Find("Unlit/Color")) { color = Color.yellow };
        matTrimYellow.EnableKeyword("_EMISSION");
        matTrimYellow.SetColor("_EmissionColor", Color.yellow * 2.0f);

        // Height tiers (meters)
        const float Y_E1 = 4f;
        const float Y_MID = 8f;
        const float Y_UP = 12f;

        // Environment: dark industrial atmosphere
        try
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            if (theme)
            {
                RenderSettings.fogDensity = Mathf.Max(0f, theme.FogDensity);
                RenderSettings.fogColor = theme.FogColor;
            }
            else
            {
                RenderSettings.fogDensity = 0.02f;
                RenderSettings.fogColor = new Color(0.1f, 0.1f, 0.2f);
            }

            // Main directional light
            var mainLightGO = new GameObject("Directional Light_Main");
            var mainLight = mainLightGO.AddComponent<Light>();
            mainLight.type = LightType.Directional;
            mainLight.intensity = 1.2f;
            mainLight.color = new Color(1.0f, 0.85f, 0.6f); // warm orange
            mainLightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            mainLightGO.transform.SetParent(root, false);

            // Secondary cool light
            var secLightGO = new GameObject("Directional Light_Secondary");
            var secLight = secLightGO.AddComponent<Light>();
            secLight.type = LightType.Directional;
            secLight.intensity = 0.5f;
            secLight.color = new Color(0.5f, 0.7f, 1.0f); // cool blue
            secLightGO.transform.rotation = Quaternion.Euler(-30f, 120f, 0f);
            secLightGO.transform.SetParent(root, false);

            // Ambient / sky tint
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.05f, 0.05f, 0.1f);
            RenderSettings.ambientEquatorColor = new Color(0.1f, 0.05f, 0.15f);
            RenderSettings.ambientGroundColor = Color.black;

            // Try to set an industrial skybox if found
            try
            {
                var guids = AssetDatabase.FindAssets("skybox t:material");
                foreach (var g in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(g);
                    var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (mat != null)
                    {
                        string lower = path.ToLowerInvariant();
                        if (lower.Contains("space") || lower.Contains("sci") || lower.Contains("industrial"))
                        {
                            RenderSettings.skybox = mat;
                            Debug.Log("[BuildFromBlueprint] Applied skybox: " + path);
                            break;
                        }
                    }
                }
            }
            catch { /* ignore skybox search errors */ }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BuildFromBlueprint] Failed to apply environment settings: " + ex.Message);
        }

        // Trim lights counter
        int trimLightCount = 0;

        // --- 0) Create platforms / ramps / bridges based on pixel mappings (multi-level)
        int platformCount = 0;
        int totalObjects = 0;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var tk = grid[x, y];
                float wx = (x - (w * 0.5f) + 0.5f) * cell;
                float wz = ((h * 0.5f) - y - 0.5f) * cell;

                if (tk == TileKind.Platform_E1 || tk == TileKind.Platform_Mid || tk == TileKind.Platform_Upper)
                {
                    // Enforce platform & total object limits to avoid explosion of GameObjects
                    if (platformCount >= MAX_PLATFORMS || totalObjects >= MAX_TOTAL_OBJECTS)
                    {
                        continue;
                    }
                    platformCount++;
                    totalObjects++;

                    float py = 0f;
                    if (tk == TileKind.Platform_E1) py = Y_E1;
                    else if (tk == TileKind.Platform_Mid) py = Y_MID;
                    else py = Y_UP;

                    GameObject plat = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    plat.name = $"Platform_{x}_{y}_h{py}";
                    plat.transform.position = new Vector3(wx, py, wz);
                    plat.transform.localScale = new Vector3(cell, 0.5f, cell); // thin platform
                    plat.transform.SetParent(platformsGroup, false);
                    var rend = plat.GetComponent<Renderer>();
                    if (rend != null) rend.sharedMaterial = matPlatform;

                    // Support pillar to ground
                    GameObject pillar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    pillar.name = $"Pillar_{x}_{y}_h{py}";
                    float pillarHeight = py - 0.5f; if (pillarHeight < 0.1f) pillarHeight = 0.1f;
                    pillar.transform.position = new Vector3(wx, pillarHeight / 2f, wz);
                    pillar.transform.localScale = new Vector3(cell * 0.2f, pillarHeight, cell * 0.2f);
                    pillar.transform.SetParent(plat.transform, true);
                    var pr = pillar.GetComponent<Renderer>(); if (pr != null) pr.sharedMaterial = matPlatform;

                    // Railings on exposed edges (simple check)
                    var neighbors = new (int nx, int ny, Vector3 offset)[]
                    {
                        (x+1, y, new Vector3(cell/2f, 0.5f, 0f)),
                        (x-1, y, new Vector3(-cell/2f, 0.5f, 0f)),
                        (x, y+1, new Vector3(0f, 0.5f, cell/2f)),
                        (x, y-1, new Vector3(0f, 0.5f, -cell/2f))
                    };
                    foreach (var nb in neighbors)
                    {
                        bool edge = nb.nx < 0 || nb.nx >= w || nb.ny < 0 || nb.ny >= h || (grid[nb.nx, nb.ny] == TileKind.Void);
                        if (edge)
                        {
                            GameObject rail = GameObject.CreatePrimitive(PrimitiveType.Cube);
                            rail.name = $"Railing_{x}_{y}";
                            rail.transform.SetParent(plat.transform, true);
                            rail.transform.localScale = new Vector3(cell * 0.9f, 1.0f, 0.1f);
                            rail.transform.localPosition = nb.offset + new Vector3(0f, 0.5f, 0f);
                            if (rail.GetComponent<Renderer>() != null) rail.GetComponent<Renderer>().sharedMaterial = matPlatform;
                        }
                    }

                    // Trim light strip (optimized: spaced and limited)
                    if (trimLightCount < MAX_TRIM_LIGHTS && (x % TRIM_SPACING == 0))
                    {
                        GameObject trim = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        trim.name = $"Trim_{x}_{y}";
                        trim.transform.SetParent(plat.transform, true);
                        trim.transform.localScale = new Vector3(cell, 0.05f, 0.15f);
                        trim.transform.localPosition = new Vector3(0f, 0.3f, 0f);
                        var trr = trim.GetComponent<Renderer>();
                        if (trr != null) { trr.sharedMaterial = matTrimCyan; trimLightCount++; }
                    }

                }
                else if (tk == TileKind.Ramp || tk == TileKind.Stairs)
                {
                    // Enforce total objects safety limit
                    if (totalObjects >= MAX_TOTAL_OBJECTS) continue;

                    // Approximate ramp with rotated cube
                    float angle = -30f;
                    float rampHeight = 2f;
                    if (tk == TileKind.Stairs) { angle = -20f; rampHeight = 2.5f; }
                    GameObject ramp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    ramp.name = $"Ramp_{x}_{y}";
                    ramp.transform.SetParent(platformsGroup, false);
                    ramp.transform.localScale = new Vector3(cell, 0.5f, cell * 2f);
                    ramp.transform.position = new Vector3(wx, rampHeight / 2f, wz);
                    ramp.transform.rotation = Quaternion.Euler(angle, 0f, 0f);
                    var rr = ramp.GetComponent<Renderer>(); if (rr != null) rr.sharedMaterial = matPlatform;
                    totalObjects++;
                }
                else if (tk == TileKind.Bridge)
                {
                    // Enforce total objects safety limit
                    if (totalObjects >= MAX_TOTAL_OBJECTS) continue;

                    GameObject bridge = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    bridge.name = $"Bridge_{x}_{y}";
                    bridge.transform.SetParent(bridgesGroup, false);
                    bridge.transform.localScale = new Vector3(cell * 3f, 0.2f, cell);
                    bridge.transform.position = new Vector3(wx, Y_E1, wz);
                    var br = bridge.GetComponent<Renderer>(); if (br != null) br.sharedMaterial = matPlatform;
                    totalObjects++;
                }
            }
        }

        // Batch groups to reduce object count (platforms/bridges)
        try
        {
            CombineAndReplaceGroup(platformsGroup, "Platforms_Combined", matPlatform);
        }
        catch (Exception) { /* non-fatal batching errors ignored */ }

        try
        {
            CombineAndReplaceGroup(bridgesGroup, "Bridges_Combined", matPlatform);
        }
        catch (Exception) { /* ignore */ }

        // 1) Flood-fill floors into connected rooms -> produce one mesh per region
        bool[,] visited = new bool[w, h];
        List<Mesh> floorMeshes = new List<Mesh>();
        List<Vector3> floorPositions = new List<Vector3>(); // for grouping
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (visited[x, y]) continue;
                if (grid[x, y] != TileKind.Floor && grid[x, y] != TileKind.FloorVariant) continue;

                // BFS flood fill
                List<Vector2Int> region = new List<Vector2Int>();
                Queue<Vector2Int> q = new Queue<Vector2Int>();
                q.Enqueue(new Vector2Int(x, y));
                visited[x, y] = true;

                while (q.Count > 0)
                {
                    var p = q.Dequeue();
                    region.Add(p);
                    var nbrs = new Vector2Int[] {
                        new Vector2Int(p.x + 1, p.y),
                        new Vector2Int(p.x - 1, p.y),
                        new Vector2Int(p.x, p.y + 1),
                        new Vector2Int(p.x, p.y - 1)
                    };
                    foreach (var n in nbrs)
                    {
                        if (n.x >= 0 && n.x < w && n.y >= 0 && n.y < h && !visited[n.x, n.y])
                        {
                            if (grid[n.x, n.y] == TileKind.Floor || grid[n.x, n.y] == TileKind.FloorVariant)
                            {
                                visited[n.x, n.y] = true;
                                q.Enqueue(n);
                            }
                        }
                    }
                }

                // Create one mesh for this region (merged quads)
                var mesh = BuildQuadMeshForRegion(region, cell, w, h);
                floorMeshes.Add(mesh);
                floorsCreated++;

                // Create GameObject (respect total object safety limit)
                if (totalObjects >= MAX_TOTAL_OBJECTS)
                {
                    Debug.LogWarning("[BuildFromBlueprint] Object limit reached, skipping creation of additional floor region GameObjects.");
                }
                else
                {
                    var go = new GameObject($"FloorRegion_{floorMeshes.Count}");
                    go.transform.SetParent(floorsGroup, false);
                    var mf = go.AddComponent<MeshFilter>();
                    mf.sharedMesh = mesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    // Try to use floor prefab material if available
                    if (p_Floor)
                    {
                        var prefabMr = p_Floor.GetComponentInChildren<MeshRenderer>();
                        if (prefabMr != null) mr.sharedMaterial = prefabMr.sharedMaterial;
                    }
                    go.isStatic = true;
                    totalObjects++;
                }
            }
        }

        // Combine floor region meshes into a single batched floor mesh to drastically reduce GameObject count
        if (floorMeshes != null && floorMeshes.Count > 0)
        {
            GameObject floorsGOCombined = null; // <-- DECLARED HERE
            try
            {
                var floorCombines = new List<CombineInstance>(floorMeshes.Count);
                for (int i = 0; i < floorMeshes.Count; i++)
                {
                    var ci = new CombineInstance { mesh = floorMeshes[i], transform = Matrix4x4.identity };
                    floorCombines.Add(ci);
                }

                Mesh combinedFloor = ProgressiveCombiner.Combine(floorCombines, GEOMETRY_CHUNK_SIZE);
                if (combinedFloor == null)
                {
                    Debug.LogWarning("[BuildFromBlueprint] Floor combination returned null mesh.");
                }
                else
                {
                    floorsGOCombined = new GameObject("Floors_Combined"); // <-- ASSIGNED HERE
                    floorsGOCombined.transform.SetParent(floorsGroup, false);
                    var mfFlo = floorsGOCombined.AddComponent<MeshFilter>();
                    mfFlo.sharedMesh = combinedFloor;
                    var mrFlo = floorsGOCombined.AddComponent<MeshRenderer>();
                    if (p_Floor)
                    {
                        var pmr = p_Floor.GetComponentInChildren<MeshRenderer>();
                        if (pmr != null) mrFlo.sharedMaterial = pmr.sharedMaterial;
                        else mrFlo.sharedMaterial = matFloor;
                    }
                    else
                    {
                        mrFlo.sharedMaterial = matFloor;
                    }
                    floorsGOCombined.isStatic = true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BuildFromBlueprint] Floor combining failed: {ex.Message}");
            }

            // Remove old per-region GameObjects to free memory
            for (int i = floorsGroup.childCount - 1; i >= 0; i--)
            {
                var child = floorsGroup.GetChild(i);
                if (child == null || (floorsGOCombined != null && child == floorsGOCombined.transform)) continue; // <-- FIXED
                GameObject.DestroyImmediate(child.gameObject);
            }
        }

        // 2) Merge explicit wall pixels using maximal rectangles and aggregate perimeter walls
        var cubeMesh = GetPrimitiveMesh(PrimitiveType.Cube);
        var wallRectangles = ComputeWallRectangles(grid, w, h);
        var perimeterPrisms = CollectPerimeterPrisms(grid, w, h, cell, WALL_HEIGHT);

        List<CombineInstance> allWalls = new List<CombineInstance>(wallRectangles.Count + perimeterPrisms.Count);

        foreach (var rect in wallRectangles)
        {
            float rectCenterX = ((rect.x + rect.width * 0.5f) - (w * 0.5f)) * cell;
            float rectCenterZ = ((h * 0.5f) - (rect.y + rect.height * 0.5f)) * cell;
            Vector3 center = new Vector3(rectCenterX, WALL_HEIGHT / 2f, rectCenterZ);
            Vector3 size = new Vector3(rect.width * cell, WALL_HEIGHT, rect.height * cell);
            Matrix4x4 mat = Matrix4x4.TRS(center, Quaternion.identity, size);
            allWalls.Add(new CombineInstance { mesh = cubeMesh, transform = mat });
        }

        wallsCreated += wallRectangles.Count;

        foreach (var prism in perimeterPrisms)
        {
            Matrix4x4 mat = Matrix4x4.TRS(prism.center, Quaternion.identity, prism.size);
            allWalls.Add(new CombineInstance { mesh = cubeMesh, transform = mat });
        }

        int totalWallBoxes = allWalls.Count;

        if (totalWallBoxes > 0)
        {
            Mesh wallsMesh = ProgressiveCombiner.Combine(allWalls, GEOMETRY_CHUNK_SIZE);
            int wallStage1 = ProgressiveCombiner.LastStage1Chunks;
            Debug.Log($"[Combine] stage1 chunks: {wallStage1} | stage2: 1 (walls)");

            // Ensure combined walls have the intended WALL_HEIGHT by adjusting vertex Y-range and scaling.
            try
            {
                var verts = wallsMesh.vertices;
                if (verts == null || verts.Length == 0)
                {
                    Debug.LogWarning("[BuildFromBlueprint] Walls mesh has no vertices.");
                }
                else
                {
                    // Find current min/max Y to compute accurate height
                    float minY = float.MaxValue;
                    float maxY = float.MinValue;
                    for (int vi = 0; vi < verts.Length; vi++)
                    {
                        if (verts[vi].y < minY) minY = verts[vi].y;
                        if (verts[vi].y > maxY) maxY = verts[vi].y;
                    }

                    float currentHeight = maxY - minY;
                    if (currentHeight > 0.0001f)
                    {
                        float splitY = minY + currentHeight * 0.5f;
                        for (int vi = 0; vi < verts.Length; vi++)
                        {
                            bool isWallTop = verts[vi].y >= splitY - 0.0001f;
                            verts[vi].y = isWallTop ? WALL_HEIGHT : 0f;
                        }

                        // Assign back and recalc
                        wallsMesh.vertices = verts;
                        wallsMesh.RecalculateBounds();
                        wallsMesh.RecalculateNormals();
                    }
                    else
                    {
                        Debug.LogWarning("[BuildFromBlueprint] Walls combined mesh has negligible current height; skipping height adjust.");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BuildFromBlueprint] Wall mesh height adjustment failed: {ex.Message}");
            }

            var wallsGO = new GameObject("Walls_Combined");
            wallsGO.transform.SetParent(wallsGroup, false);
            var wf = wallsGO.AddComponent<MeshFilter>();
            wf.sharedMesh = wallsMesh;
            var wr = wallsGO.AddComponent<MeshRenderer>();
            // Try to assign wall material if available
            if (p_Wall)
            {
                var mr = p_Wall.GetComponentInChildren<MeshRenderer>();
                if (mr != null) wr.sharedMaterial = mr.sharedMaterial;
                else wr.sharedMaterial = matWall;
            }
            else
            {
                wr.sharedMaterial = matWall;
            }
            wallsGO.isStatic = true;
            var boxCollider = wallsGO.AddComponent<BoxCollider>();
            var bounds = wallsMesh.bounds;
            boxCollider.center = bounds.center;
            boxCollider.size = bounds.size;

            Debug.Log($"[BuildFromBlueprint] Walls rectangles: {totalWallBoxes}, Walls combined: 1");
        }
        else
        {
            Debug.Log("[BuildFromBlueprint] Walls rectangles: 0, Walls combined: 0");
        }

        // 4) Place water tiles (blue) - one mesh per contiguous water region (optional simple approach: create a quad per water tile and combine)
        List<CombineInstance> waterCombine = new List<CombineInstance>();
        for (int yy = 0; yy < h; yy++)
        {
            for (int xx = 0; xx < w; xx++)
            {
                if (grid[xx, yy] == TileKind.Water)
                {
                    float wx = (xx - (w * 0.5f) + 0.5f) * cell;
                    float wz = ((h * 0.5f) - yy - 0.5f) * cell;
                    Vector3 center = new Vector3(wx, -0.5f, wz); // Y = -0.5 per spec
                    Matrix4x4 mat = Matrix4x4.TRS(center, Quaternion.identity, new Vector3(cell, 0.1f, cell));
                    CombineInstance ci = new CombineInstance { mesh = cubeMesh, transform = mat };
                    waterCombine.Add(ci);
                }
            }
        }

        if (waterCombine.Count > 0)
        {
            Mesh waterMesh = ProgressiveCombiner.Combine(waterCombine, GEOMETRY_CHUNK_SIZE);
            if (waterMesh == null)
            {
                Debug.LogWarning("[BuildFromBlueprint] Water combination returned null mesh.");
            }
            else
            {
                var wgo = new GameObject("Water_Combined");
                wgo.transform.SetParent(waterGroup, false);
                var wf = wgo.AddComponent<MeshFilter>(); wf.sharedMesh = waterMesh;
                var wr = wgo.AddComponent<MeshRenderer>();
                // try to assign material from prefab
                if (p_Water)
                {
                    var mr = p_Water.GetComponentInChildren<MeshRenderer>();
                    if (mr != null) wr.sharedMaterial = mr.sharedMaterial;
                }
                wgo.isStatic = true;
            }
        }

        // 5) Spawn & pickup clustering
        var placedSpawns = new List<Vector3>();
        var placedPickups = new List<Vector3>();
        var teleporterObjects = new List<GameObject>();
        bool spawnSeparationIssue = false;
        bool pickupSeparationIssue = false;
        float spawnMinSeparation = gameplaySet ? Mathf.Max(0.5f, gameplaySet.MinSpawnSeparation) : 6f;
        float pickupMinSeparation = gameplaySet ? Mathf.Max(0.5f, gameplaySet.MinPickupSeparation) : 4f;
        float floorRaycast = gameplaySet ? Mathf.Max(0.5f, gameplaySet.FloorRaycastDown) : 5f;
        int spawnNeutralCount = 0, spawnRedCount = 0, spawnGreenCount = 0;
        int pickupHealthCount = 0, pickupArmorCount = 0;
        int jumpPadCount = 0;

        bool[,] clusterVisited = new bool[w, h];

        for (int yy = 0; yy < h; yy++)
        {
            for (int xx = 0; xx < w; xx++)
            {
                if (clusterVisited[xx, yy]) continue;

                TileKind tk = grid[xx, yy];
                if (tk != TileKind.SpawnNeutral && tk != TileKind.SpawnRed && tk != TileKind.SpawnGreen
                    && tk != TileKind.PickupHealth && tk != TileKind.PickupArmor && tk != TileKind.JumpPad && tk != TileKind.Teleporter)
                    continue;

                // BFS cluster
                List<Vector2Int> cluster = new List<Vector2Int>();
                Queue<Vector2Int> q = new Queue<Vector2Int>();
                q.Enqueue(new Vector2Int(xx, yy));
                clusterVisited[xx, yy] = true;

                while (q.Count > 0)
                {
                    var p = q.Dequeue();
                    cluster.Add(p);
                    var nbrs = new Vector2Int[] {
                        new Vector2Int(p.x + 1, p.y),
                        new Vector2Int(p.x - 1, p.y),
                        new Vector2Int(p.x, p.y + 1),
                        new Vector2Int(p.x, p.y - 1)
                    };
                    foreach (var n in nbrs)
                    {
                        if (n.x >= 0 && n.x < w && n.y >= 0 && n.y < h && !clusterVisited[n.x, n.y])
                        {
                            if (grid[n.x, n.y] == tk)
                            {
                                clusterVisited[n.x, n.y] = true;
                                q.Enqueue(n);
                            }
                        }
                    }
                }

                // Compute center in pixel space then convert to world
                Vector2 avg = Vector2.zero;
                foreach (var p in cluster) avg += new Vector2(p.x + 0.5f, p.y + 0.5f);
                avg /= cluster.Count;
                float worldX = (avg.x - (w * 0.5f)) * cell;
                float worldZ = ((h * 0.5f) - avg.y) * cell;

                if (totalObjects >= MAX_TOTAL_OBJECTS)
                {
                    Debug.LogWarning("[BuildFromBlueprint] Object limit reached, skipping spawn/pickup creation.");
                    continue;
                }

                Vector3 snapped = SnapToFloor(worldX, worldZ, floorRaycast, out bool snappedHit);
                Vector3 candidateXZ = new Vector3(snapped.x, 0f, snapped.z);

                if (!snappedHit)
                {
                    Debug.LogWarning($"[QC] Floor raycast missed at ({worldX:F2},{worldZ:F2}); using fallback height.");
                }

                if (tk == TileKind.SpawnNeutral || tk == TileKind.SpawnRed || tk == TileKind.SpawnGreen)
                {
                    if (IsTooClose(placedSpawns, candidateXZ, spawnMinSeparation))
                    {
                        spawnSeparationIssue = true;
                        Debug.LogWarning($"[QC] Spawn skipped (separation) near {candidateXZ}");
                        continue;
                    }

                    GameObject prefab = null;
                    if (gameplaySet)
                    {
                        prefab = tk switch
                        {
                            TileKind.SpawnNeutral => gameplaySet.SpawnNeutral,
                            TileKind.SpawnRed => gameplaySet.SpawnRed,
                            TileKind.SpawnGreen => gameplaySet.SpawnGreen,
                            _ => null
                        };
                    }

                    GameObject marker = InstantiateGameplayPrefab(prefab, spawnsGroup)
                        ?? InstantiateGameplayPrefab(p_Spawn, spawnsGroup);

                    bool primitiveSpawn = false;
                    if (!marker)
                    {
                        marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                        marker.transform.SetParent(spawnsGroup, false);
                        marker.transform.localScale = new Vector3(4f, 2.5f, 4f);
                        primitiveSpawn = true;
                    }

                    Vector3 spawnPos = primitiveSpawn ? snapped + new Vector3(0f, 2.5f, 0f) : snapped;
                    marker.transform.position = spawnPos;
                    marker.name = tk switch
                    {
                        TileKind.SpawnNeutral => $"Spawn_Neutral_{++spawnNeutralCount}",
                        TileKind.SpawnRed => $"Spawn_Red_{++spawnRedCount}",
                        TileKind.SpawnGreen => $"Spawn_Green_{++spawnGreenCount}",
                        _ => marker.name
                    };
                    marker.isStatic = false;
                    if (marker.CompareTag("Untagged")) marker.tag = "SpawnPoint";
                    ApplyThemeMaterialIfMissing(marker, theme ? theme.SpawnMat : null);

                    placedSpawns.Add(candidateXZ);
                    totalObjects++;
                }
                else if (tk == TileKind.PickupHealth || tk == TileKind.PickupArmor || tk == TileKind.JumpPad || tk == TileKind.Teleporter)
                {
                    if (IsTooClose(placedPickups, candidateXZ, pickupMinSeparation))
                    {
                        pickupSeparationIssue = true;
                        Debug.LogWarning($"[QC] Pickup skipped (separation) near {candidateXZ}");
                        continue;
                    }

                    GameObject prefab = null;
                    Material themeMat = null;
                    bool primitive = false;
                    GameObject instance = null;

                    switch (tk)
                    {
                        case TileKind.PickupHealth:
                            prefab = gameplaySet ? gameplaySet.PickupHealth : null;
                            instance = InstantiateGameplayPrefab(prefab, pickupsGroup) ?? InstantiateGameplayPrefab(p_Health, pickupsGroup);
                            if (!instance)
                            {
                                instance = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                                instance.transform.SetParent(pickupsGroup, false);
                                instance.transform.localScale = Vector3.one * 0.75f;
                                primitive = true;
                            }
                            instance.name = $"Pickup_Health_{++pickupHealthCount}";
                            themeMat = theme ? theme.PickupMat : null;
                            break;
                        case TileKind.PickupArmor:
                            prefab = gameplaySet ? gameplaySet.PickupArmor : null;
                            instance = InstantiateGameplayPrefab(prefab, pickupsGroup) ?? InstantiateGameplayPrefab(p_Armor, pickupsGroup);
                            if (!instance)
                            {
                                instance = GameObject.CreatePrimitive(PrimitiveType.Cube);
                                instance.transform.SetParent(pickupsGroup, false);
                                instance.transform.localScale = new Vector3(0.75f, 0.5f, 0.75f);
                                primitive = true;
                            }
                            instance.name = $"Pickup_Armor_{++pickupArmorCount}";
                            themeMat = theme ? theme.PickupMat : null;
                            break;
                        case TileKind.JumpPad:
                            prefab = gameplaySet ? gameplaySet.JumpPad : null;
                            instance = InstantiateGameplayPrefab(prefab, pickupsGroup) ?? InstantiateGameplayPrefab(p_Jump, pickupsGroup);
                            if (!instance)
                            {
                                instance = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                                instance.transform.SetParent(pickupsGroup, false);
                                instance.transform.localScale = new Vector3(2.5f, 0.1f, 2.5f);
                                primitive = true;
                            }
                            instance.name = $"JumpPad_{++jumpPadCount}";
                            themeMat = theme ? theme.JumpPadMat : null;
                            if (instance.CompareTag("Untagged")) instance.tag = "JumpPad";
                            break;
                        case TileKind.Teleporter:
                            prefab = gameplaySet ? gameplaySet.Teleporter : null;
                            instance = InstantiateGameplayPrefab(prefab, pickupsGroup) ?? InstantiateGameplayPrefab(p_Tele, pickupsGroup);
                            if (!instance)
                            {
                                instance = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                                instance.transform.SetParent(pickupsGroup, false);
                                instance.transform.localScale = new Vector3(2f, 1.5f, 2f);
                                primitive = true;
                            }
                            teleporterObjects.Add(instance);
                            instance.name = $"Teleporter_{teleporterObjects.Count:00}";
                            themeMat = theme ? theme.TeleporterMat : null;
                            break;
                    }

                    if (instance)
                    {
                        Vector3 pos = snapped;
                        if (tk == TileKind.Teleporter && primitive)
                            pos += new Vector3(0f, 1.5f, 0f);
                        instance.transform.position = pos;
                        ApplyThemeMaterialIfMissing(instance, themeMat);
                        placedPickups.Add(candidateXZ);
                        totalObjects++;
                    }
                }
            }
        }

        PairTeleporters(teleporterObjects);
        bool oddTeleporter = (teleporterObjects.Count % 2) == 1;
        if (oddTeleporter)
        {
            Debug.LogWarning("[QC] Warning: odd teleporter count.");
        }

        // 6) Finalize floors: attach renderers and mark static (we already created floor mesh objects)
        foreach (Transform t in floorsGroup)
        {
            t.gameObject.isStatic = true;
        }

        // 7) Combine floor meshes into single mesh per material if desired (we already used one mesh per region)
        // Optional: build navmesh on floors or root
        TryBuildNavMesh(root.gameObject);

        // Mark scene dirty
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        // Diagnostics
        Debug.Log($"[BuildFromBlueprint] Build complete '{tex.name}' {w}x{h} px, scale {cell}m/px.");
        Debug.Log($"    Floor regions: {floorsGroup.childCount}");
        Debug.Log($"    Spawn markers: {spawnsGroup.childCount}");
        Debug.Log($"    Pickups: {pickupsGroup.childCount}");
        Debug.Log($"    Walls combined: {(wallsGroup.childCount > 0 ? 1 : 0)}");
        Debug.Log($"[BuildFromBlueprint] Counts - wallsCreated:{wallsCreated} floorsCreated:{floorsCreated} cyanSkipped:{cyanSkipped} totalObjectsUsed:{totalObjects}");
        float mapWidthMeters = w * cell;
        float mapHeightMeters = h * cell;
        float floorArea = floorTileCount * cell * cell;
        Debug.Log($"[QC] sizeMeters: {mapWidthMeters:F1}x{mapHeightMeters:F1} | floorArea(m²): {floorArea:F1} | walls: {wallsCreated}");
        Debug.Log($"[QC] spawns: yellow={spawnNeutralCount} red={spawnRedCount} green={spawnGreenCount} | pickups: health={pickupHealthCount} armor={pickupArmorCount} | teleporters={teleporterObjects.Count} (pairs={teleporterObjects.Count / 2})");
        if (spawnSeparationIssue)
            Debug.LogWarning("[QC] Warning: spawn separation violation (skipped placement).");
        if (pickupSeparationIssue)
            Debug.LogWarning("[QC] Warning: pickup separation violation (skipped placement).");
        if (floorArea < 700f)
            Debug.LogWarning($"[QC] Warning: floor area under 700 m² (actual {floorArea:F1}).");

        Selection.activeTransform = root;
    }

    // ---------- Helper functions ----------

    private static TileKind ClassifyPixel(Color32 c)
    {
        if (c.r == 0 && c.g == 255 && c.b == 255)
        {
            return TileKind.Void;
        }
        // Treat cyan as decorative border — skip entirely
        if (ColorsClose(c, COL_CYAN, COLOR_TOLERANCE)) return TileKind.Void;

        // Structural mappings
        if (ColorsClose(c, COL_BLACK, COLOR_TOLERANCE)) return TileKind.WallPixel;   // BLACK = tall wall
        // Gray shades map to walkable floor at ground level (not elevated platforms)
        if (ColorsClose(c, COL_VERYDARK, COLOR_TOLERANCE)) return TileKind.Floor;
        if (ColorsClose(c, COL_MEDGRAY, COLOR_TOLERANCE)) return TileKind.Floor;
        if (ColorsClose(c, COL_LIGHTGRAY, COLOR_TOLERANCE)) return TileKind.Floor;
        if (ColorsClose(c, COL_WHITE, COLOR_TOLERANCE)) return TileKind.Void;        // white = empty

        // Gameplay / misc
        if (ColorsClose(c, COL_BLUE, COLOR_TOLERANCE)) return TileKind.Water;        // blue = water features
        if (ColorsClose(c, COL_BROWN, COLOR_TOLERANCE)) return TileKind.Ramp;       // brown -> ramp
        if (ColorsClose(c, COL_PURPLE, COLOR_TOLERANCE)) return TileKind.Bridge;     // purple -> bridge
        if (ColorsClose(c, COL_RED, COLOR_TOLERANCE)) return TileKind.SpawnRed;
        if (ColorsClose(c, COL_GREEN, COLOR_TOLERANCE)) return TileKind.SpawnGreen;
        if (ColorsClose(c, COL_YELLOW, COLOR_TOLERANCE)) return TileKind.SpawnNeutral;
        if (ColorsClose(c, COL_HEALTH, COLOR_TOLERANCE)) return TileKind.PickupHealth;
        if (ColorsClose(c, COL_ARMOR, COLOR_TOLERANCE)) return TileKind.PickupArmor;
        if (ColorsClose(c, COL_JUMPPAD, COLOR_TOLERANCE)) return TileKind.JumpPad;
        if (ColorsClose(c, COL_TELEPORTER, COLOR_TOLERANCE)) return TileKind.Teleporter;
        if (ColorsClose(c, COL_ORANGE, COLOR_TOLERANCE)) return TileKind.Stairs;     // orange -> stairs

        // Fallback: anything else is treated as Void/empty
        return TileKind.Void;
    }

    /// <summary>
    /// Builds a quad-per-tile mesh for a connected region of tile positions.
    /// Floors are at Y=0.
    /// </summary>
    private static Mesh BuildQuadMeshForRegion(List<Vector2Int> region, float cell, int texW, int texH)
    {
        Mesh m = new Mesh();
        m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        int n = region.Count;
        Vector3[] verts = new Vector3[4 * n];
        Vector2[] uvs = new Vector2[4 * n];
        int[] tris = new int[6 * n];

        for (int i = 0; i < n; i++)
        {
            var p = region[i];
            float x0 = (p.x - (texW * 0.5f)) * cell;
            float z0 = ((texH * 0.5f) - p.y) * cell;
            float half = cell * 0.5f;

            Vector3 bl = new Vector3(x0 + (-half + cell / 2f), 0f, z0 + (-half + cell / 2f));
            Vector3 br = new Vector3(x0 + (half + cell / 2f), 0f, z0 + (-half + cell / 2f));
            Vector3 tl = new Vector3(x0 + (-half + cell / 2f), 0f, z0 + (half + cell / 2f));
            Vector3 tr = new Vector3(x0 + (half + cell / 2f), 0f, z0 + (half + cell / 2f));

            int vi = i * 4;
            verts[vi + 0] = bl;
            verts[vi + 1] = br;
            verts[vi + 2] = tl;
            verts[vi + 3] = tr;

            uvs[vi + 0] = new Vector2(0, 0);
            uvs[vi + 1] = new Vector2(1, 0);
            uvs[vi + 2] = new Vector2(0, 1);
            uvs[vi + 3] = new Vector2(1, 1);

            int ti = i * 6;
            tris[ti + 0] = vi + 0;
            tris[ti + 1] = vi + 2;
            tris[ti + 2] = vi + 1;
            tris[ti + 3] = vi + 2;
            tris[ti + 4] = vi + 3;
            tris[ti + 5] = vi + 1;
        }

        m.vertices = verts;
        m.uv = uvs;
        m.triangles = tris;
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    private static List<WallRect> ComputeWallRectangles(TileKind[,] grid, int texW, int texH)
    {
        var rectangles = new List<WallRect>();
        bool[,] consumed = new bool[texW, texH];

        for (int y = 0; y < texH; y++)
        {
            int x = 0;
            while (x < texW)
            {
                if (consumed[x, y] || grid[x, y] != TileKind.WallPixel)
                {
                    x++;
                    continue;
                }

                int startX = x;
                while (x < texW && grid[x, y] == TileKind.WallPixel && !consumed[x, y])
                {
                    x++;
                }

                int width = x - startX;
                int height = 1;
                bool expanded = true;

                while (expanded)
                {
                    expanded = false;

                    bool heightExpanded = true;
                    while (heightExpanded && (y + height) < texH)
                    {
                        for (int xi = startX; xi < startX + width; xi++)
                        {
                            if (grid[xi, y + height] != TileKind.WallPixel || consumed[xi, y + height])
                            {
                                heightExpanded = false;
                                break;
                            }
                        }

                        if (heightExpanded)
                        {
                            height++;
                            expanded = true;
                        }
                    }

                    bool widthExpanded = true;
                    while (widthExpanded && (startX + width) < texW)
                    {
                        int candidateX = startX + width;
                        widthExpanded = true;

                        for (int yi = y; yi < y + height; yi++)
                        {
                            if (grid[candidateX, yi] != TileKind.WallPixel || consumed[candidateX, yi])
                            {
                                widthExpanded = false;
                                break;
                            }
                        }

                        if (widthExpanded)
                        {
                            width++;
                            expanded = true;
                        }
                    }
                }

                for (int yy = y; yy < y + height; yy++)
                {
                    for (int xx = startX; xx < startX + width; xx++)
                    {
                        consumed[xx, yy] = true;
                    }
                }

                rectangles.Add(new WallRect(startX, y, width, height));
                x = startX + width;
            }
        }

        return rectangles;
    }

    private static List<WallPrism> CollectPerimeterPrisms(TileKind[,] grid, int texW, int texH, float cell, float wallHeight)
    {
        var prisms = new List<WallPrism>();
        bool[,] isFloor = new bool[texW, texH];

        for (int y = 0; y < texH; y++)
        {
            for (int x = 0; x < texW; x++)
            {
                isFloor[x, y] = grid[x, y] == TileKind.Floor || grid[x, y] == TileKind.FloorVariant;
            }
        }

        bool[,] verticalEdges = new bool[texW + 1, texH];
        bool[,] horizontalEdges = new bool[texW, texH + 1];

        Func<int, int, bool> shouldSkipWall = (nx, ny) =>
        {
            if (nx < 0 || nx >= texW || ny < 0 || ny >= texH)
            {
                return false;
            }

            var nk = grid[nx, ny];
            return nk == TileKind.Door || nk == TileKind.Water || nk == TileKind.Glass || nk == TileKind.Floor || nk == TileKind.FloorVariant || nk == TileKind.WallPixel;
        };

        for (int y = 0; y < texH; y++)
        {
            for (int x = 0; x < texW; x++)
            {
                if (!isFloor[x, y])
                {
                    continue;
                }

                if (!shouldSkipWall(x + 1, y))
                {
                    verticalEdges[x + 1, y] = true;
                }

                if (!shouldSkipWall(x - 1, y))
                {
                    verticalEdges[x, y] = true;
                }

                if (!shouldSkipWall(x, y + 1))
                {
                    horizontalEdges[x, y + 1] = true;
                }

                if (!shouldSkipWall(x, y - 1))
                {
                    horizontalEdges[x, y] = true;
                }
            }
        }

        float perimeterThickness = 0.1f;

        for (int col = 0; col <= texW; col++)
        {
            int start = -1;
            for (int row = 0; row < texH; row++)
            {
                if (verticalEdges[col, row])
                {
                    if (start == -1)
                    {
                        start = row;
                    }
                }
                else if (start != -1)
                {
                    int length = row - start;
                    float centerZ = ((texH * 0.5f) - (start + length / 2f)) * cell;
                    float centerX = (col - texW * 0.5f) * cell;
                    Vector3 center = new Vector3(centerX, wallHeight / 2f, centerZ);
                    Vector3 size = new Vector3(perimeterThickness, wallHeight, length * cell);
                    prisms.Add(new WallPrism(center, size));
                    start = -1;
                }
            }

            if (start != -1)
            {
                int length = texH - start;
                float centerZ = ((texH * 0.5f) - (start + length / 2f)) * cell;
                float centerX = (col - texW * 0.5f) * cell;
                Vector3 center = new Vector3(centerX, wallHeight / 2f, centerZ);
                Vector3 size = new Vector3(perimeterThickness, wallHeight, length * cell);
                prisms.Add(new WallPrism(center, size));
            }
        }

        for (int row = 0; row <= texH; row++)
        {
            int start = -1;
            for (int col = 0; col < texW; col++)
            {
                if (horizontalEdges[col, row])
                {
                    if (start == -1)
                    {
                        start = col;
                    }
                }
                else if (start != -1)
                {
                    int length = col - start;
                    float centerX = ((start + length / 2f) - texW * 0.5f) * cell;
                    float centerZ = ((texH * 0.5f) - row) * cell;
                    Vector3 center = new Vector3(centerX, wallHeight / 2f, centerZ);
                    Vector3 size = new Vector3(length * cell, wallHeight, perimeterThickness);
                    prisms.Add(new WallPrism(center, size));
                    start = -1;
                }
            }

            if (start != -1)
            {
                int length = texW - start;
                float centerX = ((start + length / 2f) - texW * 0.5f) * cell;
                float centerZ = ((texH * 0.5f) - row) * cell;
                Vector3 center = new Vector3(centerX, wallHeight / 2f, centerZ);
                Vector3 size = new Vector3(length * cell, wallHeight, perimeterThickness);
                prisms.Add(new WallPrism(center, size));
            }
        }

        return prisms;
    }

    private static Mesh GetPrimitiveMesh(PrimitiveType t)
    {
        // Create a temporary primitive and return its shared mesh
        GameObject go = GameObject.CreatePrimitive(t);
        Mesh m = go.GetComponent<MeshFilter>().sharedMesh;
        GameObject.DestroyImmediate(go);
        return m;
    }

    /// <summary>
    /// Loads prefabs optionally used for nicer visuals.
    /// </summary>
    private static void LoadPrefabs()
    {
        p_Floor = FindPrefab("Floor");
        p_Wall = FindPrefab("Wall");
        p_Glass = FindPrefab("Bridge_Glass");
        p_Water = FindPrefab("Water_Tile");
        p_Spawn = FindPrefab("SpawnPoint");
        p_Jump = FindPrefab("JumpPad");
        p_Tele = FindPrefab("Teleporter");
        p_Health = FindPrefab("Pickup_Health");
        p_Armor = FindPrefab("Pickup_Armor");
    }

    private static MapTheme ResolveTheme()
    {
        if (s_ThemeOverride) return s_ThemeOverride;

        var guids = AssetDatabase.FindAssets("t:MapTheme");
        if (guids != null && guids.Length > 0)
        {
            var path = AssetDatabase.GUIDToAssetPath(guids[0]);
            return AssetDatabase.LoadAssetAtPath<MapTheme>(path);
        }

        return null;
    }

    private static MapGameplaySet ResolveGameplaySet()
    {
        if (s_GameplayOverride) return s_GameplayOverride;

        var guids = AssetDatabase.FindAssets("t:MapGameplaySet");
        if (guids == null || guids.Length == 0)
        {
            Debug.LogWarning("[BuildFromBlueprint] No MapGameplaySet found; using primitive fallbacks.");
            return null;
        }

        var path = AssetDatabase.GUIDToAssetPath(guids[0]);
        var asset = AssetDatabase.LoadAssetAtPath<MapGameplaySet>(path);
        if (!asset)
        {
            Debug.LogWarning($"[BuildFromBlueprint] Failed to load MapGameplaySet at {path}; using primitive fallbacks.");
        }

        return asset;
    }

    /// <summary>
    /// Reused helper to find prefabs in project.
    /// </summary>
    private static GameObject FindPrefab(string shortName)
    {
        var res = Resources.Load<GameObject>(shortName);
        if (res) return res;

        string[] searchRoots = new[]
        {
            PREFABS_DIR,
            "Assets/UberUnity/Assets/Prefabs",
            "Assets/UberUnity/Prefabs",
            "Assets/UberUnity/Assets/Resources",
            "Assets/UberUnity/Resources"
        };

        foreach (var root in searchRoots)
        {
            try
            {
                var guids = AssetDatabase.FindAssets(shortName + " t:prefab", new[] { root });
                if (guids != null && guids.Length > 0)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (go != null) return go;
                }
            }
            catch { /* ignore invalid folders */ }
        }

        var guidsAll = AssetDatabase.FindAssets(shortName + " t:prefab");
        if (guidsAll != null && guidsAll.Length > 0)
        {
            var path = AssetDatabase.GUIDToAssetPath(guidsAll[0]);
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go != null) return go;
        }

        return null;
    }

    /// <summary>
    /// Colors close with per-channel tolerance.
    /// </summary>
    private static bool ColorsClose(Color32 a, Color32 b, int tolerance)
    {
        int dr = a.r - b.r; if (dr < 0) dr = -dr;
        int dg = a.g - b.g; if (dg < 0) dg = -dg;
        int db = a.b - b.b; if (db < 0) db = -db;
        return dr <= tolerance && dg <= tolerance && db <= tolerance;
    }

    private static bool ColorsClose(Color32 a, Color32 b)
    {
        return ColorsClose(a, b, COLOR_TOLERANCE);
    }

    /// <summary>
    /// Combine meshes under a group into a single combined mesh GameObject, then destroy originals.
    /// This reduces draw calls and object count for large procedurally-created groups.
    /// </summary>
    private static void CombineAndReplaceGroup(Transform group, string combinedName, Material mat)
    {
        if (group == null || group.gameObject == null) return;
        if (group.childCount == 0) { GameObject.DestroyImmediate(group.gameObject); return; }

        var combines = new List<CombineInstance>();
        var toDestroy = new List<GameObject>();

        // Collect MeshFilters from children
        for (int i = 0; i < group.childCount; i++)
        {
            var child = group.GetChild(i);
            var mf = child.GetComponent<MeshFilter>();
            var mr = child.GetComponent<MeshRenderer>();
            if (mf != null && mf.sharedMesh != null)
            {
                CombineInstance ci = new CombineInstance
                {
                    mesh = mf.sharedMesh,
                    transform = mf.transform.localToWorldMatrix
                };
                combines.Add(ci);
                toDestroy.Add(child.gameObject);
            }
        }

        if (combines.Count == 0)
        {
            // Nothing to combine; remove empty group container
            GameObject.DestroyImmediate(group.gameObject);
            return;
        }

        Mesh combined = new Mesh();
        combined.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        combined.CombineMeshes(combines.ToArray(), true, true);

        var combinedGO = new GameObject(combinedName);
        combinedGO.transform.SetParent(group.parent, false);
        var mfCombined = combinedGO.AddComponent<MeshFilter>();
        mfCombined.sharedMesh = combined;
        var mrCombined = combinedGO.AddComponent<MeshRenderer>();
        if (mat != null) mrCombined.sharedMaterial = mat;
        combinedGO.isStatic = true;

        // Destroy originals
        foreach (var go in toDestroy) GameObject.DestroyImmediate(go);

        // Destroy the empty group holder
        GameObject.DestroyImmediate(group.gameObject);
    }

    private static GameObject InstantiateGameplayPrefab(GameObject prefab, Transform parent)
    {
        if (!prefab) return null;
        var obj = PrefabUtility.InstantiatePrefab(prefab, parent) as GameObject;
        return obj;
    }

    private static Vector3 SnapToFloor(float x, float z, float rayDistance, out bool hit)
    {
        if (Physics.Raycast(new Vector3(x, 2f, z), Vector3.down, out RaycastHit rayHit, rayDistance))
        {
            hit = true;
            return rayHit.point + new Vector3(0f, 0.1f, 0f);
        }

        hit = false;
        return new Vector3(x, 0.1f, z);
    }

    private static bool IsTooClose(List<Vector3> placed, Vector3 candidate, float minDistance)
    {
        float sqr = minDistance * minDistance;
        foreach (var p in placed)
        {
            if ((p - candidate).sqrMagnitude < sqr)
                return true;
        }
        return false;
    }

    private static void ApplyThemeMaterialIfMissing(GameObject root, Material mat)
    {
        if (!root || !mat) return;
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        foreach (var renderer in renderers)
        {
            if (renderer.sharedMaterial == null)
            {
                renderer.sharedMaterial = mat;
            }
        }
    }

    /// <summary>
    /// Lightweight WFC pre-pass to repair impossible geometry before the main build.
    /// </summary>
    private static class WFCPreprocessor
    {
        public static Texture2D PreprocessWithWFC(Texture2D source)
        {
            if (!HasArchitecturalIssues(source))
                return source;

            int seed = UnityEngine.Random.Range(0, int.MaxValue);
            var solver = new WFCCore(source.width, source.height, seed);
            solver.ApplyConstraints(BuildConstraints(source));

            bool ok = solver.Collapse() && solver.EnsureConnectivity();
            if (!ok)
            {
                Debug.LogWarning("[WFC] Failed to repair blueprint; using original texture.");
                return source;
            }

            var colors = solver.ToBlueprintColors();
            var tex = new Texture2D(source.width, source.height, TextureFormat.RGB24, false, true)
            {
                name = source.name + "_wfc"
            };
            tex.SetPixels(colors);
            tex.filterMode = FilterMode.Point;
            tex.Apply();
            Debug.Log($"[WFC] Applied WFC correction (seed {seed}).");
            return tex;
        }

        private static Dictionary<Vector2Int, WFCTileType> BuildConstraints(Texture2D tex)
        {
            var constraints = new Dictionary<Vector2Int, WFCTileType>();

            for (int x = 0; x < tex.width; x++)
            {
                constraints[new Vector2Int(x, 0)] = WFCTileType.Wall;
                constraints[new Vector2Int(x, tex.height - 1)] = WFCTileType.Wall;
            }
            for (int y = 0; y < tex.height; y++)
            {
                constraints[new Vector2Int(0, y)] = WFCTileType.Wall;
                constraints[new Vector2Int(tex.width - 1, y)] = WFCTileType.Wall;
            }

            var px = tex.GetPixels32();
            for (int y = 0; y < tex.height; y++)
            {
                for (int x = 0; x < tex.width; x++)
                {
                    var kind = ClassifyPixel(px[y * tex.width + x]);
                    switch (kind)
                    {
                        case TileKind.SpawnNeutral:
                        case TileKind.SpawnRed:
                        case TileKind.SpawnGreen:
                            constraints[new Vector2Int(x, y)] = WFCTileType.Spawn;
                            break;
                        case TileKind.Water:
                            constraints[new Vector2Int(x, y)] = WFCTileType.Water;
                            break;
                    }
                }
            }

            return constraints;
        }

        private static bool HasArchitecturalIssues(Texture2D tex)
        {
            var px = tex.GetPixels32();
            int w = tex.width, h = tex.height;

            for (int y = 1; y < h - 1; y++)
            {
                for (int x = 1; x < w - 1; x++)
                {
                    var kind = ClassifyPixel(px[y * w + x]);
                    if (kind != TileKind.WallPixel)
                        continue;

                    int neighbors = 0;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            var nk = ClassifyPixel(px[(y + dy) * w + (x + dx)]);
                            if (nk == TileKind.WallPixel)
                                neighbors++;
                        }
                    }

                    if (neighbors < 2)
                        return true;
                }
            }

            var walkable = new HashSet<Vector2Int>();
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    var kind = ClassifyPixel(px[y * w + x]);
                    if (IsWalkable(kind))
                        walkable.Add(new Vector2Int(x, y));
                }
            }

            if (walkable.Count == 0)
                return true;

            var stack = new Stack<Vector2Int>();
            var seen = new HashSet<Vector2Int>();
            var first = walkable.First();
            stack.Push(first); seen.Add(first);
            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                foreach (var dir in new[] { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left })
                {
                    var next = cur + dir;
                    if (walkable.Contains(next) && seen.Add(next))
                        stack.Push(next);
                }
            }

            return seen.Count != walkable.Count;
        }

        private static bool IsWalkable(TileKind kind)
        {
            switch (kind)
            {
                case TileKind.Floor:
                case TileKind.FloorVariant:
                case TileKind.Platform_E1:
                case TileKind.Platform_Mid:
                case TileKind.Platform_Upper:
                case TileKind.SpawnNeutral:
                case TileKind.SpawnRed:
                case TileKind.SpawnGreen:
                case TileKind.Bridge:
                    return true;
                default:
                    return false;
            }
        }
    }

    private static void PairTeleporters(List<GameObject> teleporters)
    {
        if (teleporters == null) return;

        for (int i = 0; i + 1 < teleporters.Count; i += 2)
        {
            var a = teleporters[i];
            var b = teleporters[i + 1];
            if (!a || !b) continue;

            var linkA = a.GetComponent<TeleporterLink>() ?? a.AddComponent<TeleporterLink>();
            var linkB = b.GetComponent<TeleporterLink>() ?? b.AddComponent<TeleporterLink>();
            int groupId = (i / 2) + 1;
            if (linkA)
            {
                linkA.GroupId = groupId;
                linkA.Partner = b.transform;
            }
            if (linkB)
            {
                linkB.GroupId = groupId;
                linkB.Partner = a.transform;
            }

            UpdateTeleporterExit(a.transform, b.transform);
            UpdateTeleporterExit(b.transform, a.transform);
        }
    }

    private static void UpdateTeleporterExit(Transform tele, Transform partner)
    {
        if (!tele || !partner) return;
        var exit = tele.Find("Exit");
        if (exit)
        {
            exit.LookAt(partner.position);
        }
    }

    /// <summary>
    /// Attempts to build NavMesh using reflection (optional AI Navigation package).
    /// </summary>
    private static void TryBuildNavMesh(GameObject root)
    {
        if (!BUILD_NAVMESH)
        {
            Debug.Log("[NavMesh] Skipped (disabled).");
            return;
        }

        var type = System.Type.GetType("Unity.AI.Navigation.NavMeshSurface, Unity.AI.Navigation");
        if (type == null)
        {
            Debug.Log("[BuildFromBlueprint] NavMeshSurface not found (AI Navigation package not installed).");
            return;
        }

        var floor = GameObject.Find("Floor");
        var host = floor != null ? floor : root;
        var comp = host.GetComponent(type) ?? host.AddComponent(type);

        var mi = type.GetMethod("BuildNavMesh", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (mi != null)
        {
            mi.Invoke(comp, null);
            Debug.Log("[NavMesh] ✓ Built successfully.");
        }
        else
        {
            Debug.LogWarning("[BuildFromBlueprint] BuildNavMesh method not found on NavMeshSurface.");
        }
    }
}
#endif
