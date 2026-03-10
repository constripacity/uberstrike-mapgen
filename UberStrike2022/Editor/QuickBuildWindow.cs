#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering; // ✅ fixes ReflectionProbeMode enums
using System;
using System.IO;
using System.Linq;
using System.Reflection;

public static class QuickBuildWindow
{
    const string MAP_LAYOUTS = "Assets/_UberStrike/Blueprints/MapLayouts";
    const string OUT_DIR = "Assets/_UberStrike/Maps/Playable";
    const float DEFAULT_MPP = 0.05f; // meters per pixel — small value for large blueprint images
    const float DEFAULT_WALL_HEIGHT = 4.0f;
    const int DEFAULT_MAX_OBJECTS = 2000;

    const string PREF_KEY_MPP = "UGen.MPP";
    const string PREF_KEY_WALL_HEIGHT = "UGen.WallH";
    const string PREF_KEY_MAX_OBJECTS = "UGen.MaxObjs";
    const string PREF_KEY_NAVMESH = "UGen.NavMesh";
    const string PREF_KEY_VERSION = "UGen.Version";
    const int SETTINGS_VERSION = 2; // Bump this to force re-apply defaults

    /// <summary>
    /// One-time migration: if EditorPrefs has stale values from a previous version
    /// (e.g. MPP=1.0 from the old default), reset to current defaults.
    /// </summary>
    static void MigrateSettingsIfNeeded()
    {
        int ver = EditorPrefs.GetInt(PREF_KEY_VERSION, 0);
        if (ver < SETTINGS_VERSION)
        {
            Debug.Log($"[QuickBuild] Migrating settings from v{ver} to v{SETTINGS_VERSION} — resetting MPP to {DEFAULT_MPP}");
            EditorPrefs.SetFloat(PREF_KEY_MPP, DEFAULT_MPP);
            EditorPrefs.SetFloat(PREF_KEY_WALL_HEIGHT, DEFAULT_WALL_HEIGHT);
            EditorPrefs.SetInt(PREF_KEY_MAX_OBJECTS, DEFAULT_MAX_OBJECTS);
            EditorPrefs.SetBool(PREF_KEY_NAVMESH, true);
            EditorPrefs.SetInt(PREF_KEY_VERSION, SETTINGS_VERSION);
        }
    }

    [MenuItem("Tools/UnityAI/Quick Build Settings")]
    public static void OpenSettingsWindow()
    {
        MigrateSettingsIfNeeded();
        QuickBuildSettingsWindow.ShowWindow();
    }

    [MenuItem("Tools/UnityAI/Quick Build ► From Latest PNG & Save Scene")]
    public static void BuildFromLatestPngAndSave()
    {
        MigrateSettingsIfNeeded();

        try
        {
            // 1️⃣ Find the newest blueprint PNG
            Directory.CreateDirectory(MAP_LAYOUTS);
            var fullRoot = Path.GetFullPath(MAP_LAYOUTS);
            var di = new DirectoryInfo(fullRoot);
            var latest = di.GetFiles("*.png", SearchOption.TopDirectoryOnly)
                            .OrderByDescending(f => f.LastWriteTimeUtc)
                            .FirstOrDefault();
            if (latest == null)
            {
                EditorUtility.DisplayDialog("Quick Build", $"No PNGs found in:\n{MAP_LAYOUTS}", "OK");
                return;
            }

            string fullPng = latest.FullName;
            Debug.Log($"[QuickBuild] Using blueprint: {fullPng}");

            // 2️⃣ Use current active scene (preserve existing player/camera)
            var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            Debug.Log("[QuickBuild] Using current active scene; existing objects preserved.");

            // 3️⃣ Add lighting + reflection probe
            var lightGO = new GameObject("Directional Light");
            var dl = lightGO.AddComponent<Light>();
            dl.type = LightType.Directional;
            dl.intensity = 1.1f;
            lightGO.transform.rotation = Quaternion.Euler(50, -30, 0);

            var probeGO = new GameObject("Reflection Probe");
            var rp = probeGO.AddComponent<ReflectionProbe>();
            rp.mode = UnityEngine.Rendering.ReflectionProbeMode.Realtime;
            rp.refreshMode = UnityEngine.Rendering.ReflectionProbeRefreshMode.OnAwake;
            rp.timeSlicingMode = UnityEngine.Rendering.ReflectionProbeTimeSlicingMode.AllFacesAtOnce;
            rp.size = new Vector3(500, 200, 500);
            rp.boxProjection = true;
            rp.cullingMask = ~0;

            // 4️⃣ Try to call your builder if available
            var asm = typeof(QuickBuildWindow).Assembly;
            float metersPerPixel = EditorPrefs.GetFloat(PREF_KEY_MPP, DEFAULT_MPP);
            float wallHeight = EditorPrefs.GetFloat(PREF_KEY_WALL_HEIGHT, DEFAULT_WALL_HEIGHT);
            int maxObjects = EditorPrefs.GetInt(PREF_KEY_MAX_OBJECTS, DEFAULT_MAX_OBJECTS);
            bool buildNavMesh = EditorPrefs.GetBool(PREF_KEY_NAVMESH, true);

            Debug.Log($"[QuickBuild] Settings — MPP:{metersPerPixel:F2} WallHeight:{wallHeight:F2} MaxObjects:{maxObjects} NavMesh:{buildNavMesh}");

            var buildType = asm.GetTypes().FirstOrDefault(t => t.Name == "BuildFromBlueprint");
            if (buildType != null)
            {
                ApplyBuildSettings(buildType, wallHeight, maxObjects, buildNavMesh);

                var mi = buildType.GetMethod("BuildFromPNGPath",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (mi != null)
                {
                    mi.Invoke(null, new object[] { fullPng, metersPerPixel });
                    Debug.Log("[QuickBuild] BuildFromBlueprint.BuildFromPNGPath() invoked.");
                }
                else
                {
                    Debug.LogWarning("[QuickBuild] No BuildFromPNGPath found — using fallback floor.");
                    CreateFallback();
                }
            }
            else
            {
                Debug.LogWarning("[QuickBuild] No BuildFromBlueprint class found — using fallback floor.");
                CreateFallback();
            }

            // 5️⃣ Save the scene (ensure camera/player exist first)
            // Ensure a Main Camera exists
            if (GameObject.Find("Main Camera") == null)
            {
                var camGO = new GameObject("Main Camera");
                camGO.AddComponent<Camera>();
                camGO.tag = "MainCamera";
                camGO.transform.position = new Vector3(0f, 1.6f, -2f);
            }

            // --- ❗❗ THIS BLOCK WAS BROKEN ❗❗ ---
            // Ensure a player capsule with CharacterController + SimplePlayerController exists
            if (GameObject.FindObjectOfType<CharacterController>() == null)
            {
                // Try to find the SimplePlayerController script type
                var playerScriptType = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(asm => asm.GetTypes())
                    .FirstOrDefault(t => t.Name == "SimplePlayerController");

                if (playerScriptType != null)
                {
                    Debug.Log("[QuickBuild] Adding default Player_Capsule.");
                    var playerGO = new GameObject("Player_Capsule");
                    playerGO.transform.position = new Vector3(0, 1.5f, -5f);
                    playerGO.AddComponent<CharacterController>();
                    playerGO.AddComponent(playerScriptType);
                }
                else
                {
                    Debug.LogWarning("[QuickBuild] CharacterController not found, and 'SimplePlayerController' script type not found. Player not created.");
                }
            }
            // --- END OF FIX ---

            Directory.CreateDirectory(OUT_DIR);
            string mapName = Path.GetFileNameWithoutExtension(latest.Name);
            string outPath = $"{OUT_DIR}/Arena_{mapName}_{DateTime.Now:yyyyMMdd_HHmmss}.unity";

            var activeScene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            bool ok = UnityEditor.SceneManagement.EditorSceneManager.SaveScene(activeScene, outPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (!ok)
                throw new Exception("Failed to save scene to " + outPath);

            Debug.Log($"[QuickBuild] ✅ Saved playable scene: {outPath}");
            EditorUtility.DisplayDialog("Quick Build Complete", $"Saved:\n{outPath}", "Nice!");
        }
        catch (Exception ex)
        {
            Debug.LogError("[QuickBuild] ❌ " + ex);
            EditorUtility.DisplayDialog("Quick Build Failed", ex.Message, "OK");
        }
    }

    static void CreateFallback()
    {
        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "FallbackFloor";
        floor.transform.localScale = new Vector3(10, 1, 10);
        Debug.Log("[QuickBuild] Created fallback floor.");
    }

    static void ApplyBuildSettings(Type buildType, float wallHeight, int maxObjects, bool buildNavMesh)
    {
        TrySetStaticField(buildType, "WALL_HEIGHT", wallHeight);
        TrySetStaticField(buildType, "MAX_TOTAL_OBJECTS", maxObjects);
        TrySetStaticField(buildType, "BUILD_NAVMESH", buildNavMesh);
    }

    static void TrySetStaticField(Type targetType, string fieldName, object value)
    {
        try
        {
            var field = targetType.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (field != null && !field.IsLiteral)
            {
                field.SetValue(null, value);
                Debug.Log($"[QuickBuild] Applied {fieldName}={value} on {targetType.Name}.");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[QuickBuild] Failed to apply {fieldName}: {ex.Message}");
        }
    }

    class QuickBuildSettingsWindow : EditorWindow
    {
        float metersPerPixel;
        float wallHeight;
        int maxObjects;
        bool buildNavMesh;

        public static void ShowWindow()
        {
            var window = GetWindow<QuickBuildSettingsWindow>(true, "Quick Build Settings");
            window.minSize = new Vector2(320, 150);
            window.Show();
        }

        void OnEnable()
        {
            metersPerPixel = EditorPrefs.GetFloat(PREF_KEY_MPP, DEFAULT_MPP);
            wallHeight = EditorPrefs.GetFloat(PREF_KEY_WALL_HEIGHT, DEFAULT_WALL_HEIGHT);
            maxObjects = EditorPrefs.GetInt(PREF_KEY_MAX_OBJECTS, DEFAULT_MAX_OBJECTS);
            buildNavMesh = EditorPrefs.GetBool(PREF_KEY_NAVMESH, true);
        }

        void OnGUI()
        {
            GUILayout.Label("Quick Build Settings", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Values are stored in EditorPrefs and reused across sessions.", MessageType.Info);

            EditorGUI.BeginChangeCheck();

            metersPerPixel = EditorGUILayout.FloatField("Meters Per Pixel", metersPerPixel);
            wallHeight = EditorGUILayout.FloatField("Wall Height", wallHeight);
            maxObjects = EditorGUILayout.IntField("Max Total Objects", maxObjects);
            buildNavMesh = EditorGUILayout.Toggle("Build NavMesh", buildNavMesh);

            if (EditorGUI.EndChangeCheck())
            {
                metersPerPixel = Mathf.Max(0.01f, metersPerPixel);
                wallHeight = Mathf.Max(0.1f, wallHeight);
                maxObjects = Mathf.Max(1, maxObjects);

                EditorPrefs.SetFloat(PREF_KEY_MPP, metersPerPixel);
                EditorPrefs.SetFloat(PREF_KEY_WALL_HEIGHT, wallHeight);
                EditorPrefs.SetInt(PREF_KEY_MAX_OBJECTS, maxObjects);
                EditorPrefs.SetBool(PREF_KEY_NAVMESH, buildNavMesh);
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Reset to Defaults"))
            {
                metersPerPixel = DEFAULT_MPP;
                wallHeight = DEFAULT_WALL_HEIGHT;
                maxObjects = DEFAULT_MAX_OBJECTS;
                buildNavMesh = true;

                EditorPrefs.SetFloat(PREF_KEY_MPP, metersPerPixel);
                EditorPrefs.SetFloat(PREF_KEY_WALL_HEIGHT, wallHeight);
                EditorPrefs.SetInt(PREF_KEY_MAX_OBJECTS, maxObjects);
                EditorPrefs.SetBool(PREF_KEY_NAVMESH, buildNavMesh);
            }
        }
    }
}
#endif