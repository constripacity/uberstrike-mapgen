#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UberStrike.Realtime.Common;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Phase 2: One-click build from test blueprint → playable UberStrike map.
/// Combines QuickBuild + MapGenBridge into a single safe operation.
/// </summary>
public static class Phase2TestBuild
{
    const string BLUEPRINT_DIR = "Assets/_UberStrike/Blueprints/MapLayouts";
    const string SCENE_PATH = "Assets/Scenes/LevelGeneratedArena.unity";
    const int MAP_ID = 100;
    const float MPP = 0.05f; // meters per pixel

    [MenuItem("Tools/MapGen/Phase 2: Build From Blueprint (One-Click)")]
    public static void BuildFromBlueprint()
    {
        // 1. Find the test blueprint
        string blueprintPath = FindTestBlueprint();
        if (blueprintPath == null)
        {
            EditorUtility.DisplayDialog("Phase 2",
                "No blueprint PNG found in:\n" + BLUEPRINT_DIR +
                "\n\nExpected: test_small_200.png or arena_layout.png",
                "OK");
            return;
        }

        Debug.Log($"[Phase2] Using blueprint: {blueprintPath}");

        if (!EditorUtility.DisplayDialog("Phase 2: Build From Blueprint",
            $"Blueprint: {Path.GetFileName(blueprintPath)}\n" +
            $"MPP: {MPP} (meters per pixel)\n" +
            $"MapId: {MAP_ID}\n\n" +
            "This will generate geometry and save as:\n" +
            SCENE_PATH + "\n\n" +
            "The Phase 0 test arena will be replaced.",
            "Build", "Cancel"))
            return;

        string previousScene = SceneManager.GetActiveScene().path;

        try
        {
            // 2. Create a fresh empty scene to build into
            var buildScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            buildScene.name = "LevelGeneratedArena_Build";

            // 3. Call BuildFromBlueprint to generate geometry
            Debug.Log($"[Phase2] Calling BuildFromBlueprint.BuildFromPNGPath('{blueprintPath}', {MPP})");
            InvokeBuildFromBlueprint(blueprintPath, MPP);

            // 4. Log what was generated
            var rootObjects = buildScene.GetRootGameObjects();
            Debug.Log($"[Phase2] BuildFromBlueprint generated {rootObjects.Length} root objects:");
            foreach (var go in rootObjects)
            {
                int meshCount = go.GetComponentsInChildren<MeshRenderer>().Length;
                int childCount = go.GetComponentsInChildren<Transform>().Length;
                Debug.Log($"[Phase2]   '{go.name}': {childCount} transforms, {meshCount} renderers");
            }

            // 5. Find the main geometry root (largest object with renderers)
            GameObject geometryRoot = FindGeometryRoot(rootObjects);
            if (geometryRoot == null)
            {
                Debug.LogError("[Phase2] No geometry found after BuildFromBlueprint! Aborting.");
                EditorUtility.DisplayDialog("Phase 2 Failed",
                    "BuildFromBlueprint produced no geometry.\nCheck the Console for errors.",
                    "OK");
                ReturnToScene(previousScene);
                return;
            }

            Debug.Log($"[Phase2] Geometry root: '{geometryRoot.name}'");

            // 6. Create UberStrike map structure
            GameObject mapRoot = CreateUberStrikeMap(geometryRoot, buildScene);

            // 7. Log spawn and bounds info
            LogMapInfo(mapRoot);

            // 8. Save as clean scene
            SaveCleanScene(mapRoot, buildScene);

            Debug.Log($"[Phase2] SUCCESS! Scene saved to: {SCENE_PATH}");
            EditorUtility.DisplayDialog("Phase 2 Complete",
                $"Map built from blueprint!\n\n" +
                $"Scene: {SCENE_PATH}\n" +
                $"MapId: {MAP_ID}\n\n" +
                "Next: Press Play → Training → GeneratedArena\n" +
                "Press F12 for diagnostics overlay.",
                "OK");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Phase2] FAILED: {ex}");
            EditorUtility.DisplayDialog("Phase 2 Failed", ex.Message, "OK");
        }

        ReturnToScene(previousScene);
    }

    static string FindTestBlueprint()
    {
        if (!Directory.Exists(BLUEPRINT_DIR))
            return null;

        var dir = new DirectoryInfo(Path.GetFullPath(BLUEPRINT_DIR));
        var pngs = dir.GetFiles("*.png", SearchOption.TopDirectoryOnly)
                      .Where(f => !f.Name.StartsWith("__")) // Skip temp files
                      .OrderByDescending(f => f.LastWriteTimeUtc)
                      .ToList();

        if (pngs.Count == 0) return null;

        // Prefer test_small_200.png if it exists
        var testPng = pngs.FirstOrDefault(f => f.Name.Contains("test_small"));
        if (testPng != null)
        {
            return testPng.FullName;
        }

        // Fall back to newest PNG
        return pngs[0].FullName;
    }

    static void InvokeBuildFromBlueprint(string pngPath, float mpp)
    {
        // Use reflection to call BuildFromBlueprint (same pattern as QuickBuildWindow)
        var buildType = typeof(Phase2TestBuild).Assembly.GetTypes()
            .FirstOrDefault(t => t.Name == "BuildFromBlueprint");

        if (buildType == null)
        {
            throw new Exception("BuildFromBlueprint class not found! Is it compiled?");
        }

        // Set build parameters
        TrySetStaticField(buildType, "WALL_HEIGHT", 4.0f);
        TrySetStaticField(buildType, "MAX_TOTAL_OBJECTS", 2000);
        TrySetStaticField(buildType, "BUILD_NAVMESH", false); // Skip NavMesh for speed

        var method = buildType.GetMethod("BuildFromPNGPath",
            BindingFlags.Public | BindingFlags.Static);

        if (method == null)
        {
            throw new Exception("BuildFromPNGPath method not found on BuildFromBlueprint!");
        }

        method.Invoke(null, new object[] { pngPath, mpp });
    }

    static void TrySetStaticField(Type type, string fieldName, object value)
    {
        var field = type.GetField(fieldName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        if (field != null && !field.IsLiteral)
        {
            field.SetValue(null, value);
        }
    }

    static GameObject FindGeometryRoot(GameObject[] rootObjects)
    {
        GameObject best = null;
        int bestCount = 0;

        foreach (var go in rootObjects)
        {
            int rendererCount = go.GetComponentsInChildren<MeshRenderer>().Length;
            if (rendererCount > bestCount)
            {
                bestCount = rendererCount;
                best = go;
            }
        }

        return best;
    }

    /// <summary>
    /// Restructure BuildFromBlueprint output into proper UberStrike hierarchy:
    /// Root (MapConfiguration)
    ///   ├── StaticContent (all geometry)
    ///   ├── SpawnPoints (SpawnPoint components)
    ///   └── Level_Camera (Camera + DefaultViewPoint)
    /// </summary>
    static GameObject CreateUberStrikeMap(GameObject geometryRoot, Scene buildScene)
    {
        // Create the map root
        GameObject root = new GameObject("LevelGeneratedArena");

        // --- StaticContent ---
        GameObject staticContent = new GameObject("StaticContent");
        staticContent.transform.SetParent(root.transform);
        staticContent.transform.localPosition = Vector3.zero;

        // Move all geometry from the build output into StaticContent
        // The geometry root might have children or might BE the geometry
        if (geometryRoot.GetComponent<MeshRenderer>() != null)
        {
            // The root itself has a renderer — wrap it
            geometryRoot.transform.SetParent(staticContent.transform);
        }
        else
        {
            // Move all children
            var children = new List<Transform>();
            for (int i = geometryRoot.transform.childCount - 1; i >= 0; i--)
            {
                children.Add(geometryRoot.transform.GetChild(i));
            }
            foreach (var child in children)
            {
                child.SetParent(staticContent.transform);
            }
            // Destroy the empty original root
            UnityEngine.Object.DestroyImmediate(geometryRoot);
        }

        // Also grab any orphaned root objects from the build (lights, probes, etc.)
        foreach (var go in buildScene.GetRootGameObjects())
        {
            if (go == root) continue;
            if (go.GetComponent<Light>() != null || go.GetComponent<MeshRenderer>() != null ||
                go.GetComponentsInChildren<MeshRenderer>().Length > 0)
            {
                go.transform.SetParent(staticContent.transform);
            }
        }

        // Mark static for lightmapping
        SetStaticRecursively(staticContent);

        // Add MeshColliders to combined geometry
        // BuildFromBlueprint's mesh combining strips colliders from the originals,
        // leaving only MeshRenderer + MeshFilter. Without colliders the player falls through.
        int collidersAdded = 0;
        foreach (var mf in staticContent.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh != null && mf.GetComponent<Collider>() == null)
            {
                mf.gameObject.AddComponent<MeshCollider>();
                collidersAdded++;
            }
        }
        Debug.Log($"[Phase2] Added MeshColliders to {collidersAdded} objects");

        // Calculate map bounds for spawn/camera placement
        Bounds mapBounds = CalculateBounds(staticContent);
        Debug.Log($"[Phase2] Map bounds: center={mapBounds.center}, size={mapBounds.size}");

        // --- SpawnPoints ---
        // First, try to find spawn markers from BuildFromBlueprint (green cubes, Spawn_* objects)
        GameObject spawnContainer = new GameObject("SpawnPoints");
        spawnContainer.transform.SetParent(root.transform);
        spawnContainer.transform.localPosition = Vector3.zero;

        var spawnMarkers = FindSpawnMarkers(staticContent);
        Debug.Log($"[Phase2] Found {spawnMarkers.Count} spawn markers from blueprint");

        if (spawnMarkers.Count >= 4)
        {
            // Use blueprint spawn markers
            CreateSpawnPointsFromMarkers(spawnContainer, spawnMarkers, staticContent);
        }
        else
        {
            // Fallback: generate circular spawns within map bounds
            Debug.Log("[Phase2] Not enough spawn markers, generating circular pattern");
            GenerateCircularSpawns(spawnContainer, mapBounds, 8);
        }

        // --- Camera ---
        GameObject camGO = new GameObject("Level_Camera");
        camGO.transform.SetParent(root.transform);

        float elevation = Mathf.Max(mapBounds.extents.x, mapBounds.extents.z) * 0.8f + 10f;
        camGO.transform.position = mapBounds.center + new Vector3(0, elevation, -elevation * 0.5f);
        camGO.transform.LookAt(mapBounds.center);

        Camera cam = camGO.AddComponent<Camera>();
        cam.fieldOfView = 60f;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 500f;
        cam.enabled = false;

        GameObject viewPoint = new GameObject("DefaultViewPoint");
        viewPoint.transform.SetParent(camGO.transform);
        viewPoint.transform.localPosition = Vector3.zero;
        viewPoint.transform.localRotation = Quaternion.identity;

        // --- Directional Light (if not already in StaticContent) ---
        EnsureDirectionalLight(staticContent);

        // --- MapConfiguration ---
        MapConfiguration config = root.AddComponent<MapConfiguration>();
        var so = new SerializedObject(config);

        so.FindProperty("_isEnabled").boolValue = true;
        so.FindProperty("_mapId").intValue = MAP_ID;
        so.FindProperty("_defaultSpawnPoint").intValue = 0;
        so.FindProperty("_defaultFootStep").enumValueIndex = (int)FootStepSoundType.Metal;
        so.FindProperty("_camera").objectReferenceValue = cam;
        so.FindProperty("_defaultViewPoint").objectReferenceValue = viewPoint.transform;
        so.FindProperty("_staticContentParent").objectReferenceValue = staticContent;
        so.FindProperty("_spawnPoints").objectReferenceValue = spawnContainer;

        // Detect water plane
        Transform waterPlane = FindWaterPlane(staticContent);
        if (waterPlane != null)
        {
            so.FindProperty("_waterPlane").objectReferenceValue = waterPlane;
        }

        so.ApplyModifiedProperties();

        // Clean up any remaining orphan objects from the build
        foreach (var go in buildScene.GetRootGameObjects())
        {
            if (go != root)
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        return root;
    }

    static List<Transform> FindSpawnMarkers(GameObject staticContent)
    {
        var markers = new List<Transform>();

        foreach (var renderer in staticContent.GetComponentsInChildren<MeshRenderer>())
        {
            // Check for PlayerSpawnPoint component (MapGen native)
            var mb = renderer.GetComponent<MonoBehaviour>();
            if (mb != null && mb.GetType().Name == "PlayerSpawnPoint")
            {
                markers.Add(renderer.transform);
                continue;
            }

            // Check for green-colored objects (spawn markers)
            if (renderer.sharedMaterial != null && renderer.sharedMaterial.HasProperty("_Color"))
            {
                Color c = renderer.sharedMaterial.color;
                if (c.g > 0.8f && c.r < 0.3f && c.b < 0.3f)
                {
                    markers.Add(renderer.transform);
                }
            }

            // Check for Spawn_* naming
            if (renderer.gameObject.name.StartsWith("Spawn_") ||
                renderer.gameObject.name.StartsWith("SpawnPoint_"))
            {
                if (!markers.Contains(renderer.transform))
                    markers.Add(renderer.transform);
            }
        }

        return markers;
    }

    static void CreateSpawnPointsFromMarkers(GameObject container, List<Transform> markers,
        GameObject staticContent)
    {
        // Find floor material for recoloring green blocks
        Material floorMat = null;
        foreach (var r in staticContent.GetComponentsInChildren<MeshRenderer>())
        {
            if (r.sharedMaterial != null && r.sharedMaterial.HasProperty("_Color"))
            {
                Color c = r.sharedMaterial.color;
                if (c.r > 0.3f && c.r < 0.9f && Mathf.Abs(c.r - c.g) < 0.1f && Mathf.Abs(c.r - c.b) < 0.1f)
                {
                    floorMat = r.sharedMaterial;
                    break;
                }
            }
        }

        for (int i = 0; i < markers.Count; i++)
        {
            var marker = markers[i];
            var markerRenderer = marker.GetComponent<MeshRenderer>();

            // Get top of marker block
            float topY = 0f;
            if (markerRenderer != null)
            {
                topY = markerRenderer.bounds.max.y;
                // Recolor green block to match floor
                if (floorMat != null)
                    markerRenderer.sharedMaterial = floorMat;
            }
            else
            {
                topY = marker.position.y + 2f;
            }

            GameObject spawnGO = new GameObject($"SpawnPoint_{i:D2}");
            spawnGO.transform.SetParent(container.transform);
            // Place at top of block (game adds Vector3.up for player)
            spawnGO.transform.position = new Vector3(marker.position.x, topY, marker.position.z);
            spawnGO.transform.rotation = Quaternion.Euler(0, UnityEngine.Random.Range(0f, 360f), 0);

            SpawnPoint sp = spawnGO.AddComponent<SpawnPoint>();

            // First half: DeathMatch (used by Training mode)
            // Second half: alternate RED/BLUE for TDM
            if (i < markers.Count / 2 || i < 4)
            {
                sp.GameMode = GameMode.DeathMatch;
                sp.TeamPoint = UberStrike.Realtime.Common.TeamID.NONE;
            }
            else if (i % 2 == 0)
            {
                sp.GameMode = GameMode.TeamDeathMatch;
                sp.TeamPoint = UberStrike.Realtime.Common.TeamID.RED;
            }
            else
            {
                sp.GameMode = GameMode.TeamDeathMatch;
                sp.TeamPoint = UberStrike.Realtime.Common.TeamID.BLUE;
            }

            Debug.Log($"[Phase2] Spawn_{i:D2} at ({marker.position.x:F1}, {topY:F1}, {marker.position.z:F1}) " +
                      $"mode={sp.GameMode} team={sp.TeamPoint}");
        }
    }

    static void GenerateCircularSpawns(GameObject container, Bounds mapBounds, int count)
    {
        float radius = Mathf.Min(mapBounds.extents.x, mapBounds.extents.z) * 0.6f;
        radius = Mathf.Max(radius, 2f); // At least 2m radius

        for (int i = 0; i < count; i++)
        {
            float angle = (float)i / count * Mathf.PI * 2f;
            float x = mapBounds.center.x + Mathf.Cos(angle) * radius;
            float z = mapBounds.center.z + Mathf.Sin(angle) * radius;
            // Place at floor level (Y=0). The game adds Vector3.up when spawning the player.
            // Don't use raycast — combined meshes may not have colliders, and raycasts
            // can hit wall tops (Y=4) instead of floors (Y=0).
            float y = mapBounds.min.y;

            GameObject spawnGO = new GameObject($"SpawnPoint_{i:D2}");
            spawnGO.transform.SetParent(container.transform);
            spawnGO.transform.position = new Vector3(x, y, z);
            spawnGO.transform.rotation = Quaternion.Euler(0f, Mathf.Atan2(-x + mapBounds.center.x,
                -z + mapBounds.center.z) * Mathf.Rad2Deg, 0f);

            SpawnPoint sp = spawnGO.AddComponent<SpawnPoint>();
            sp.GameMode = GameMode.DeathMatch;
            sp.TeamPoint = UberStrike.Realtime.Common.TeamID.NONE;

            Debug.Log($"[Phase2] Auto-Spawn_{i:D2} at ({x:F1}, {y:F1}, {z:F1})");
        }
    }

    static void SaveCleanScene(GameObject mapRoot, Scene buildScene)
    {
        // Ensure the Scenes directory exists
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(SCENE_PATH)));

        // The build scene was created as EmptyScene/Single, so it already contains
        // only our map objects. Just save it directly — no need for additive scene.
        EditorSceneManager.SaveScene(buildScene, SCENE_PATH);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[Phase2] Scene saved: {SCENE_PATH}");

        // Add to build settings
        var scenes = EditorBuildSettings.scenes.ToList();
        if (!scenes.Any(s => s.path == SCENE_PATH))
        {
            scenes.Add(new EditorBuildSettingsScene(SCENE_PATH, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log($"[Phase2] Added {SCENE_PATH} to Build Settings");
        }
    }

    static void ReturnToScene(string scenePath)
    {
        if (!string.IsNullOrEmpty(scenePath) && File.Exists(scenePath))
        {
            EditorSceneManager.OpenScene(scenePath);
        }
    }

    static void EnsureDirectionalLight(GameObject staticContent)
    {
        var lights = staticContent.GetComponentsInChildren<Light>();
        bool hasDirectional = lights.Any(l => l.type == LightType.Directional);

        if (!hasDirectional)
        {
            GameObject lightGO = new GameObject("Directional Light");
            lightGO.transform.SetParent(staticContent.transform);
            lightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            Light light = lightGO.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.color = new Color(1f, 0.96f, 0.89f);
            light.shadows = LightShadows.Soft;
        }
    }

    static Transform FindWaterPlane(GameObject staticContent)
    {
        foreach (var renderer in staticContent.GetComponentsInChildren<MeshRenderer>())
        {
            if (renderer.gameObject.name.Contains("Water") || renderer.gameObject.name.Contains("water"))
                return renderer.transform;

            if (renderer.sharedMaterial != null && renderer.sharedMaterial.HasProperty("_Color"))
            {
                Color c = renderer.sharedMaterial.color;
                if (c.b > 0.8f && c.r < 0.3f && c.g < 0.3f)
                {
                    renderer.gameObject.name = "WaterPlane";
                    return renderer.transform;
                }
            }
        }
        return null;
    }

    static Bounds CalculateBounds(GameObject root)
    {
        var renderers = root.GetComponentsInChildren<MeshRenderer>();
        if (renderers.Length == 0)
            return new Bounds(Vector3.zero, Vector3.one * 10f);

        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            b.Encapsulate(renderers[i].bounds);
        return b;
    }

    static void SetStaticRecursively(GameObject go)
    {
        go.isStatic = true;
        foreach (Transform child in go.transform)
            SetStaticRecursively(child.gameObject);
    }

    static void LogMapInfo(GameObject mapRoot)
    {
        var staticContent = mapRoot.transform.Find("StaticContent");
        var spawnContainer = mapRoot.transform.Find("SpawnPoints");

        if (staticContent != null)
        {
            var bounds = CalculateBounds(staticContent.gameObject);
            int rendererCount = staticContent.GetComponentsInChildren<MeshRenderer>().Length;
            Debug.Log($"[Phase2] StaticContent: {rendererCount} renderers, " +
                      $"bounds center={bounds.center}, size={bounds.size}");
        }

        if (spawnContainer != null)
        {
            var spawns = spawnContainer.GetComponentsInChildren<SpawnPoint>();
            int dm = spawns.Count(s => s.GameMode == GameMode.DeathMatch);
            int tdm = spawns.Count(s => s.GameMode == GameMode.TeamDeathMatch);
            Debug.Log($"[Phase2] SpawnPoints: {spawns.Length} total ({dm} DM, {tdm} TDM)");

            foreach (var sp in spawns)
            {
                Debug.Log($"[Phase2]   {sp.name}: pos={sp.transform.position} mode={sp.GameMode} team={sp.TeamPoint}");
            }
        }
    }
}
#endif
