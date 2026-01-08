using UnityEngine;
using System.Collections.Generic;

namespace MapGen.Core
{
    public static class BlueprintColors
    {
        public static readonly Color32 Wall = new Color32(68, 68, 68, 255);       // #444444
        public static readonly Color32 Floor = new Color32(184, 184, 184, 255);   // #B8B8B8
        public static readonly Color32 Water = new Color32(0, 68, 255, 255);      // #0044FF
        public static readonly Color32 Glass = new Color32(0, 255, 255, 255);     // #00FFFF
        
        public static bool Match(Color32 a, Color32 b, int tolerance = 10)
        {
            return Mathf.Abs(a.r - b.r) <= tolerance &&
                   Mathf.Abs(a.g - b.g) <= tolerance &&
                   Mathf.Abs(a.b - b.b) <= tolerance;
        }
    }

    public class GreyboxBuilder
    {
        public GameObject Generate(StackDefinition def, ThemeDefinition theme)
        {
            if (def?.Layers?.layout == null)
            {
                Debug.LogError("[GreyboxBuilder] Missing layout texture.");
                return null;
            }

            var tex = def.Layers.layout;
            var w = tex.width;
            var h = tex.height;
            var pixels = tex.GetPixels32();
            var mpp = def.metersPerPixel;

            GameObject root = new GameObject($"Map_{def.sourceName}");
            GameObject walls = new GameObject("Walls");
            GameObject floors = new GameObject("Floors");
            GameObject water = new GameObject("Water");
            GameObject glass = new GameObject("Glass");
            
            walls.transform.SetParent(root.transform);
            floors.transform.SetParent(root.transform);
            water.transform.SetParent(root.transform);
            glass.transform.SetParent(root.transform);

            // Extract booleans
            bool[] wallGrid = new bool[w * h];
            bool[] floorGrid = new bool[w * h];
            bool[] waterGrid = new bool[w * h];
            bool[] glassGrid = new bool[w * h];

            for (int i = 0; i < pixels.Length; i++)
            {
                if (BlueprintColors.Match(pixels[i], BlueprintColors.Wall)) wallGrid[i] = true;
                else if (BlueprintColors.Match(pixels[i], BlueprintColors.Floor)) floorGrid[i] = true;
                else if (BlueprintColors.Match(pixels[i], BlueprintColors.Water)) { waterGrid[i] = true; } // Water is NOT walkable.
                else if (BlueprintColors.Match(pixels[i], BlueprintColors.Glass)) { glassGrid[i] = true; floorGrid[i] = true; }
            }

            // Optimize
            var wallsList = GreedyMesher.Optimize(wallGrid, w, h, mpp, def.wallHeight);
            var floorsList = GreedyMesher.Optimize(floorGrid, w, h, mpp, 0.1f);
            var waterList = GreedyMesher.Optimize(waterGrid, w, h, mpp, 0.1f); // Water is flat
            var glassList = GreedyMesher.Optimize(glassGrid, w, h, mpp, 0.1f);

            // Generate Objects
            foreach (var q in wallsList) CreateCube(q.position + Vector3.up * (def.wallHeight / 2), q.size, walls.transform, "Wall", true);
            foreach (var q in floorsList) CreateCube(q.position + Vector3.up * 0.05f, q.size, floors.transform, "Floor", true);
            foreach (var q in waterList) CreateCube(q.position + Vector3.up * 0.05f, q.size, water.transform, "Water", true); // Water at floor level? Or slightly lower? Let's keep level.
            foreach (var q in glassList) CreateCube(q.position + Vector3.up * 0.05f, q.size, glass.transform, "Glass", true);

            // Debug Stats
            Debug.Log($"[MapGen] Generated: Walls={wallsList.Count}, Floors={floorsList.Count}, Water={waterList.Count}, Glass={glassList.Count}");

            if (def.Layers?.flow != null)
            {
                GenerateGameplay(def, root, floorGrid, w, h);
            }

            ThemeSystem.Apply(root, theme);
            
            return root;
        }

        private void CreateCube(Vector3 pos, Vector3 scale, Transform parent, string name, bool collider)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent);
            cube.transform.position = pos;
            cube.transform.localScale = scale;
            if (!collider) GameObject.DestroyImmediate(cube.GetComponent<Collider>());
        }

        private void GenerateGameplay(StackDefinition def, GameObject root, bool[] floorGrid, int w, int h)
        {
            if (def.Layers?.flow == null) return;
            // ... (setup code skipped, only showing changes if possible, but context is large) ...
            var tex = def.Layers.flow;
            var pixels = tex.GetPixels32();
            
            GameObject spawns = new GameObject("Spawns"); spawns.transform.SetParent(root.transform);
            GameObject items = new GameObject("Items"); items.transform.SetParent(root.transform);
            
            List<MapGenTeleporterNode> teleporters = new List<MapGenTeleporterNode>();
            int spawnCount = 0;

            for (int i = 0; i < pixels.Length; i++)
            {
                var c = pixels[i];
                if (c.a < 10) continue; 

                FlowToken token = FlowToken.None;
                if (BlueprintColors.Match(c, new Color32(255,255,0,255))) token = FlowToken.Spawn;
                else if (BlueprintColors.Match(c, new Color32(0,255,0,255))) token = FlowToken.JumpPad;
                else if (BlueprintColors.Match(c, new Color32(255,0,255,255))) token = FlowToken.Teleport;
                else if (BlueprintColors.Match(c, new Color32(255,0,0,255))) token = FlowToken.PickupHealth;
                else if (BlueprintColors.Match(c, new Color32(255,127,0,255))) token = FlowToken.PickupArmor;
                else if (BlueprintColors.Match(c, new Color32(0,174,239,255))) token = FlowToken.PickupAmmo;

                if (token == FlowToken.None) continue;

                var x = i % w; var y = i / w;
                
                // Validate Floor (Grid Lookup)
                if (!IsFloor(x, y, w, h, floorGrid))
                {
                    Debug.LogWarning($"[Flow] Skipped {token} at ({x},{y}) - Not on Floor.");
                    continue;
                }

                // ... (Calculation code) ...
                float wx = (x - w * 0.5f) * def.metersPerPixel;
                float wz = (y - h * 0.5f) * def.metersPerPixel;
                Vector3 pos = new Vector3(wx, 0.1f, wz);

                string path = UberVocab.Resolve(token);
                GameObject prefabInstance = null;
                
                // Editor Instantiation
#if UNITY_EDITOR
                if (!string.IsNullOrEmpty(path))
                {
                    var prefabAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefabAsset != null)
                    {
                        prefabInstance = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefabAsset);
                    }
                    else
                    {
                        // Fallback placeholder if asset missing in project
                         // Debug.LogWarning("Missing prefab: " + path);
                    }
                }
#endif

                if (token == FlowToken.Spawn)
                {
                   var go = new GameObject($"Spawn_{spawnCount++}");
                   go.transform.position = pos;
                   go.transform.SetParent(spawns.transform);
                   var sp = go.AddComponent<MapGenSpawnPoint>();
                   
                   if (prefabInstance != null) {
                       prefabInstance.transform.SetParent(go.transform, false);
                       prefabInstance.transform.localPosition = Vector3.zero;
                   } else if (!string.IsNullOrEmpty(path)) {
                       var v = new GameObject($"PREFAB:{path}");
                       v.transform.SetParent(go.transform, false);
                       v.transform.localPosition = Vector3.zero;
                   }
                }
                else if (token == FlowToken.Teleport)
                {
                    var go = new GameObject($"Teleport_{teleporters.Count}");
                    go.transform.position = pos;
                    go.transform.SetParent(items.transform);
                    var node = go.AddComponent<MapGenTeleporterNode>();
                    node.NodeID = teleporters.Count;
                    teleporters.Add(node);
                    
                    if (prefabInstance != null) {
                       prefabInstance.transform.SetParent(go.transform, false);
                       prefabInstance.transform.localPosition = Vector3.zero;
                   } else if (!string.IsNullOrEmpty(path)) {
                       var v = new GameObject($"PREFAB:{path}");
                       v.transform.SetParent(go.transform, false);
                       v.transform.localPosition = Vector3.zero;
                   }
                }
                else
                {
                    // Generic (Pickup, JumpPad, etc)
                    GameObject go;
                    if (prefabInstance != null) {
                        go = prefabInstance;
                        go.name = $"{token}";
                        go.transform.position = pos; 
                        go.transform.SetParent(items.transform, true); // Keep world pos if it was instantiated correctly? No, we set pos manually.
                        // PrefabUtility.InstantiatePrefab puts it at 0,0,0 usually unless parented?
                        // Let's reset parent and local pos.
                        go.transform.SetParent(items.transform, false);
                        go.transform.position = pos; // Set world pos
                    } else {
                        go = new GameObject($"{token}");
                        go.transform.position = pos;
                        go.transform.SetParent(items.transform);
                         if (!string.IsNullOrEmpty(path)) {
                            var v = new GameObject($"PREFAB:{path}");
                            v.transform.SetParent(go.transform, false);
                            v.transform.localPosition = Vector3.zero;
                        }
                    }
                    
                    var pt = go.GetComponent<MapGenPlacedToken>();
                    if (pt == null) pt = go.AddComponent<MapGenPlacedToken>();
                    pt.Token = token;
                    pt.PrefabPath = path;
                }
            }

            // QC: Pair Teleporters
            for(int i=0; i<teleporters.Count; i+=2)
            {
                if (i+1 < teleporters.Count) {
                    teleporters[i].PairID = teleporters[i+1].NodeID;
                    teleporters[i+1].PairID = teleporters[i].NodeID;
                } else {
                    Debug.LogError("[QC] Odd number of teleporters! Last one unpaired.");
                }
            }
            
            if (spawnCount < 8) Debug.LogError($"[QC] Validation Failed: Only {spawnCount} spawns (Min 8).");
        }

        private bool IsFloor(int x, int y, int w, int h, bool[] grid)
        {
             if (x < 0 || x >= w || y < 0 || y >= h) return false;
             return grid[y * w + x];
        }
    }
}
