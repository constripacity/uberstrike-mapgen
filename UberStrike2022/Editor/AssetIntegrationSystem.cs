#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityAI
{
    /// <summary>
    /// Central point that stitches UberStrike prefabs, gameplay rules and generated geometry together.
    /// </summary>
    public static class AssetIntegrationSystem
    {
        public sealed class PrefabCatalogSnapshot
        {
            public readonly List<GameObject> Weapons = new List<GameObject>();
            public readonly List<GameObject> Pickups = new List<GameObject>();
            public readonly List<GameObject> UtilityPrefabs = new List<GameObject>();
            public readonly Dictionary<string, List<Material>> ThemeMaterials = new Dictionary<string, List<Material>>(StringComparer.OrdinalIgnoreCase);
            public MapGameplaySet GameplaySet;

            public IEnumerable<GameObject> AllPrefabs
            {
                get
                {
                    foreach (var weapon in Weapons) yield return weapon;
                    foreach (var pickup in Pickups) yield return pickup;
                    foreach (var util in UtilityPrefabs) yield return util;
                }
            }
        }

        private static readonly string[] WeaponFolders =
        {
            "Assets/UberStrike/Prefabs/Weapons",
            "Assets/UberStrike/Prefabs/Items/Weapons"
        };

        private static readonly string[] PickupFolders =
        {
            "Assets/UberStrike/Prefabs/Pickups",
            "Assets/UberStrike/Prefabs/Items/Pickups"
        };

        private static readonly string[] UtilityFolders =
        {
            "Assets/UberStrike/Prefabs/Gameplay",
            "Assets/UberStrike/Prefabs/Props"
        };

        /// <summary>
        /// Loads a snapshot of available prefabs and materials from the UberStrike content folders.
        /// </summary>
        public static PrefabCatalogSnapshot LoadSnapshot()
        {
            var catalogAsset = AssetCatalog.Instance;
            if (catalogAsset != null)
            {
                var fromCatalog = new PrefabCatalogSnapshot
                {
                    GameplaySet = ResolveGameplaySet()
                };

                fromCatalog.Weapons.AddRange(catalogAsset.weapons.Where(e => e?.prefab).Select(e => e.prefab));
                fromCatalog.Pickups.AddRange(catalogAsset.pickups.Where(e => e?.prefab).Select(e => e.prefab));
                fromCatalog.UtilityPrefabs.AddRange(catalogAsset.gameplay.Where(e => e?.prefab).Select(e => e.prefab));

                foreach (var kv in catalogAsset.ToMaterialLookup())
                {
                    fromCatalog.ThemeMaterials[kv.Key] = kv.Value;
                }

                Debug.Log($"[AssetIntegrationSystem] Loaded catalog snapshot (weapons={fromCatalog.Weapons.Count}, pickups={fromCatalog.Pickups.Count}, themes={fromCatalog.ThemeMaterials.Count}).");
                return fromCatalog;
            }

            var snapshot = new PrefabCatalogSnapshot();
            snapshot.GameplaySet = ResolveGameplaySet();
            snapshot.Weapons.AddRange(LoadPrefabs(WeaponFolders));
            snapshot.Pickups.AddRange(LoadPrefabs(PickupFolders));
            snapshot.UtilityPrefabs.AddRange(LoadPrefabs(UtilityFolders));
            snapshot.ThemeMaterials.Clear();
            foreach (var entry in BuildMaterialLibrary())
            {
                if (!snapshot.ThemeMaterials.TryGetValue(entry.Key, out var list))
                {
                    list = new List<Material>();
                    snapshot.ThemeMaterials.Add(entry.Key, list);
                }
                list.AddRange(entry.Value);
            }
            return snapshot;
        }

        /// <summary>
        /// Runs the full integration flow: gameplay prefabs, spawn logic, materials and AI driven props.
        /// </summary>
        public static void Integrate(StackDefinition definition, GameObject root)
        {
            if (definition == null || root == null)
            {
                Debug.LogWarning("[AssetIntegrationSystem] Missing definition or root, skipping integration.");
                return;
            }

            var snapshot = LoadSnapshot();
            var flow = FlowAnalyser.Analyse(definition);

            PrefabPlacementAI.PlacePrefabs(root, snapshot, flow, definition);
            ApplySpawnLogic(root, snapshot, flow);
            ApplyThemeMaterials(root, definition, snapshot);
        }

        private static void ApplySpawnLogic(GameObject root, PrefabCatalogSnapshot snapshot, FlowAnalysisResult flow)
        {
            if (!root || snapshot?.GameplaySet == null)
            {
                return;
            }

            var gameplaySet = snapshot.GameplaySet;
            var spawnParent = root.transform.Find("Spawns");
            if (!spawnParent)
            {
                spawnParent = new GameObject("Spawns").transform;
                spawnParent.SetParent(root.transform, false);
            }

            if (spawnParent.childCount > 0)
            {
                return;
            }

            var layout = gameplaySet.SpawnNeutral ? gameplaySet.SpawnNeutral.transform.localScale : Vector3.one;
            float radius = Mathf.Max(layout.x, layout.z) * 0.5f + 0.5f;

            int requiredSpawns = Mathf.Max(4, flow.spawnCount);
            var navBounds = CalculateNavBounds(root);
            var ring = SampleRing(navBounds, requiredSpawns, radius);

            for (int i = 0; i < ring.Count; i++)
            {
                GameObject prefab = gameplaySet.SpawnNeutral;
                if (i % 3 == 1 && gameplaySet.SpawnRed) prefab = gameplaySet.SpawnRed;
                else if (i % 3 == 2 && gameplaySet.SpawnGreen) prefab = gameplaySet.SpawnGreen;
                if (!prefab) prefab = gameplaySet.SpawnNeutral;
                if (!prefab) continue;

                var spawned = (GameObject)PrefabUtility.InstantiatePrefab(prefab, spawnParent);
                spawned.transform.position = ring[i];
                spawned.name = $"Spawn_{i:00}";
            }
        }

        private static Bounds CalculateNavBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                return new Bounds(Vector3.zero, Vector3.one * 10f);
            }

            var bounds = renderers[0].bounds;
            foreach (var r in renderers)
            {
                bounds.Encapsulate(r.bounds);
            }
            return bounds;
        }

        private static List<Vector3> SampleRing(Bounds bounds, int samples, float radius)
        {
            var result = new List<Vector3>(samples);
            float y = bounds.min.y + 0.5f;
            Vector3 center = bounds.center;
            for (int i = 0; i < samples; i++)
            {
                float angle = (Mathf.PI * 2f * i) / samples;
                var pos = new Vector3(center.x + Mathf.Cos(angle) * radius, y, center.z + Mathf.Sin(angle) * radius);
                result.Add(pos);
            }
            return result;
        }

        private static void ApplyThemeMaterials(GameObject root, StackDefinition definition, PrefabCatalogSnapshot snapshot)
        {
            if (!root || definition == null)
            {
                return;
            }

            var themeTex = definition.Layers.theme;
            if (!themeTex)
            {
                return;
            }

            var zones = BuildThemeZones(definition);
            if (zones.Count == 0)
            {
                return;
            }

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                var center = renderer.bounds.center;
                foreach (var zone in zones)
                {
                    if (!zone.bounds.Contains(center))
                    {
                        continue;
                    }

                    if (!snapshot.ThemeMaterials.TryGetValue(zone.theme, out var mats) || mats.Count == 0)
                    {
                        continue;
                    }

                    renderer.sharedMaterial = mats[(renderer.GetInstanceID() & 0x7fffffff) % mats.Count];
                    break;
                }
            }
        }

        private static List<ThemeZone> BuildThemeZones(StackDefinition definition)
        {
            var zones = new List<ThemeZone>();
            var themeTex = definition.Layers.theme;
            if (!themeTex)
            {
                return zones;
            }

            var pixels = themeTex.GetPixels32();
            var groups = new Dictionary<string, List<Vector2Int>>(StringComparer.OrdinalIgnoreCase);
            for (int y = 0; y < themeTex.height; y++)
            {
                for (int x = 0; x < themeTex.width; x++)
                {
                    var col = pixels[y * themeTex.width + x];
                    if (col.a < 200) continue;
                    string hex = ColorToHex(col);
                    if (!definition.themeMap.TryGetValue(hex, out var themeName))
                    {
                        continue;
                    }
                    if (!groups.TryGetValue(themeName, out var list))
                    {
                        list = new List<Vector2Int>();
                        groups.Add(themeName, list);
                    }
                    list.Add(new Vector2Int(x, y));
                }
            }

            float cell = Mathf.Max(0.1f, definition.metersPerPixel);
            foreach (var pair in groups)
            {
                if (pair.Value.Count == 0)
                {
                    continue;
                }

                int minX = pair.Value.Min(v => v.x);
                int maxX = pair.Value.Max(v => v.x);
                int minY = pair.Value.Min(v => v.y);
                int maxY = pair.Value.Max(v => v.y);

                float halfW = definition.Width * cell * 0.5f;
                float halfH = definition.Height * cell * 0.5f;

                Vector3 min = new Vector3(minX * cell - halfW, -5f, halfH - maxY * cell);
                Vector3 max = new Vector3(maxX * cell - halfW + cell, 15f, halfH - minY * cell + cell);
                zones.Add(new ThemeZone
                {
                    theme = pair.Key,
                    bounds = new Bounds((min + max) * 0.5f, max - min)
                });
            }

            return zones;
        }

        private struct ThemeZone
        {
            public string theme;
            public Bounds bounds;
        }

        private static Dictionary<string, List<Material>> BuildMaterialLibrary()
        {
            var result = new Dictionary<string, List<Material>>(StringComparer.OrdinalIgnoreCase);
            var guids = AssetDatabase.FindAssets("t:Material", new[] { "Assets/UberStrike/Materials", "Assets/UberStrike/Themes" });
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (!material)
                {
                    continue;
                }

                string folderName = System.IO.Path.GetDirectoryName(path)?.Split('/')?.LastOrDefault() ?? "Default";
                if (!result.TryGetValue(folderName, out var list))
                {
                    list = new List<Material>();
                    result.Add(folderName, list);
                }
                list.Add(material);
            }
            return result;
        }

        private static IEnumerable<GameObject> LoadPrefabs(string[] folders)
        {
            if (folders == null || folders.Length == 0)
            {
                yield break;
            }

            var guids = AssetDatabase.FindAssets("t:Prefab", folders);
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab)
                {
                    yield return prefab;
                }
            }
        }

        private static MapGameplaySet ResolveGameplaySet()
        {
            var guids = AssetDatabase.FindAssets("t:MapGameplaySet", new[] { "Assets/UberStrike" });
            if (guids == null || guids.Length == 0)
            {
                Debug.LogWarning("[AssetIntegrationSystem] MapGameplaySet not found in Assets/UberStrike.");
                return null;
            }

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var set = AssetDatabase.LoadAssetAtPath<MapGameplaySet>(path);
                if (set)
                {
                    return set;
                }
            }

            return null;
        }

        private static string ColorToHex(Color32 color)
        {
            return $"#{color.r:X2}{color.g:X2}{color.b:X2}";
        }
    }
}
#endif
