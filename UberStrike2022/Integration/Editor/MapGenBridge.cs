using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Converts a MapGen-generated scene into a fully UberStrike-compatible map scene.
/// Adds MapConfiguration, SpawnPoint components, camera, and proper hierarchy.
/// </summary>
public class MapGenBridge : EditorWindow
{
    private const int BASE_MAP_ID = 100;
    private const string SCENES_FOLDER = "Assets/Scenes/";
    private const string GENERATED_FOLDER = "Assets/MapGen/Generated/";

    private string _mapName = "GeneratedArena";
    private int _mapId = BASE_MAP_ID;
    private FootStepSoundType _footStepType = FootStepSoundType.Metal;
    private bool _autoDetectSpawns = true;
    private int _minDMSpawns = 8;
    private int _minTeamSpawns = 4;

    [MenuItem("Tools/MapGen/Convert to UberStrike Map")]
    static void ShowWindow()
    {
        var window = GetWindow<MapGenBridge>("MapGen Bridge");
        window.minSize = new Vector2(400, 350);
        window.Show();
    }

    void OnGUI()
    {
        GUILayout.Label("Convert MapGen Scene to UberStrike Map", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        _mapName = EditorGUILayout.TextField("Map Name", _mapName);
        _mapId = EditorGUILayout.IntField("Map ID (100+)", _mapId);
        _footStepType = (FootStepSoundType)EditorGUILayout.EnumPopup("Default Footstep", _footStepType);
        _autoDetectSpawns = EditorGUILayout.Toggle("Auto-detect Spawns", _autoDetectSpawns);

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "This will restructure the current scene to match UberStrike's map format:\n" +
            "- Add MapConfiguration component\n" +
            "- Convert spawn markers to SpawnPoint components\n" +
            "- Create camera and viewpoint\n" +
            "- Set up static content parent\n" +
            "- Save as Assets/Scenes/Level{MapName}.unity",
            MessageType.Info);

        EditorGUILayout.Space();

        if (_mapId < BASE_MAP_ID)
        {
            EditorGUILayout.HelpBox("Map ID must be >= 100 (reserved range for generated maps)", MessageType.Warning);
        }

        EditorGUI.BeginDisabledGroup(_mapId < BASE_MAP_ID);
        if (GUILayout.Button("Convert Current Scene", GUILayout.Height(35)))
        {
            ConvertCurrentScene();
        }
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.Space();
        GUILayout.Label("Batch Operations", EditorStyles.boldLabel);
        if (GUILayout.Button("Scan for Generated Scenes"))
        {
            ScanGeneratedScenes();
        }
    }

    /// <summary>
    /// Main conversion: takes the current open scene and restructures it for UberStrike.
    /// Creates a CLEAN scene with only map objects (avoids duplicating game manager singletons).
    /// </summary>
    void ConvertCurrentScene()
    {
        var sourceScene = SceneManager.GetActiveScene();
        Undo.SetCurrentGroupName("MapGen Bridge Conversion");

        string levelName = "Level" + _mapName.Replace(" ", "");
        Debug.Log($"[MapGenBridge] Converting scene to UberStrike map: {levelName} (MapId={_mapId})");

        // Step 1: Find or create the root GameObject (in the current scene)
        GameObject root = FindOrCreateRoot(levelName);

        // Step 2: Create StaticContent parent and reparent geometry
        GameObject staticContent = SetupStaticContent(root);

        // Step 3: Create SpawnPoints container with SpawnPoint components
        GameObject spawnContainer = SetupSpawnPoints(root, staticContent);

        // Step 4: Create camera and viewpoint
        Camera cam = SetupCamera(root, staticContent);

        // Step 5: Detect water plane
        Transform waterPlane = FindWaterPlane(staticContent);

        // Step 6: Add MapConfiguration and wire everything up
        MapConfiguration mapConfig = SetupMapConfiguration(
            root, _mapId, staticContent, spawnContainer, cam, waterPlane);

        // Step 7: Ensure directional light exists
        EnsureDirectionalLight(staticContent);

        // Step 8: Create a CLEAN scene with only the map root object.
        // The source scene (Latest) contains game manager singletons (SfxManager,
        // UnityItemConfiguration, LevelEnviroment, etc.) that must NOT be saved
        // into the map scene, or they'll duplicate when loaded additively.
        string scenePath = SCENES_FOLDER + levelName + ".unity";
        var cleanScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        cleanScene.name = levelName;

        // Move the map root (and all its children) into the clean scene
        SceneManager.MoveGameObjectToScene(root, cleanScene);

        // Save only the clean scene
        EditorSceneManager.SaveScene(cleanScene, scenePath);

        // Move root back to the source scene so the user can keep working
        SceneManager.MoveGameObjectToScene(root, sourceScene);

        // Close the temporary clean scene
        EditorSceneManager.CloseScene(cleanScene, true);

        // Step 9: Add to build settings
        AddSceneToBuildSettings(scenePath);

        Debug.Log($"[MapGenBridge] Conversion complete! Scene saved to: {scenePath}");
        EditorUtility.DisplayDialog("MapGen Bridge",
            $"Map converted successfully!\n\n" +
            $"Scene: {scenePath}\n" +
            $"MapId: {_mapId}\n" +
            $"Spawns: {spawnContainer.transform.childCount}\n\n" +
            $"The map scene contains ONLY map objects (no game managers).\n" +
            $"Next: Press Play to test — select GeneratedArena from the map list.",
            "OK");
    }

    /// <summary>
    /// Find existing root or create one. Renames to Level{Name}.
    /// </summary>
    GameObject FindOrCreateRoot(string levelName)
    {
        // Look for existing MapGen root objects (Arena_*, Generated_*, etc.)
        var rootObjects = SceneManager.GetActiveScene().GetRootGameObjects();
        GameObject root = null;

        // Prefer the largest root object (most children = likely the map)
        int maxChildren = 0;
        foreach (var go in rootObjects)
        {
            if (go.name.StartsWith("Arena_") || go.name.StartsWith("Generated_") ||
                go.name.Contains("Map") || go.GetComponentsInChildren<MeshRenderer>().Length > 0)
            {
                int count = go.GetComponentsInChildren<Transform>().Length;
                if (count > maxChildren)
                {
                    maxChildren = count;
                    root = go;
                }
            }
        }

        if (root == null)
        {
            root = new GameObject(levelName);
            Debug.Log("[MapGenBridge] Created new root GameObject");
        }
        else
        {
            Undo.RecordObject(root, "Rename root");
            root.name = levelName;
            Debug.Log($"[MapGenBridge] Using existing root: {root.name} ({maxChildren} transforms)");
        }

        return root;
    }

    /// <summary>
    /// Create StaticContent parent and reparent all geometry under it.
    /// </summary>
    GameObject SetupStaticContent(GameObject root)
    {
        // Check if StaticContent already exists
        Transform existing = root.transform.Find("StaticContent");
        if (existing != null)
        {
            Debug.Log("[MapGenBridge] StaticContent already exists, using it");
            return existing.gameObject;
        }

        GameObject staticContent = new GameObject("StaticContent");
        Undo.RegisterCreatedObjectUndo(staticContent, "Create StaticContent");
        staticContent.transform.SetParent(root.transform);
        staticContent.transform.localPosition = Vector3.zero;

        // Reparent all geometry children (skip SpawnPoints, Camera, etc. we'll create)
        var children = new List<Transform>();
        for (int i = root.transform.childCount - 1; i >= 0; i--)
        {
            var child = root.transform.GetChild(i);
            if (child.gameObject != staticContent &&
                !child.name.Contains("SpawnPoint") &&
                !child.name.Contains("Spawn_") &&
                !child.name.Contains("Camera") &&
                !child.name.Contains("ViewPoint"))
            {
                children.Add(child);
            }
        }

        foreach (var child in children)
        {
            Undo.SetTransformParent(child, staticContent.transform, "Reparent to StaticContent");
        }

        // Mark all as static for lightmapping
        SetStaticRecursively(staticContent);

        Debug.Log($"[MapGenBridge] Reparented {children.Count} objects under StaticContent");
        return staticContent;
    }

    /// <summary>
    /// Find spawn markers from MapGen and convert to UberStrike SpawnPoint components.
    /// MapGen uses green-colored GameObjects or objects named "Spawn_*" / "PlayerSpawnPoint".
    /// </summary>
    GameObject SetupSpawnPoints(GameObject root, GameObject staticContent)
    {
        // Check if SpawnPoints container already exists
        Transform existing = root.transform.Find("SpawnPoints");
        if (existing != null && existing.GetComponentsInChildren<SpawnPoint>().Length > 0)
        {
            Debug.Log("[MapGenBridge] SpawnPoints already configured, using existing");
            return existing.gameObject;
        }

        GameObject spawnContainer = existing != null ? existing.gameObject : new GameObject("SpawnPoints");
        if (existing == null)
        {
            Undo.RegisterCreatedObjectUndo(spawnContainer, "Create SpawnPoints");
            spawnContainer.transform.SetParent(root.transform);
            spawnContainer.transform.localPosition = Vector3.zero;
        }

        // Find all spawn markers in the scene
        var spawnMarkers = new List<Transform>();

        if (_autoDetectSpawns)
        {
            // Method 1: Find UnityAI.PlayerSpawnPoint components (MapGen native)
            var nativeSpawns = Object.FindObjectsOfType<MonoBehaviour>()
                .Where(mb => mb.GetType().Name == "PlayerSpawnPoint")
                .Select(mb => mb.transform)
                .ToList();
            spawnMarkers.AddRange(nativeSpawns);

            // Method 2: Find objects named Spawn_* or SpawnPoint_*
            foreach (var go in Object.FindObjectsOfType<Transform>())
            {
                if ((go.name.StartsWith("Spawn_") || go.name.StartsWith("SpawnPoint_")) &&
                    !spawnMarkers.Contains(go))
                {
                    spawnMarkers.Add(go);
                }
            }

            // Method 3: Find green-colored cubes (MapGen spawn markers)
            foreach (var renderer in staticContent.GetComponentsInChildren<MeshRenderer>())
            {
                if (renderer.sharedMaterial != null && renderer.sharedMaterial.HasProperty("_Color"))
                {
                    Color c = renderer.sharedMaterial.color;
                    // Green = spawn in MapGen legend (RGB ~0,255,0)
                    if (c.g > 0.8f && c.r < 0.3f && c.b < 0.3f)
                    {
                        spawnMarkers.Add(renderer.transform);
                    }
                }
            }
        }

        Debug.Log($"[MapGenBridge] Found {spawnMarkers.Count} spawn markers");

        // If no spawns found, generate default positions on the floor
        if (spawnMarkers.Count < _minDMSpawns)
        {
            Debug.LogWarning($"[MapGenBridge] Only {spawnMarkers.Count} spawns found, generating {_minDMSpawns} defaults");
            GenerateDefaultSpawns(spawnContainer, staticContent, _minDMSpawns - spawnMarkers.Count);
        }

        // Convert markers to SpawnPoint components
        int dmCount = 0;
        int redCount = 0;
        int blueCount = 0;

        // Find the default floor material (gray) to recolor green spawn blocks
        Material floorMaterial = null;
        foreach (var renderer in staticContent.GetComponentsInChildren<MeshRenderer>())
        {
            if (renderer.sharedMaterial != null && renderer.sharedMaterial.HasProperty("_Color"))
            {
                Color c = renderer.sharedMaterial.color;
                // Look for gray material (floor/wall blocks)
                if (c.r > 0.3f && c.r < 0.9f && Mathf.Abs(c.r - c.g) < 0.1f && Mathf.Abs(c.r - c.b) < 0.1f)
                {
                    floorMaterial = renderer.sharedMaterial;
                    break;
                }
            }
        }

        for (int i = 0; i < spawnMarkers.Count; i++)
        {
            var marker = spawnMarkers[i];
            GameObject spawnGO = new GameObject($"SpawnPoint_{i:D2}");
            Undo.RegisterCreatedObjectUndo(spawnGO, "Create SpawnPoint");
            spawnGO.transform.SetParent(spawnContainer.transform);

            // Place spawn on TOP of the marker block.
            // The green block IS the floor at this position — do NOT destroy it.
            // Use the block's bounding box top + player clearance.
            var markerRenderer = marker.GetComponent<MeshRenderer>();
            float topY;
            if (markerRenderer != null)
            {
                topY = markerRenderer.bounds.max.y;

                // Recolor the green block to match surrounding floor material
                if (floorMaterial != null)
                {
                    markerRenderer.sharedMaterial = floorMaterial;
                }
            }
            else
            {
                topY = marker.position.y + 2f; // fallback
            }

            // Place spawn 1.0m above the block top (player eye height ~1.6m, half capsule ~0.8m)
            spawnGO.transform.position = new Vector3(marker.position.x, topY + 1.0f, marker.position.z);
            spawnGO.transform.rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
            Debug.Log($"[MapGenBridge] SpawnPoint_{i:D2} at {spawnGO.transform.position} (block top={topY})");

            SpawnPoint sp = spawnGO.AddComponent<SpawnPoint>();

            // Distribute: first set as DM/NONE, then alternate RED/BLUE for TDM
            if (i < spawnMarkers.Count / 2)
            {
                // DeathMatch spawns (also used for Training mode)
                sp.GameMode = GameMode.DeathMatch;
                sp.TeamPoint = UberStrike.Realtime.Common.TeamID.NONE;
                dmCount++;
            }
            else if (i % 2 == 0)
            {
                // Team Red
                sp.GameMode = GameMode.TeamDeathMatch;
                sp.TeamPoint = UberStrike.Realtime.Common.TeamID.RED;
                redCount++;
            }
            else
            {
                // Team Blue
                sp.GameMode = GameMode.TeamDeathMatch;
                sp.TeamPoint = UberStrike.Realtime.Common.TeamID.BLUE;
                blueCount++;
            }

            // DO NOT destroy the green block — it IS the floor at the spawn position.
            // We recolored it above so it blends in with surrounding geometry.
        }

        Debug.Log($"[MapGenBridge] Created {dmCount} DM + {redCount} RED + {blueCount} BLUE spawn points");
        return spawnContainer;
    }

    /// <summary>
    /// Generate default spawn positions spread across the map floor.
    /// </summary>
    void GenerateDefaultSpawns(GameObject container, GameObject staticContent, int count)
    {
        Bounds mapBounds = CalculateMapBounds(staticContent);
        float padding = 3f;

        for (int i = 0; i < count; i++)
        {
            float t = (float)i / count;
            float angle = t * Mathf.PI * 2f;
            float radius = Mathf.Min(mapBounds.extents.x, mapBounds.extents.z) * 0.6f;

            Vector3 pos = mapBounds.center + new Vector3(
                Mathf.Cos(angle) * radius,
                0f,
                Mathf.Sin(angle) * radius
            );

            // Raycast down to find floor
            if (Physics.Raycast(pos + Vector3.up * 50f, Vector3.down, out RaycastHit hit, 100f))
            {
                pos = hit.point + Vector3.up * 0.5f;
            }
            else
            {
                pos.y = mapBounds.min.y + 1f;
            }

            GameObject spawnGO = new GameObject($"SpawnPoint_Auto_{i:D2}");
            Undo.RegisterCreatedObjectUndo(spawnGO, "Create auto spawn");
            spawnGO.transform.SetParent(container.transform);
            spawnGO.transform.position = pos;
            spawnGO.transform.rotation = Quaternion.Euler(0, angle * Mathf.Rad2Deg + 180f, 0);

            SpawnPoint sp = spawnGO.AddComponent<SpawnPoint>();
            sp.GameMode = GameMode.DeathMatch;
            sp.TeamPoint = UberStrike.Realtime.Common.TeamID.NONE;
        }

        Debug.Log($"[MapGenBridge] Generated {count} auto spawn points in circular pattern");
    }

    /// <summary>
    /// Create the map camera and default viewpoint.
    /// </summary>
    Camera SetupCamera(GameObject root, GameObject staticContent)
    {
        // Check for existing camera
        Transform camTransform = root.transform.Find("Level_Camera");
        if (camTransform != null)
        {
            Camera existingCam = camTransform.GetComponent<Camera>();
            if (existingCam != null)
            {
                Debug.Log("[MapGenBridge] Using existing camera");
                return existingCam;
            }
        }

        Bounds bounds = CalculateMapBounds(staticContent);

        // Create camera positioned to overview the map
        GameObject camGO = new GameObject("Level_Camera");
        Undo.RegisterCreatedObjectUndo(camGO, "Create camera");
        camGO.transform.SetParent(root.transform);

        // Position at map center, elevated, looking at center
        float elevation = Mathf.Max(bounds.extents.x, bounds.extents.z) * 0.8f + 10f;
        camGO.transform.position = bounds.center + new Vector3(0, elevation, -elevation * 0.5f);
        camGO.transform.LookAt(bounds.center);

        Camera cam = camGO.AddComponent<Camera>();
        cam.fieldOfView = 60f;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 1000f;
        cam.enabled = false; // UberStrike enables via SetEnabled()

        // Create default viewpoint as child
        GameObject viewPoint = new GameObject("DefaultViewPoint");
        Undo.RegisterCreatedObjectUndo(viewPoint, "Create viewpoint");
        viewPoint.transform.SetParent(camGO.transform);
        viewPoint.transform.localPosition = Vector3.zero;
        viewPoint.transform.localRotation = Quaternion.identity;

        Debug.Log($"[MapGenBridge] Created camera at {camGO.transform.position}");
        return cam;
    }

    /// <summary>
    /// Find water plane in the generated geometry.
    /// MapGen creates water as blue-colored planes.
    /// </summary>
    Transform FindWaterPlane(GameObject staticContent)
    {
        foreach (var renderer in staticContent.GetComponentsInChildren<MeshRenderer>())
        {
            if (renderer.gameObject.name.Contains("Water") ||
                renderer.gameObject.name.Contains("water"))
            {
                Debug.Log($"[MapGenBridge] Found water plane: {renderer.gameObject.name}");
                return renderer.transform;
            }

            // Check for blue material (MapGen water color)
            // Skip materials without _Color property (particle shaders, HeatDistort, etc.)
            if (renderer.sharedMaterial != null && renderer.sharedMaterial.HasProperty("_Color"))
            {
                Color c = renderer.sharedMaterial.color;
                if (c.b > 0.8f && c.r < 0.3f && c.g < 0.3f)
                {
                    renderer.gameObject.name = "WaterPlane";
                    Debug.Log($"[MapGenBridge] Detected water plane from blue material");
                    return renderer.transform;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Add and configure MapConfiguration component with all required references.
    /// Uses reflection to set private serialized fields.
    /// </summary>
    MapConfiguration SetupMapConfiguration(GameObject root, int mapId,
        GameObject staticContent, GameObject spawnPoints, Camera camera, Transform waterPlane)
    {
        MapConfiguration config = root.GetComponent<MapConfiguration>();
        if (config == null)
        {
            config = root.AddComponent<MapConfiguration>();
        }
        Undo.RecordObject(config, "Configure MapConfiguration");

        // Use SerializedObject to set private [SerializeField] fields
        var so = new SerializedObject(config);
        so.FindProperty("_mapId").intValue = mapId;
        so.FindProperty("_defaultSpawnPoint").intValue = 0;
        so.FindProperty("_defaultFootStep").enumValueIndex = (int)_footStepType;
        so.FindProperty("_staticContentParent").objectReferenceValue = staticContent;
        so.FindProperty("_spawnPoints").objectReferenceValue = spawnPoints;
        so.FindProperty("_camera").objectReferenceValue = camera;

        // DefaultViewPoint - use first child of camera
        Transform viewPoint = camera.transform.Find("DefaultViewPoint");
        if (viewPoint != null)
        {
            so.FindProperty("_defaultViewPoint").objectReferenceValue = viewPoint;
        }

        // Water plane (optional)
        if (waterPlane != null)
        {
            so.FindProperty("_waterPlane").objectReferenceValue = waterPlane;
        }

        so.ApplyModifiedProperties();
        Debug.Log($"[MapGenBridge] MapConfiguration set: MapId={mapId}");
        return config;
    }

    /// <summary>
    /// Ensure at least one directional light exists for non-lightmapped maps.
    /// </summary>
    void EnsureDirectionalLight(GameObject staticContent)
    {
        var lights = Object.FindObjectsOfType<Light>();
        bool hasDirectional = lights.Any(l => l.type == LightType.Directional);

        if (!hasDirectional)
        {
            GameObject lightGO = new GameObject("Directional Light");
            Undo.RegisterCreatedObjectUndo(lightGO, "Create light");
            lightGO.transform.SetParent(staticContent.transform);
            lightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            Light light = lightGO.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.color = new Color(1f, 0.96f, 0.89f); // Warm white
            light.shadows = LightShadows.Soft;

            Debug.Log("[MapGenBridge] Added directional light");
        }
    }

    /// <summary>
    /// Calculate the world-space bounds of all renderers in the map.
    /// </summary>
    Bounds CalculateMapBounds(GameObject root)
    {
        var renderers = root.GetComponentsInChildren<MeshRenderer>();
        if (renderers.Length == 0)
        {
            return new Bounds(Vector3.zero, Vector3.one * 50f);
        }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }
        return bounds;
    }

    /// <summary>
    /// Mark all GameObjects in hierarchy as static (for lightmapping/batching).
    /// </summary>
    void SetStaticRecursively(GameObject go)
    {
        go.isStatic = true;
        foreach (Transform child in go.transform)
        {
            SetStaticRecursively(child.gameObject);
        }
    }

    /// <summary>
    /// Add scene to EditorBuildSettings if not already present.
    /// </summary>
    void AddSceneToBuildSettings(string scenePath)
    {
        var scenes = EditorBuildSettings.scenes.ToList();
        if (!scenes.Any(s => s.path == scenePath))
        {
            scenes.Add(new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log($"[MapGenBridge] Added {scenePath} to Build Settings");
        }
    }

    /// <summary>
    /// Scan the Generated folder for MapGen output scenes.
    /// </summary>
    void ScanGeneratedScenes()
    {
        string[] guids = AssetDatabase.FindAssets("t:Scene", new[] { GENERATED_FOLDER });
        if (guids.Length == 0)
        {
            guids = AssetDatabase.FindAssets("t:Scene", new[] { "Assets/MapGen" });
        }

        if (guids.Length == 0)
        {
            EditorUtility.DisplayDialog("Scan Results", "No generated scenes found in Assets/MapGen/", "OK");
            return;
        }

        string list = "Found generated scenes:\n\n";
        foreach (var guid in guids)
        {
            list += "- " + AssetDatabase.GUIDToAssetPath(guid) + "\n";
        }
        list += "\nOpen a scene and click 'Convert Current Scene' to process it.";

        EditorUtility.DisplayDialog("Scan Results", list, "OK");
    }
}
