#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Linq;
using UberStrike.Realtime.Common;

/// <summary>
/// Phase 0: Build the simplest possible UberStrike-compatible map by hand.
/// No BuildFromBlueprint, no generation — just geometry + correct wiring.
/// Proves the integration pipeline works before involving generation.
/// </summary>
public static class TestMapBuilder
{
    const string SCENE_PATH = "Assets/Scenes/LevelGeneratedArena.unity";
    const int MAP_ID = 100;
    const float ARENA_SIZE = 50f;   // 50x50 meters
    const float WALL_HEIGHT = 4f;
    const float WALL_THICKNESS = 0.5f;
    const int SPAWN_COUNT = 8;
    const float SPAWN_RADIUS = 15f; // circle radius for spawn placement

    [MenuItem("Tools/MapGen/Phase 0: Build Test Arena")]
    public static void BuildTestArena()
    {
        if (!EditorUtility.DisplayDialog("Build Test Arena",
            "This will create a minimal test arena and save it as:\n" +
            SCENE_PATH + "\n\n" +
            "Any existing LevelGeneratedArena.unity will be overwritten.",
            "Build", "Cancel"))
            return;

        // Remember current scene so we can return to it
        string previousScene = SceneManager.GetActiveScene().path;

        // 1. Create a fresh empty scene
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        scene.name = "LevelGeneratedArena";

        // 2. Build the root hierarchy (matching working map structure)
        GameObject root = new GameObject("LevelGeneratedArena");

        // 3. StaticContent — all geometry goes here
        GameObject staticContent = new GameObject("StaticContent");
        staticContent.transform.SetParent(root.transform);
        staticContent.transform.localPosition = Vector3.zero;

        BuildFloor(staticContent);
        BuildWalls(staticContent);
        BuildDirectionalLight(staticContent);

        SetStaticRecursively(staticContent);

        // 4. SpawnPoints container with SpawnPoint components
        GameObject spawnContainer = new GameObject("SpawnPoints");
        spawnContainer.transform.SetParent(root.transform);
        spawnContainer.transform.localPosition = Vector3.zero;

        BuildSpawnPoints(spawnContainer);

        // 5. Camera + DefaultViewPoint
        GameObject camGO = new GameObject("Level_Camera");
        camGO.transform.SetParent(root.transform);
        camGO.transform.position = new Vector3(0f, 30f, -30f);
        camGO.transform.LookAt(Vector3.zero);

        Camera cam = camGO.AddComponent<Camera>();
        cam.fieldOfView = 60f;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 500f;
        cam.enabled = false; // UberStrike enables via SetEnabled()

        GameObject viewPoint = new GameObject("DefaultViewPoint");
        viewPoint.transform.SetParent(camGO.transform);
        viewPoint.transform.localPosition = Vector3.zero;
        viewPoint.transform.localRotation = Quaternion.identity;

        // 6. Add MapConfiguration and wire ALL references
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
        // _waterPlane left null (no water in test arena)
        // _combatRange uses defaults (1, 1, 1)

        so.ApplyModifiedProperties();

        // 7. Save scene
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(
            System.IO.Path.GetFullPath(SCENE_PATH)));
        EditorSceneManager.SaveScene(scene, SCENE_PATH);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // 8. Add to build settings
        AddSceneToBuildSettings(SCENE_PATH);

        // 9. Log summary for verification
        var spawns = spawnContainer.GetComponentsInChildren<SpawnPoint>();
        Debug.Log($"[TestMapBuilder] Test arena built successfully!");
        Debug.Log($"[TestMapBuilder]   Scene: {SCENE_PATH}");
        Debug.Log($"[TestMapBuilder]   MapId: {MAP_ID}");
        Debug.Log($"[TestMapBuilder]   Arena: {ARENA_SIZE}x{ARENA_SIZE}m");
        Debug.Log($"[TestMapBuilder]   Spawns: {spawns.Length} (all DeathMatch/NONE)");
        Debug.Log($"[TestMapBuilder]   Camera: {camGO.transform.position}");
        Debug.Log($"[TestMapBuilder]   MapConfiguration wired: camera={cam != null}, " +
            $"viewPoint={viewPoint != null}, staticContent={staticContent != null}, " +
            $"spawnPoints={spawnContainer != null}");

        foreach (var sp in spawns)
        {
            Debug.Log($"[TestMapBuilder]   {sp.name} at {sp.transform.position} " +
                $"mode={sp.GameMode} team={sp.TeamPoint}");
        }

        EditorUtility.DisplayDialog("Test Arena Built",
            $"Saved: {SCENE_PATH}\n\n" +
            $"Arena: {ARENA_SIZE}x{ARENA_SIZE}m\n" +
            $"Spawns: {spawns.Length}\n" +
            $"MapId: {MAP_ID}\n\n" +
            "Next: Open Latest.unity, press Play, select Training > GeneratedArena.",
            "OK");

        // Reopen the previous scene so the user can play
        if (!string.IsNullOrEmpty(previousScene) && previousScene != SCENE_PATH)
        {
            EditorSceneManager.OpenScene(previousScene);
        }
    }

    static void BuildFloor(GameObject parent)
    {
        // Create a large flat floor using a scaled cube (planes don't have good colliders)
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Floor";
        floor.transform.SetParent(parent.transform);
        floor.transform.localPosition = new Vector3(0f, -0.05f, 0f); // top surface at Y=0
        floor.transform.localScale = new Vector3(ARENA_SIZE, 0.1f, ARENA_SIZE);

        // Gray material
        var renderer = floor.GetComponent<MeshRenderer>();
        var mat = new Material(Shader.Find("Standard"));
        mat.color = new Color(0.5f, 0.5f, 0.5f);
        mat.name = "FloorMaterial";
        renderer.sharedMaterial = mat;

        Debug.Log($"[TestMapBuilder] Floor: {ARENA_SIZE}x{ARENA_SIZE}m at Y=0");
    }

    static void BuildWalls(GameObject parent)
    {
        float half = ARENA_SIZE / 2f;
        float halfH = WALL_HEIGHT / 2f;
        float halfT = WALL_THICKNESS / 2f;

        var wallMat = new Material(Shader.Find("Standard"));
        wallMat.color = new Color(0.35f, 0.35f, 0.4f);
        wallMat.name = "WallMaterial";

        // North wall (positive Z)
        CreateWall(parent, "Wall_North", wallMat,
            new Vector3(0, halfH, half + halfT),
            new Vector3(ARENA_SIZE + WALL_THICKNESS, WALL_HEIGHT, WALL_THICKNESS));

        // South wall (negative Z)
        CreateWall(parent, "Wall_South", wallMat,
            new Vector3(0, halfH, -(half + halfT)),
            new Vector3(ARENA_SIZE + WALL_THICKNESS, WALL_HEIGHT, WALL_THICKNESS));

        // East wall (positive X)
        CreateWall(parent, "Wall_East", wallMat,
            new Vector3(half + halfT, halfH, 0),
            new Vector3(WALL_THICKNESS, WALL_HEIGHT, ARENA_SIZE + WALL_THICKNESS));

        // West wall (negative X)
        CreateWall(parent, "Wall_West", wallMat,
            new Vector3(-(half + halfT), halfH, 0),
            new Vector3(WALL_THICKNESS, WALL_HEIGHT, ARENA_SIZE + WALL_THICKNESS));

        Debug.Log($"[TestMapBuilder] 4 walls, {WALL_HEIGHT}m tall, {WALL_THICKNESS}m thick");
    }

    static void CreateWall(GameObject parent, string name, Material mat, Vector3 pos, Vector3 scale)
    {
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = name;
        wall.transform.SetParent(parent.transform);
        wall.transform.localPosition = pos;
        wall.transform.localScale = scale;
        wall.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }

    static void BuildSpawnPoints(GameObject container)
    {
        for (int i = 0; i < SPAWN_COUNT; i++)
        {
            float angle = (float)i / SPAWN_COUNT * Mathf.PI * 2f;
            float x = Mathf.Cos(angle) * SPAWN_RADIUS;
            float z = Mathf.Sin(angle) * SPAWN_RADIUS;

            GameObject spawnGO = new GameObject($"SpawnPoint_{i:D2}");
            spawnGO.transform.SetParent(container.transform);
            // Y=1.0 — player gets +1 from LocalPlayer.SpawnPlayerAt, so feet at Y=1, eye ~Y=2.6
            // Actually, LocalPlayer adds Vector3.up (1m), so place spawn at floor level Y=0
            // That puts the player at Y=1 which is standing on the floor (Y=0)
            spawnGO.transform.position = new Vector3(x, 0f, z);
            // Face toward center
            spawnGO.transform.rotation = Quaternion.Euler(0f, Mathf.Atan2(-x, -z) * Mathf.Rad2Deg, 0f);

            SpawnPoint sp = spawnGO.AddComponent<SpawnPoint>();
            // ALL spawns are DeathMatch/NONE — Training mode uses DeathMatch spawns
            sp.GameMode = global::GameMode.DeathMatch;
            sp.TeamPoint = TeamID.NONE;
        }

        Debug.Log($"[TestMapBuilder] {SPAWN_COUNT} spawn points in circle, radius={SPAWN_RADIUS}m");
    }

    static void BuildDirectionalLight(GameObject parent)
    {
        GameObject lightGO = new GameObject("Directional Light");
        lightGO.transform.SetParent(parent.transform);
        lightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        Light light = lightGO.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.2f;
        light.color = new Color(1f, 0.96f, 0.89f);
        light.shadows = LightShadows.Soft;
    }

    static void SetStaticRecursively(GameObject go)
    {
        go.isStatic = true;
        foreach (Transform child in go.transform)
        {
            SetStaticRecursively(child.gameObject);
        }
    }

    static void AddSceneToBuildSettings(string scenePath)
    {
        var scenes = EditorBuildSettings.scenes.ToList();
        if (!scenes.Any(s => s.path == scenePath))
        {
            scenes.Add(new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log($"[TestMapBuilder] Added {scenePath} to Build Settings");
        }
    }
}
#endif
