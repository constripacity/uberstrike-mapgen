#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityAI
{
    /// <summary>
    /// Lightweight AI heuristics that arranges gameplay prefabs in a deterministic-yet-interesting manner.
    /// </summary>
    public static class PrefabPlacementAI
    {
        private const float WEAPON_MIN_DISTANCE = 7.5f;
        private const float PICKUP_MIN_DISTANCE = 5f;
        private const float JUMPPAD_MIN_DISTANCE = 12f;

        public static void PlacePrefabs(GameObject root, AssetIntegrationSystem.PrefabCatalogSnapshot catalog, FlowAnalysisResult flow, StackDefinition definition, bool useSimulatedAnnealing = true)
        {
            if (root == null || catalog == null)
            {
                return;
            }

            var gameplayRoot = root.transform.Find("GameplayAuto");
            if (!gameplayRoot)
            {
                gameplayRoot = new GameObject("GameplayAuto").transform;
                gameplayRoot.SetParent(root.transform, false);
            }
            else
            {
                for (int i = gameplayRoot.childCount - 1; i >= 0; i--)
                {
                    UnityEngine.Object.DestroyImmediate(gameplayRoot.GetChild(i).gameObject);
                }
            }

            var candidates = SampleWalkablePoints(definition, root);
            if (candidates.Count == 0)
            {
                candidates = SampleFallbackPoints(root);
            }

            if (useSimulatedAnnealing)
            {
                var constraints = BuildConstraints(definition, root);
                var rules = BuildRules(catalog, flow);
                if (constraints.HasWalkable && rules.Count > 0)
                {
                    var placement = SimulatedAnnealingPlacer.Optimise(constraints, rules);
                    if (placement.Count > 0)
                    {
                        InstantiateOptimised(gameplayRoot, placement, rules);
                        return;
                    }
                }
            }

            PlaceWeapons(gameplayRoot, catalog, flow, candidates);
            PlacePickups(gameplayRoot, catalog, flow, candidates, root);
            PlaceUtilities(gameplayRoot, catalog, flow, candidates);
        }

        private static List<ItemPlacementRule> BuildRules(AssetIntegrationSystem.PrefabCatalogSnapshot catalog, FlowAnalysisResult flow)
        {
            var rules = new List<ItemPlacementRule>();
            if (catalog == null)
            {
                return rules;
            }

            GameObject Pick(Func<GameObject, bool> pred, IEnumerable<GameObject> set) => set.FirstOrDefault(pred) ?? set.FirstOrDefault();

            if (catalog.Weapons.Count > 0)
            {
                rules.Add(new ItemPlacementRule { key = "weapon_sniper", prefab = Pick(p => p.name.IndexOf("sniper", StringComparison.OrdinalIgnoreCase) >= 0, catalog.Weapons), count = 1, minSpacing = 35f, preferExposed = true });
                rules.Add(new ItemPlacementRule { key = "weapon_rocket", prefab = Pick(p => p.name.IndexOf("rocket", StringComparison.OrdinalIgnoreCase) >= 0, catalog.Weapons), count = 1, minSpacing = 30f, preferExposed = true, preferCenter = true });
                rules.Add(new ItemPlacementRule { key = "weapon_shotgun", prefab = Pick(p => p.name.IndexOf("shot", StringComparison.OrdinalIgnoreCase) >= 0, catalog.Weapons), count = Mathf.Clamp(flow.spawnCount, 1, 3), minSpacing = 18f, preferCover = true });
            }

            if (catalog.GameplaySet != null)
            {
                if (catalog.GameplaySet.PickupArmor)
                {
                    rules.Add(new ItemPlacementRule { key = "armor_light", prefab = catalog.GameplaySet.PickupArmor, count = 3, minSpacing = 14f, preferExposed = false });
                }
                if (catalog.GameplaySet.PickupHealth)
                {
                    rules.Add(new ItemPlacementRule { key = "health_small", prefab = catalog.GameplaySet.PickupHealth, count = 5, minSpacing = 10f, preferCover = true });
                }
                if (catalog.GameplaySet.Teleporter)
                {
                    rules.Add(new ItemPlacementRule { key = "teleporter", prefab = catalog.GameplaySet.Teleporter, count = Mathf.Max(2, flow.spawnCount / 3), minSpacing = 20f, preferCenter = true });
                }
            }

            return rules.Where(r => r.prefab != null && r.count > 0).ToList();
        }

        private static PlacementConstraints BuildConstraints(StackDefinition definition, GameObject root)
        {
            var constraints = new PlacementConstraints
            {
                cellSize = definition != null ? Mathf.Max(0.25f, definition.metersPerPixel) : 1f,
                origin = root ? root.transform.position : Vector3.zero,
                heightmap = definition?.Layers.height
            };

            if (definition?.Layers.layout)
            {
                var layout = definition.Layers.layout;
                constraints.walkableMask = new bool[layout.width, layout.height];
                var pixels = layout.GetPixels32();
                for (int y = 0; y < layout.height; y++)
                {
                    for (int x = 0; x < layout.width; x++)
                    {
                        constraints.walkableMask[x, y] = IsWalkable(pixels[y * layout.width + x]);
                    }
                }
            }

            if (definition?.Layers.flow)
            {
                var flowTex = definition.Layers.flow;
                var flowPixels = flowTex.GetPixels32();
                var config = definition.flow ?? new StackDefinition.FlowColorConfig();
                for (int y = 0; y < flowTex.height; y++)
                {
                    for (int x = 0; x < flowTex.width; x++)
                    {
                        var c = flowPixels[y * flowTex.width + x];
                        var world = PixelToWorld(definition, root, x, y);
                        if (IsColorMatch(c, config.chokeColor))
                        {
                            constraints.chokePoints.Add(new Vector2(world.x, world.z));
                        }
                        else if (IsColorMatch(c, config.spawnColorYellow) || IsColorMatch(c, config.spawnColorRed) || IsColorMatch(c, config.spawnColorGreen))
                        {
                            constraints.spawnPoints.Add(new Vector2(world.x, world.z));
                        }
                    }
                }
            }
            else if (root)
            {
                var spawns = FindSpawnPositions(root.transform);
                constraints.spawnPoints.AddRange(spawns.Select(p => new Vector2(p.x, p.z)));
            }

            if (definition?.Layers.layout)
            {
                var layout = definition.Layers.layout;
                var pixels = layout.GetPixels32();
                float cell = Mathf.Max(0.25f, definition.metersPerPixel);
                float halfW = layout.width * cell * 0.5f;
                float halfH = layout.height * cell * 0.5f;
                for (int y = 0; y < layout.height; y++)
                {
                    for (int x = 0; x < layout.width; x++)
                    {
                        var col = pixels[y * layout.width + x];
                        if (!IsWalkable(col))
                        {
                            float wx = x * cell - halfW + cell * 0.5f;
                            float wz = halfH - y * cell - cell * 0.5f;
                            constraints.coverPoints.Add(new Vector2(wx + constraints.origin.x, wz + constraints.origin.z));
                        }
                    }
                }
            }

            return constraints;
        }

        private static Vector3 PixelToWorld(StackDefinition definition, GameObject root, int px, int py)
        {
            float cell = Mathf.Max(0.25f, definition.metersPerPixel);
            float halfW = definition.Width * cell * 0.5f;
            float halfH = definition.Height * cell * 0.5f;
            var origin = root ? root.transform.position : Vector3.zero;
            return origin + new Vector3(px * cell - halfW + cell * 0.5f, 0f, halfH - py * cell - cell * 0.5f);
        }

        private static void InstantiateOptimised(Transform parent, Dictionary<string, List<Vector3>> placement, List<ItemPlacementRule> rules)
        {
            foreach (var entry in placement)
            {
                var rule = rules.FirstOrDefault(r => r.key == entry.Key);
                if (rule == null || !rule.prefab)
                {
                    continue;
                }

                for (int i = 0; i < entry.Value.Count; i++)
                {
                    var pos = entry.Value[i];
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(rule.prefab, parent);
                    go.transform.position = pos;
                    go.name = $"{entry.Key}_{i:00}";
                }
            }
        }

        private static void PlaceWeapons(Transform parent, AssetIntegrationSystem.PrefabCatalogSnapshot catalog, FlowAnalysisResult flow, List<Vector3> candidates)
        {
            if (catalog.Weapons.Count == 0 || candidates.Count == 0)
            {
                return;
            }

            int heavyWeaponSlots = Mathf.Clamp(flow.chokePixels / 4, 2, 6);
            var weaponPoints = SelectPoints(candidates, p => EvaluateWeaponScore(p, parent, flow), heavyWeaponSlots, WEAPON_MIN_DISTANCE);
            for (int i = 0; i < weaponPoints.Count && i < catalog.Weapons.Count; i++)
            {
                var prefab = catalog.Weapons[i];
                if (!prefab) continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                go.transform.position = weaponPoints[i];
                go.transform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(-weaponPoints[i], Vector3.up));
                go.name = $"Weapon_{prefab.name}_{i:00}";
            }
        }

        private static float EvaluateWeaponScore(Vector3 point, Transform parent, FlowAnalysisResult flow)
        {
            var center = parent.root.position;
            float distance = Vector3.Distance(point, center);
            float spawnBonus = Mathf.Clamp01(flow.minSpawnDistance / 10f);
            return distance * 0.7f + spawnBonus;
        }

        private static void PlacePickups(Transform parent, AssetIntegrationSystem.PrefabCatalogSnapshot catalog, FlowAnalysisResult flow, List<Vector3> candidates, GameObject root)
        {
            var gameplay = catalog.GameplaySet;
            if (gameplay == null || candidates.Count == 0)
            {
                return;
            }

            var spawnPoints = FindSpawnPositions(root.transform);
            float spawnRadius = spawnPoints.Count > 0 ?  Mathf.Max(4f, spawnPoints.Min(p => Vector3.Distance(p, root.transform.position))) : 8f;

            if (gameplay.PickupHealth)
            {
                var healthPoints = SelectPoints(candidates, p => -ClosestDistance(p, spawnPoints), 4, PICKUP_MIN_DISTANCE);
                foreach (var hp in healthPoints)
                {
                    CreateGameplayInstance(gameplay.PickupHealth, parent, hp, "Pickup_Health_Auto");
                }
            }

            if (gameplay.PickupArmor)
            {
                var armorPoints = SelectPoints(candidates, p => ClosestDistance(p, spawnPoints) + spawnRadius, 3, PICKUP_MIN_DISTANCE);
                foreach (var ap in armorPoints)
                {
                    CreateGameplayInstance(gameplay.PickupArmor, parent, ap, "Pickup_Armor_Auto");
                }
            }

            if (gameplay.Teleporter)
            {
                var telePoints = SelectPoints(candidates, p => Mathf.PerlinNoise(p.x * 0.2f, p.z * 0.2f), Mathf.Max(1, flow.spawnCount / 4) * 2, 10f);
                for (int i = 0; i + 1 < telePoints.Count; i += 2)
                {
                    var a = CreateGameplayInstance(gameplay.Teleporter, parent, telePoints[i], $"Teleporter_{i:00}");
                    var b = CreateGameplayInstance(gameplay.Teleporter, parent, telePoints[i + 1], $"Teleporter_{i + 1:00}");
                    LinkTeleporters(a, b);
                }
            }
        }

        private static void PlaceUtilities(Transform parent, AssetIntegrationSystem.PrefabCatalogSnapshot catalog, FlowAnalysisResult flow, List<Vector3> candidates)
        {
            if (catalog.UtilityPrefabs.Count == 0 || candidates.Count == 0)
            {
                return;
            }

            var jumpPad = catalog.UtilityPrefabs.FirstOrDefault(p => p && p.name.IndexOf("jump", StringComparison.OrdinalIgnoreCase) >= 0);
            if (jumpPad)
            {
                int pads = Mathf.Clamp(flow.chokePixels / 6, 1, 4);
                var padPoints = SelectPoints(candidates, p => Mathf.Abs(p.x) + Mathf.Abs(p.z), pads, JUMPPAD_MIN_DISTANCE);
                foreach (var pad in padPoints)
                {
                    CreateGameplayInstance(jumpPad, parent, pad, "JumpPad_Auto");
                }
            }
        }

        private static GameObject CreateGameplayInstance(GameObject prefab, Transform parent, Vector3 position, string label)
        {
            if (!prefab) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.transform.position = position;
            go.name = label;
            return go;
        }

        private static List<Vector3> SampleWalkablePoints(StackDefinition definition, GameObject root)
        {
            var points = new List<Vector3>();
            if (definition == null || definition.Layers.layout == null)
            {
                return points;
            }

            var layout = definition.Layers.layout;
            var pixels = layout.GetPixels32();
            float cell = Mathf.Max(0.25f, definition.metersPerPixel);
            float halfW = layout.width * cell * 0.5f;
            float halfH = layout.height * cell * 0.5f;
            for (int y = 0; y < layout.height; y++)
            {
                for (int x = 0; x < layout.width; x++)
                {
                    var col = pixels[y * layout.width + x];
                    if (!IsWalkable(col)) continue;
                    var pos = new Vector3(x * cell - halfW + cell * 0.5f, root.transform.position.y, halfH - y * cell - cell * 0.5f);
                    points.Add(pos);
                }
            }
            return points;
        }

        private static List<Vector3> SampleFallbackPoints(GameObject root)
        {
            var bounds = new Bounds(root.transform.position, Vector3.one * 40f);
            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                bounds = renderers[0].bounds;
                foreach (var r in renderers)
                {
                    bounds.Encapsulate(r.bounds);
                }
            }

            var points = new List<Vector3>();
            int samples = 48;
            var rand = new System.Random(42);
            for (int i = 0; i < samples; i++)
            {
                float rx = (float)rand.NextDouble();
                float rz = (float)rand.NextDouble();
                var pos = new Vector3(Mathf.Lerp(bounds.min.x, bounds.max.x, rx), bounds.center.y, Mathf.Lerp(bounds.min.z, bounds.max.z, rz));
                points.Add(pos);
            }
            return points;
        }

        private static List<Vector3> SelectPoints(List<Vector3> candidates, Func<Vector3, float> scoreFunc, int count, float minDistance)
        {
            var scored = candidates.Select(p => new KeyValuePair<Vector3, float>(p, scoreFunc(p))).OrderByDescending(p => p.Value).ToList();
            var result = new List<Vector3>(count);
            foreach (var entry in scored)
            {
                if (result.Any(r => Vector3.Distance(r, entry.Key) < minDistance))
                {
                    continue;
                }
                result.Add(entry.Key);
                if (result.Count >= count)
                {
                    break;
                }
            }
            return result;
        }

        private static bool IsWalkable(Color32 c)
        {
            return Mathf.Max(c.r, Mathf.Max(c.g, c.b)) > 60 && Mathf.Abs(c.r - c.g) < 40 && Mathf.Abs(c.r - c.b) < 40;
        }

        private static List<Vector3> FindSpawnPositions(Transform root)
        {
            var list = new List<Vector3>();
            if (!root) return list;
            var group = root.Find("Spawns");
            if (!group) return list;
            foreach (Transform child in group)
            {
                list.Add(child.position);
            }
            return list;
        }

        private static float ClosestDistance(Vector3 point, List<Vector3> positions)
        {
            if (positions == null || positions.Count == 0)
            {
                return 0f;
            }

            float min = float.MaxValue;
            foreach (var pos in positions)
            {
                float dist = Vector3.Distance(point, pos);
                if (dist < min) min = dist;
            }
            return min;
        }

        private static bool IsColorMatch(Color32 sample, string hex)
        {
            if (!ColorUtility.TryParseHtmlString(hex, out var target))
            {
                return false;
            }
            return Mathf.Abs(sample.r - target.r) < 8 && Mathf.Abs(sample.g - target.g) < 8 && Mathf.Abs(sample.b - target.b) < 8;
        }

        private static void LinkTeleporters(GameObject a, GameObject b)
        {
            if (!a || !b)
            {
                return;
            }

            var exitA = a.transform.Find("Exit") ?? a.transform;
            var exitB = b.transform.Find("Exit") ?? b.transform;
            exitA.LookAt(exitB.position + Vector3.up);
            exitB.LookAt(exitA.position + Vector3.up);
        }
    }
}
#endif
