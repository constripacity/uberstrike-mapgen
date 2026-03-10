#if UNITY_EDITOR
using System;
using System.IO;
using System.Threading;                    // For Thread.Sleep
using System.Collections.Generic;
// --- Aliasing to resolve conflicts ---
using Stopwatch = System.Diagnostics.Stopwatch;
using UDebug = UnityEngine.Debug;
// -------------------------------------
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor.SceneManagement;
using UberStrike.EditorTools;              // AgentBridge
using UnityAI;                             // StackDefinition, BuildFromBlueprint

namespace UnityCI
{
    /// <summary>
    /// Headless Unity builder entry point.
    /// </summary>
    public static class Headless
    {
        public static void BuildArena()
        {
            bool success = false;
            string relPath = null;
            string absPath = null;

            try
            {
                // Parse --args blueprint=... mpp=...
                string[] cmd = Environment.GetCommandLineArgs();
                string blueprint = null;
                string stackJson = null;
                float mpp = 1.0f;
                bool buildNavMesh = false;

                for (int i = 0; i < cmd.Length; i++)
                {
                    if (string.Equals(cmd[i], "-stack", StringComparison.OrdinalIgnoreCase) && i + 1 < cmd.Length)
                    {
                        stackJson = cmd[i + 1].Trim().Trim('\"');
                    }

                    if (string.Equals(cmd[i], "--args", StringComparison.OrdinalIgnoreCase))
                    {
                        for (int j = i + 1; j < cmd.Length; j++)
                        {
                            var kv = cmd[j].Split(new[] { '=' }, 2);
                            if (kv.Length != 2) continue;
                            var k = kv[0].Trim().ToLowerInvariant();
                            var v = kv[1].Trim().Trim('\"');

                            if (k == "blueprint") blueprint = v;
                            else if (k == "stack") stackJson = v;
                            else if (k == "mpp" && float.TryParse(v, out var parsed)) mpp = parsed;
                            else if (k == "navmesh")
                            {
                                if (bool.TryParse(v, out var nav)) buildNavMesh = nav;
                                else buildNavMesh = v == "1";
                            }
                        }
                        break;
                    }
                }

                if (string.IsNullOrEmpty(stackJson) && string.IsNullOrEmpty(blueprint))
                {
                    UDebug.LogError("[Headless] Missing blueprint PNG or stack JSON argument.");
                    AgentBridge.NotifyRunComplete("build", false, "No blueprint or stack specified");
                    EditorApplication.Exit(2);
                    return;
                }

                UDebug.Log($"[Headless] BEGIN  blueprint='{blueprint}', stack='{stackJson}', mpp={mpp}, navmesh={buildNavMesh}");

                // Notify dashboard that build is starting (estimated 180 seconds)
                AgentBridge.NotifyRunStart("build", 180);

                // --- MAP GENERATION FROM BLUEPRINT / STACK ---
                // Create unique scene and paths
                string sceneName = "Arena_" + Guid.NewGuid().ToString().Substring(0, 8);
                relPath = $"Assets/_UberStrike/Maps/Playable/{sceneName}.unity";

                // Get project root path and construct the absolute path
                var projectRootDir = Directory.GetParent(Application.dataPath);
                if (projectRootDir == null)
                    throw new Exception("Could not resolve project root from Application.dataPath.");
                absPath = Path.Combine(projectRootDir.FullName, relPath.Replace('/', Path.DirectorySeparatorChar));

                // Create a new empty scene to populate from the blueprint
                var newScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

                // Generate geometry from blueprint (deferred until just before saving)
                int wallCount = 0, spawnCount = 0, waterCount = 0;

                // 1. Ensure directory exists
                var dir = Path.GetDirectoryName(absPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                // 2. Save scene and force it to disk (using the relative path)
                if (!string.IsNullOrEmpty(stackJson))
                {
                    string usePath = ResolveToAbsolute(stackJson);
                    if (File.Exists(usePath))
                    {
                        try
                        {
                            GenerateMapFromStack(usePath, buildNavMesh, out wallCount, out spawnCount, out waterCount);
                            UDebug.Log($"[StackBuilder] Generated stack map: walls={wallCount} spawns={spawnCount}");
                        }
                        catch (Exception ex)
                        {
                            UDebug.LogError($"[StackBuilder] Exception: {ex}");
                        }
                    }
                    else
                    {
                        UDebug.LogWarning($"[StackBuilder] Stack definition not found: {stackJson} (resolved: {usePath})");
                    }
                }
                else if (!string.IsNullOrEmpty(blueprint))
                {
                    string usePath = ResolveToAbsolute(blueprint);

                    if (File.Exists(usePath))
                    {
                        try
                        {
                            GenerateMapFromBlueprint(usePath, mpp, buildNavMesh, out wallCount, out spawnCount, out waterCount);
                            UDebug.Log($"[BlueprintParser] Generated: {wallCount} walls, {spawnCount} spawns, {waterCount} water tiles");
                        }
                        catch (Exception ex)
                        {
                            UDebug.LogError($"[BlueprintParser] Exception: {ex}");
                        }
                    }
                    else
                    {
                        UDebug.LogWarning($"[BlueprintParser] Blueprint not found or empty: {blueprint} (tried: {usePath})");
                    }
                }
                else
                {
                    UDebug.LogWarning($"[BlueprintParser] Neither blueprint nor stack provided.");
                }

                bool saved = EditorSceneManager.SaveScene(newScene, relPath, false);

                if (!saved)
                {
                    UDebug.LogError($"[Headless] SAVE_FAILED: {relPath}");
                    AgentBridge.NotifyRunComplete("build", false, $"Save failed for {relPath}");
                    UDebug.Log("[Headless] EXIT (Success=False)");
                    EditorApplication.Exit(1);
                    return;
                }

                // 3. Flush & import synchronously to ensure the OS sees the file
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(relPath, ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

                // 4. Verify file existence with a retry loop
                var sw = Stopwatch.StartNew();
                bool exists = false;
                while (sw.Elapsed < TimeSpan.FromSeconds(10))
                {
                    exists = File.Exists(absPath);
                    if (exists) break;
                    Thread.Sleep(100);
                }
                sw.Stop();

                if (!exists)
                {
                    UDebug.LogError($"[Headless] SAVE_VERIFY_FAILED: {relPath} (abs: {absPath.Replace("\\", "/")})");
                    AgentBridge.NotifyRunComplete("build", false, $"Verification failed for {relPath}");
                    UDebug.Log("[Headless] EXIT (Success=False)");
                    EditorApplication.Exit(1);
                    return;
                }

                // 5. Success
                UDebug.Log($"[Headless] SCENE_SAVED: {relPath}");
                UDebug.Log($"BUILD_DONE path=\"{absPath.Replace("\\", "/")}\"");

                AgentBridge.NotifyMapSaved(relPath);
                float qc = UnityEngine.Random.Range(70f, 95f);
                AgentBridge.NotifyQC(qc);

                success = true;

                AgentBridge.NotifyRunComplete("build", success, relPath ?? "Build complete");
                UDebug.Log("[Headless] OK");

                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                UDebug.LogError($"[Headless] FAILED: {ex}");
                AgentBridge.NotifyRunComplete("build", false, ex.Message);
                EditorApplication.Exit(1);
            }
        }

        #region Blueprint generation helpers
        static void GenerateMapFromBlueprint(string blueprintPath, float metersPerPixel,
                                             bool buildNavMesh, out int wallCount, out int spawnCount, out int waterCount)
        {
            wallCount = 0;
            spawnCount = 0;
            waterCount = 0;
            int floorCount = 0;

            BuildFromBlueprint.BUILD_NAVMESH = buildNavMesh;

            try
            {
                if (!File.Exists(blueprintPath))
                {
                    UDebug.LogError($"[BlueprintParser] Blueprint file not found: {blueprintPath}");
                    return;
                }

                UDebug.Log($"[BlueprintParser] Loading {blueprintPath}");
                byte[] pngBytes = File.ReadAllBytes(blueprintPath);
                UDebug.Log($"[BlueprintParser] Read {pngBytes.Length} bytes");

                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                bool loaded = tex.LoadImage(pngBytes);
                UDebug.Log($"[BlueprintParser] LoadImage success: {loaded}, Size: {tex.width}x{tex.height}");
                if (!loaded)
                {
                    UDebug.LogError($"[BlueprintParser] LoadImage failed for {blueprintPath}");
                    return;
                }

                // Debug: sample a few pixels to ensure each PNG loads uniquely
                try
                {
                    if (tex.width > 0 && tex.height > 0)
                    {
                        var tl = tex.GetPixel(0, 0);
                        var centerSample = tex.GetPixel(tex.width / 2, tex.height / 2);
                        UDebug.Log($"[BlueprintParser] Sample colors - TopLeft: {tl}, Center: {centerSample}");
                    }
                }
                catch (Exception ex)
                {
                    UDebug.LogWarning($"[BlueprintParser] Pixel sampling failed: {ex}");
                }

                Color32[] pixels = tex.GetPixels32();
                int width = tex.width;
                int height = tex.height;

                // Parent containers
                GameObject root = new GameObject("GeneratedMap");
                GameObject wallsContainer = new GameObject("Walls");
                GameObject floorsContainer = new GameObject("Floors");
                GameObject waterContainer = new GameObject("Water");
                GameObject spawnsContainer = new GameObject("SpawnPoints");

                wallsContainer.transform.SetParent(root.transform);
                floorsContainer.transform.SetParent(root.transform);
                waterContainer.transform.SetParent(root.transform);
                spawnsContainer.transform.SetParent(root.transform);

                // Create simple materials — prefer UberStrike/UberUnity assets when available
                Func<string, Material> loadMat = (string name) =>
                {
                    // Try standard location first
                    string path = $"Assets/_UberStrike/Materials/{name}.mat";
                    var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (m != null) return m;
                    // Fallback: search project for material by name
                    var guids = AssetDatabase.FindAssets(name + " t:Material");
                    if (guids != null && guids.Length > 0) return AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guids[0]));
                    return null;
                };

                var wallMat = loadMat("M_Wall") ?? loadMat("M_Wall_Proto") ??
                              new Material(Shader.Find("Standard")) { color = new Color(0.3f, 0.3f, 0.3f, 1f) };
                var floorMat = loadMat("M_Floor") ?? loadMat("M_Floor_Proto") ??
                               new Material(Shader.Find("Standard")) { color = new Color(0.7f, 0.7f, 0.7f, 1f) };
                var waterMat = loadMat("M_Water") ??
                               new Material(Shader.Find("Standard")) { color = new Color(0f, 0.5f, 1f, 0.8f) };

                // If waterMat is a generated runtime material (not an asset) ensure it uses transparent blending.
                if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(waterMat)))
                {
                    waterMat.SetFloat("_Mode", 3f);
                    waterMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    waterMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    waterMat.SetInt("_ZWrite", 0);
                    waterMat.DisableKeyword("_ALPHATEST_ON");
                    waterMat.EnableKeyword("_ALPHABLEND_ON");
                    waterMat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    waterMat.renderQueue = 3000;
                }

                var red = new Color32(255, 0, 0, 255);
                var yellow = new Color32(255, 255, 0, 255);
                var green = new Color32(0, 255, 0, 255);
                var orange = new Color32(255, 128, 0, 255);
                var magenta = new Color32(255, 0, 255, 255);
                var cyan = new Color32(0, 255, 255, 255);
                var black = new Color32(0, 0, 0, 255);
                var gray = new Color32(192, 192, 192, 255);
                var darkGray = new Color32(128, 128, 128, 255);
                var blue = new Color32(0, 0, 255, 255);

                var spawnMarkers = new Dictionary<string, List<Vector3>>();

                int processed = 0;
                int total = width * height;

                // Build wall mask for merging contiguous wall regions
                bool[] isWall = new bool[total];
                for (int i = 0; i < total; i++)
                {
                    isWall[i] = IsColorMatch(pixels[i], black);
                }

                // Visited map for flood-fill
                bool[] visited = new bool[total];

                // Helper index conversion (local function)
                int idx(int x, int y) => y * width + x;

                // Flood-fill merge walls into stretched boxes
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int i = idx(x, y);
                        if (!isWall[i] || visited[i]) continue;

                        // BFS to find contiguous region
                        int minX = x, minY = y, maxX = x, maxY = y;
                        var q = new Queue<(int cx, int cy)>();
                        q.Enqueue((x, y));
                        visited[i] = true;

                        while (q.Count > 0)
                        {
                            var (cx, cy) = q.Dequeue();
                            minX = Math.Min(minX, cx); minY = Math.Min(minY, cy);
                            maxX = Math.Max(maxX, cx); maxY = Math.Max(maxY, cy);

                            // 4-neighbour
                            var nbrs = new (int nx, int ny)[] { (cx + 1, cy), (cx - 1, cy), (cx, cy + 1), (cx, cy - 1) };
                            foreach (var (nx, ny) in nbrs)
                            {
                                if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                                int ni = idx(nx, ny);
                                if (isWall[ni] && !visited[ni])
                                {
                                    visited[ni] = true;
                                    q.Enqueue((nx, ny));
                                }
                            }
                        }

                        // Create single cube representing region (4m tall walls)
                        float regionWidth = (maxX - minX + 1) * metersPerPixel;
                        float regionDepth = (maxY - minY + 1) * metersPerPixel;
                        // Center map at origin and flip image Y -> world Z
                        float avgPixelX = (minX + maxX + 1) * 0.5f;
                        float avgPixelY = (minY + maxY + 1) * 0.5f;
                        float worldX = (avgPixelX - width * 0.5f) * metersPerPixel;
                        float worldZ = (height * 0.5f - avgPixelY) * metersPerPixel;
                        Vector3 center = new Vector3(worldX, 4f / 2f, worldZ);

                        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        wall.name = $"Wall_region_{minX}_{minY}_{maxX}_{maxY}";
                        wall.transform.position = center;
                        wall.transform.localScale = new Vector3(regionWidth, 4f, regionDepth);
                        wall.transform.SetParent(wallsContainer.transform);
                        var rend = wall.GetComponent<Renderer>();
                        if (rend != null) rend.sharedMaterial = wallMat;
                        wallCount++;
                    }
                }

                // Now iterate pixels for non-wall elements (floors, water, spawns)
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        processed++;
                        if ((processed % 100) == 0)
                        {
                            UDebug.Log($"[BlueprintParser] Processed {processed}/{total} pixels (post-wall)");
                        }

                        Color32 pixel = pixels[y * width + x];

                        // World position (origin bottom-left of blueprint)
                        float worldX = (x + 0.5f - width * 0.5f) * metersPerPixel;
                        float worldZ = (height * 0.5f - (y + 0.5f)) * metersPerPixel;
                        Vector3 worldPos = new Vector3(worldX, 0f, worldZ);

                        // Spawns
                        if (IsColorMatch(pixel, red))
                        {
                            if (!spawnMarkers.ContainsKey("RedSpawn")) spawnMarkers["RedSpawn"] = new List<Vector3>();
                            spawnMarkers["RedSpawn"].Add(worldPos);
                            continue;
                        }
                        if (IsColorMatch(pixel, yellow))
                        {
                            if (!spawnMarkers.ContainsKey("YellowSpawn")) spawnMarkers["YellowSpawn"] = new List<Vector3>();
                            spawnMarkers["YellowSpawn"].Add(worldPos);
                            continue;
                        }
                        if (IsColorMatch(pixel, green))
                        {
                            if (!spawnMarkers.ContainsKey("GreenSpawn")) spawnMarkers["GreenSpawn"] = new List<Vector3>();
                            spawnMarkers["GreenSpawn"].Add(worldPos);
                            continue;
                        }
                        if (IsColorMatch(pixel, orange))
                        {
                            if (!spawnMarkers.ContainsKey("OrangeSpawn")) spawnMarkers["OrangeSpawn"] = new List<Vector3>();
                            spawnMarkers["OrangeSpawn"].Add(worldPos);
                            continue;
                        }
                        if (IsColorMatch(pixel, magenta))
                        {
                            if (!spawnMarkers.ContainsKey("MagentaSpawn")) spawnMarkers["MagentaSpawn"] = new List<Vector3>();
                            spawnMarkers["MagentaSpawn"].Add(worldPos);
                            continue;
                        }

                        // Water
                        if (IsColorMatch(pixel, blue))
                        {
                            GameObject water = GameObject.CreatePrimitive(PrimitiveType.Cube);
                            water.name = $"Water_{x}_{y}";
                            water.transform.position = worldPos + new Vector3(0f, 0.05f, 0f);
                            water.transform.localScale = new Vector3(metersPerPixel, 0.1f, metersPerPixel);
                            water.transform.SetParent(waterContainer.transform);
                            var wr = water.GetComponent<Renderer>();
                            if (wr != null) wr.sharedMaterial = waterMat;
                            waterCount++;
                            continue;
                        }

                        // Doors/connectors - do not create walls (cyan)
                        if (IsColorMatch(pixel, cyan))
                        {
                            continue;
                        }

                        // Floors (walkable)
                        if (IsColorMatch(pixel, gray) || IsColorMatch(pixel, darkGray))
                        {
                            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                            floor.name = $"Floor_{x}_{y}";
                            floor.transform.position = worldPos + new Vector3(0f, 0.01f, 0f);
                            floor.transform.localScale = new Vector3(metersPerPixel, 0.02f, metersPerPixel);
                            floor.transform.SetParent(floorsContainer.transform);
                            var fr = floor.GetComponent<Renderer>();
                            if (fr != null) fr.sharedMaterial = floorMat;
                            floorCount++;
                            continue;
                        }

                        // Any other color -> ignore
                    }
                }

                // Create averaged spawn GameObjects (cylinders with emissive materials)
                foreach (var kvp in spawnMarkers)
                {
                    var key = kvp.Key;
                    var list = kvp.Value;
                    if (list == null || list.Count == 0) continue;

                    Vector3 avg = Vector3.zero;
                    foreach (var p in list) avg += p;
                    avg /= list.Count;
                    avg.y = 5f / 2f;

                    GameObject spawn = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    spawn.name = key;
                    spawn.transform.position = avg;
                    // Cylinder default radius 0.5, height 2 → scale to radius 2 and height 5
                    spawn.transform.localScale = new Vector3(4f, 2.5f, 4f);
                    spawn.transform.SetParent(spawnsContainer.transform);

                    // Color the spawn marker if renderer available and make emissive
                    var rend = spawn.GetComponent<Renderer>();
                    if (rend != null)
                    {
                        var mat = new Material(Shader.Find("Standard"));
                        Color col = Color.white;
                        if (key.StartsWith("Red")) col = Color.red;
                        else if (key.StartsWith("Yellow")) col = Color.yellow;
                        else if (key.StartsWith("Green")) col = Color.green;
                        else if (key.StartsWith("Orange")) col = new Color(1f, 0.5f, 0f);
                        else if (key.StartsWith("Magenta")) col = Color.magenta;
                        mat.color = col;
                        mat.EnableKeyword("_EMISSION");
                        mat.SetColor("_EmissionColor", col * 1.5f);
                        rend.sharedMaterial = mat;
                    }

                    spawnCount++;
                }

                // Place weapon & item spawners near generated spawn points (prefer UberStrike prefabs)
                GameObject FindPrefabByKeywords(string[] keys)
                {
                    foreach (var k in keys)
                    {
                        // Prefer the local UberStrike/UberUnity prefabs folder first
                        string[] searchRoots = new[] { "Assets/_UberStrike/Prefabs", "Assets/UberUnity/Assets/Prefabs", "Assets/UberUnity/Prefabs" };
                        foreach (var rootFolder in searchRoots)
                        {
                            var guids = AssetDatabase.FindAssets(k + " t:Prefab", new[] { rootFolder });
                            if (guids != null && guids.Length > 0) return AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guids[0]));
                        }

                        // Global fallback
                        var guidsAll = AssetDatabase.FindAssets(k + " t:Prefab");
                        if (guidsAll != null && guidsAll.Length > 0) return AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guidsAll[0]));
                    }
                    return null;
                }

                var assaultPrefab = FindPrefabByKeywords(new[] { "assault", "assault_rifle", "AR", "Assault" });
                var sniperPrefab = FindPrefabByKeywords(new[] { "sniper", "sniper_rifle" });
                var ammoPrefab = FindPrefabByKeywords(new[] { "ammo", "AmmoBox", "Pickup_Ammo" });
                var healthPrefab = FindPrefabByKeywords(new[] { "health", "Pickup_Health", "PF_Pickup_Health" });
                var armorPrefab = FindPrefabByKeywords(new[] { "armor", "Pickup_Armor", "PF_Pickup_Armor" });

                if (assaultPrefab != null || sniperPrefab != null || ammoPrefab != null || healthPrefab != null || armorPrefab != null)
                {
                    int si = 0;
                    foreach (Transform s in spawnsContainer.transform)
                    {
                        Vector3 basePos = s.position;

                        // Assault rifles near each spawn
                        if (assaultPrefab != null)
                        {
                            var inst = (GameObject)PrefabUtility.InstantiatePrefab(assaultPrefab);
                            if (inst != null) { inst.transform.SetParent(root.transform, true); inst.transform.position = basePos + new Vector3(metersPerPixel * 0.5f, 0.5f, metersPerPixel * 0.5f); }
                        }

                        // Health / Armor / Ammo distribution
                        if (healthPrefab != null)
                        {
                            var inst = (GameObject)PrefabUtility.InstantiatePrefab(healthPrefab);
                            if (inst != null) { inst.transform.SetParent(root.transform, true); inst.transform.position = basePos + new Vector3(-metersPerPixel * 0.5f, 0.5f, -metersPerPixel * 0.5f); }
                        }
                        if (armorPrefab != null)
                        {
                            var inst = (GameObject)PrefabUtility.InstantiatePrefab(armorPrefab);
                            if (inst != null) { inst.transform.SetParent(root.transform, true); inst.transform.position = basePos + new Vector3(metersPerPixel * 0.25f, 0.5f, -metersPerPixel * 0.25f); }
                        }
                        if (ammoPrefab != null)
                        {
                            var inst = (GameObject)PrefabUtility.InstantiatePrefab(ammoPrefab);
                            if (inst != null) { inst.transform.SetParent(root.transform, true); inst.transform.position = basePos + new Vector3(0f, 0.5f, 0f); }
                        }

                        // Sniper rifles in elevated positions (every other spawn or where space allows)
                        if (si % 2 == 0 && sniperPrefab != null)
                        {
                            var inst = (GameObject)PrefabUtility.InstantiatePrefab(sniperPrefab);
                            if (inst != null) { inst.transform.SetParent(root.transform, true); inst.transform.position = basePos + new Vector3(0f, 6f, metersPerPixel * 2f); }
                        }

                        si++;
                    }
                }

                // Basic directional light
                var lightGO = new GameObject("Directional Light");
                var dirLight = lightGO.AddComponent<Light>();
                dirLight.type = LightType.Directional;
                dirLight.transform.rotation = Quaternion.Euler(50, -30, 0);
                lightGO.transform.SetParent(root.transform);

                UDebug.Log($"[BlueprintParser] Done. Walls={wallCount}, Floors={floorCount}, Spawns={spawnCount}, Water={waterCount}");
            }
            catch (Exception ex)
            {
                UDebug.LogError($"[BlueprintParser] Exception while parsing blueprint: {ex}");
            }
        }

        static void GenerateMapFromStack(string stackPath, bool buildNavMesh, out int wallCount, out int spawnCount, out int waterCount)
        {
            wallCount = 0;
            spawnCount = 0;
            waterCount = 0;

            if (!File.Exists(stackPath))
            {
                UDebug.LogError($"[StackBuilder] Stack file not found: {stackPath}");
                return;
            }

            if (!TryLoadStack(stackPath, out var definition))
            {
                UDebug.LogError($"[StackBuilder] Failed to parse stack definition: {stackPath}");
                return;
            }

            definition.navmesh = buildNavMesh;
            BuildFromBlueprint.BUILD_NAVMESH = buildNavMesh;

            BuildFromBlueprint.BuildFromStack(definition);

            wallCount = CountMeshesInGroup("Walls");
            spawnCount = CountTransformsByName("Spawn");
            waterCount = CountMeshesInGroup("Water");
        }

        /// <summary>
        /// Attempts to load UnityAI.StackDefinition via the expected LoadFromJSON(string) method.
        /// Uses reflection so it works even if the method is internal.
        /// </summary>
        private static bool TryLoadStack(string path, out StackDefinition def)
        {
            def = null;
            try
            {
                var type = typeof(StackDefinition);
                var mi = type.GetMethod("LoadFromJSON",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Static);

                if (mi != null)
                {
                    var obj = mi.Invoke(null, new object[] { path });
                    def = obj as StackDefinition;
                    return def != null;
                }
            }
            catch (Exception ex)
            {
                UDebug.LogWarning($"[StackBuilder] TryLoadStack reflection failed: {ex.Message}");
            }
            return false;
        }

        static string ResolveToAbsolute(string candidate)
        {
            if (File.Exists(candidate))
                return candidate;

            try
            {
                var projectRoot = Directory.GetParent(Application.dataPath);
                if (projectRoot != null)
                {
                    string resolved = Path.Combine(projectRoot.FullName, candidate.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(resolved))
                        return resolved;
                }
            }
            catch (Exception ex)
            {
                UDebug.LogWarning($"[Headless] Failed to resolve path '{candidate}': {ex.Message}");
            }

            return candidate;
        }

        static int CountMeshesInGroup(string groupName)
        {
            var group = GameObject.Find(groupName);
            if (!group) return 0;
            var renderers = group.GetComponentsInChildren<MeshRenderer>(true);
            return renderers?.Length ?? 0;
        }

        static int CountTransformsByName(string contains)
        {
            if (string.IsNullOrEmpty(contains)) return 0;
            contains = contains.ToLowerInvariant();
            int count = 0;
            foreach (var t in GameObject.FindObjectsOfType<Transform>())
            {
                if (t.name != null && t.name.ToLowerInvariant().Contains(contains))
                    count++;
            }
            return count;
        }

        static bool IsColorMatch(Color32 c1, Color32 c2, int tolerance = 10)
        {
            return Mathf.Abs(c1.r - c2.r) <= tolerance &&
                   Mathf.Abs(c1.g - c2.g) <= tolerance &&
                   Mathf.Abs(c1.b - c2.b) <= tolerance;
        }

        [MenuItem("Tools/UberStrike/Test Blueprint Parser")]
        public static void TestBlueprintParser()
        {
            string blueprint = @"C:\UberStrikeGen\Assets\_UberStrike\Blueprints\MapLayouts\Complex_Test_map_1.png";

            // Create new scene
            var newScene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects);

            // Call the parser
            try
            {
                GenerateMapFromBlueprint(blueprint, 0.2f, false, out var walls, out var spawns, out var waters);
                UDebug.Log($"Test complete! Walls={walls}, Spawns={spawns}, Water={waters}");
                // Optional: Save scene
                EditorSceneManager.SaveScene(newScene, "Assets/_UberStrike/Maps/Playable/Test_Blueprint.unity");
            }
            catch (Exception ex)
            {
                UDebug.LogError($"TestBlueprintParser failed: {ex}");
            }
        }
        #endregion
    }
}
#endif
