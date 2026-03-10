#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UnityAI
{
    [Serializable]
    public class PlacementConstraints
    {
        public List<Vector2> spawnPoints = new();
        public List<Vector2> chokePoints = new();
        public List<Vector2> coverPoints = new();
        public bool[,] walkableMask;
        public float cellSize = 1f;
        public Vector3 origin = Vector3.zero;
        public Texture2D heightmap;

        public bool HasWalkable => walkableMask != null && walkableMask.Length > 0;

        public List<Vector3> SampleWalkable(int sampleStride = 2)
        {
            var result = new List<Vector3>();
            if (!HasWalkable)
            {
                return result;
            }

            int width = walkableMask.GetLength(0);
            int height = walkableMask.GetLength(1);
            float halfW = width * cellSize * 0.5f;
            float halfH = height * cellSize * 0.5f;

            for (int y = 0; y < height; y += Mathf.Max(1, sampleStride))
            {
                for (int x = 0; x < width; x += Mathf.Max(1, sampleStride))
                {
                    if (!walkableMask[x, y])
                    {
                        continue;
                    }
                    float wx = x * cellSize - halfW + cellSize * 0.5f;
                    float wz = halfH - y * cellSize - cellSize * 0.5f;
                    float wy = SampleHeight(x, y);
                    result.Add(origin + new Vector3(wx, wy, wz));
                }
            }

            return result;
        }

        public float SampleHeight(int px, int py)
        {
            if (!heightmap)
            {
                return 0f;
            }
            px = Mathf.Clamp(px, 0, heightmap.width - 1);
            py = Mathf.Clamp(py, 0, heightmap.height - 1);
            var c = heightmap.GetPixel(px, py);
            return c.grayscale;
        }
    }

    [Serializable]
    public class ItemPlacementRule
    {
        public string key;
        public GameObject prefab;
        public int count;
        public float minSpacing;
        public bool preferCover;
        public bool preferExposed;
        public bool preferCenter;
    }

    public static class SimulatedAnnealingPlacer
    {
        private const float DEFAULT_TEMP = 750f;
        private const float DEFAULT_COOLING = 0.96f;
        private const int DEFAULT_ITERATIONS = 4500;

        public static Dictionary<string, List<Vector3>> Optimise(
            PlacementConstraints constraints,
            List<ItemPlacementRule> rules,
            int maxIterations = DEFAULT_ITERATIONS,
            float initialTemperature = DEFAULT_TEMP,
            float coolingRate = DEFAULT_COOLING)
        {
            var result = new Dictionary<string, List<Vector3>>();
            if (constraints == null || rules == null || rules.Count == 0 || !constraints.HasWalkable)
            {
                return result;
            }

            var rand = new System.Random(1337);
            var walkable = constraints.SampleWalkable(2);
            if (walkable.Count == 0)
            {
                return result;
            }

            var current = Initialise(rules, walkable, rand);
            float currentScore = Evaluate(current, constraints, rules);
            var best = Clone(current);
            float bestScore = currentScore;

            float temp = initialTemperature;
            for (int iteration = 0; iteration < maxIterations; iteration++)
            {
                var neighbor = Neighbor(current, constraints, walkable, rules, rand);
                float neighborScore = Evaluate(neighbor, constraints, rules);
                float delta = neighborScore - currentScore;
                bool accept = delta < 0f || UnityEngine.Random.value < Mathf.Exp(-delta / Mathf.Max(0.01f, temp));
                if (accept)
                {
                    current = neighbor;
                    currentScore = neighborScore;
                    if (neighborScore < bestScore)
                    {
                        best = Clone(neighbor);
                        bestScore = neighborScore;
                    }
                }

                temp *= coolingRate;
                if (temp < 0.05f && iteration > 800)
                {
                    break;
                }
            }

            return best;
        }

        private static Dictionary<string, List<Vector3>> Initialise(List<ItemPlacementRule> rules, List<Vector3> candidates, System.Random rand)
        {
            var placement = new Dictionary<string, List<Vector3>>();
            var used = new List<Vector3>();
            foreach (var rule in rules)
            {
                var list = new List<Vector3>();
                int attempts = 0;
                while (list.Count < rule.count && attempts < 1200 && candidates.Count > 0)
                {
                    var p = candidates[rand.Next(candidates.Count)];
                    if (OkSpacing(p, used, rule.minSpacing))
                    {
                        list.Add(p);
                        used.Add(p);
                    }
                    attempts++;
                }
                placement[rule.key] = list;
            }
            return placement;
        }

        private static Dictionary<string, List<Vector3>> Neighbor(
            Dictionary<string, List<Vector3>> current,
            PlacementConstraints constraints,
            List<Vector3> candidates,
            List<ItemPlacementRule> rules,
            System.Random rand)
        {
            var result = Clone(current);
            var movableKeys = result.Where(kvp => kvp.Value.Count > 0).Select(kvp => kvp.Key).ToList();
            if (movableKeys.Count == 0)
            {
                return result;
            }

            string key = movableKeys[rand.Next(movableKeys.Count)];
            var positions = result[key];
            int idx = rand.Next(positions.Count);
            var currentPos = positions[idx];
            var rule = rules.FirstOrDefault(r => r.key == key);
            float minSpacing = rule != null ? rule.minSpacing : 10f;

            var nearby = candidates.Where(c => Vector3.Distance(c, currentPos) < 20f && Vector3.Distance(c, currentPos) > 0.1f).ToList();
            if (nearby.Count == 0)
            {
                nearby = candidates;
            }

            var candidate = nearby[rand.Next(nearby.Count)];
            var other = result.Where(kvp => kvp.Key != key).SelectMany(kvp => kvp.Value).ToList();
            other.AddRange(positions.Where((p, i) => i != idx));
            if (OkSpacing(candidate, other, minSpacing))
            {
                positions[idx] = candidate;
            }
            return result;
        }

        private static float Evaluate(Dictionary<string, List<Vector3>> placement, PlacementConstraints constraints, List<ItemPlacementRule> rules)
        {
            float score = 0f;
            score += SpawnBalance(placement, constraints, rules) * 10f;
            score += RiskReward(placement, constraints, rules) * 5f;
            score += FlowAlignment(placement, constraints) * 3f;
            score += SpacingPenalty(placement, rules) * 7f;
            score += StrategicDepth(placement, constraints) * 4f;
            return score;
        }

        private static float SpawnBalance(Dictionary<string, List<Vector3>> placement, PlacementConstraints constraints, List<ItemPlacementRule> rules)
        {
            if (constraints.spawnPoints == null || constraints.spawnPoints.Count == 0)
            {
                return 0f;
            }

            var advantages = new List<float>();
            foreach (var spawn in constraints.spawnPoints)
            {
                float total = 0f;
                foreach (var kvp in placement)
                {
                    var rule = rules.FirstOrDefault(r => r.key == kvp.Key);
                    float value = rule != null ? Mathf.Max(1f, rule.count) : 1f;
                    if (kvp.Value.Count == 0)
                    {
                        continue;
                    }
                    float minDist = kvp.Value.Min(p => Vector2.Distance(new Vector2(p.x, p.z), spawn));
                    total += value / (minDist + 1f);
                }
                advantages.Add(total);
            }
            return Std(advantages);
        }

        private static float RiskReward(Dictionary<string, List<Vector3>> placement, PlacementConstraints constraints, List<ItemPlacementRule> rules)
        {
            float score = 0f;
            foreach (var kvp in placement)
            {
                var rule = rules.FirstOrDefault(r => r.key == kvp.Key);
                if (rule == null)
                {
                    continue;
                }

                foreach (var pos in kvp.Value)
                {
                    if (rule.preferExposed && constraints.coverPoints.Count > 0)
                    {
                        float cover = constraints.coverPoints.Min(c => Vector2.Distance(c, new Vector2(pos.x, pos.z)));
                        if (cover < 8f)
                        {
                            score += (8f - cover) * 2f;
                        }
                    }
                    if (rule.preferCover && constraints.coverPoints.Count > 0)
                    {
                        float cover = constraints.coverPoints.Min(c => Vector2.Distance(c, new Vector2(pos.x, pos.z)));
                        if (cover > 14f)
                        {
                            score += (cover - 14f);
                        }
                    }

                    if (rule.preferCenter && constraints.walkableMask != null)
                    {
                        float cx = constraints.walkableMask.GetLength(0) * constraints.cellSize * 0.5f;
                        float cz = constraints.walkableMask.GetLength(1) * constraints.cellSize * 0.5f;
                        float distCenter = Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(cx, cz));
                        if (distCenter > 25f)
                        {
                            score += (distCenter - 25f) * 0.5f;
                        }
                    }
                }
            }
            return score;
        }

        private static float FlowAlignment(Dictionary<string, List<Vector3>> placement, PlacementConstraints constraints)
        {
            if (constraints.chokePoints == null || constraints.chokePoints.Count == 0)
            {
                return 0f;
            }
            float score = 0f;
            foreach (var kvp in placement)
            {
                foreach (var pos in kvp.Value)
                {
                    float minChoke = constraints.chokePoints.Min(c => Vector2.Distance(c, new Vector2(pos.x, pos.z)));
                    if (minChoke < 5f)
                    {
                        score += (5f - minChoke) * 3f;
                    }
                    else if (minChoke > 15f)
                    {
                        score += (minChoke - 15f) * 0.5f;
                    }
                }
            }
            return score;
        }

        private static float SpacingPenalty(Dictionary<string, List<Vector3>> placement, List<ItemPlacementRule> rules)
        {
            float score = 0f;
            var all = new List<(Vector3 pos, float spacing)>();
            foreach (var kvp in placement)
            {
                var rule = rules.FirstOrDefault(r => r.key == kvp.Key);
                float minSpacing = rule != null ? rule.minSpacing : 10f;
                foreach (var pos in kvp.Value)
                {
                    foreach (var other in all)
                    {
                        float dist = Vector3.Distance(pos, other.pos);
                        float req = Mathf.Max(minSpacing, other.spacing);
                        if (dist < req)
                        {
                            score += (req - dist) * 5f;
                        }
                    }
                    all.Add((pos, minSpacing));
                }
            }
            return score;
        }

        private static float StrategicDepth(Dictionary<string, List<Vector3>> placement, PlacementConstraints constraints)
        {
            var power = new List<Vector2>();
            foreach (var key in new[] { "weapon_sniper", "weapon_rocket", "armor_heavy" })
            {
                if (placement.TryGetValue(key, out var list))
                {
                    power.AddRange(list.Select(p => new Vector2(p.x, p.z)));
                }
            }
            if (power.Count == 0)
            {
                return 0f;
            }

            float cx = constraints.walkableMask.GetLength(0) * constraints.cellSize * 0.5f;
            float cz = constraints.walkableMask.GetLength(1) * constraints.cellSize * 0.5f;
            var center = new Vector2(cx, cz);
            var bary = Vector2.zero;
            foreach (var p in power)
            {
                bary += p;
            }
            bary /= power.Count;
            float offset = Vector2.Distance(center, bary);
            return Mathf.Max(0f, offset - 30f);
        }

        private static bool OkSpacing(Vector3 point, List<Vector3> others, float minSpacing)
        {
            foreach (var o in others)
            {
                if (Vector3.Distance(point, o) < minSpacing)
                {
                    return false;
                }
            }
            return true;
        }

        private static float Std(List<float> values)
        {
            if (values == null || values.Count == 0)
            {
                return 0f;
            }
            float mean = values.Sum() / values.Count;
            float variance = values.Sum(v => (v - mean) * (v - mean)) / values.Count;
            return Mathf.Sqrt(variance);
        }

        private static Dictionary<string, List<Vector3>> Clone(Dictionary<string, List<Vector3>> src)
        {
            var dst = new Dictionary<string, List<Vector3>>();
            foreach (var kvp in src)
            {
                dst[kvp.Key] = new List<Vector3>(kvp.Value);
            }
            return dst;
        }
    }
}
#endif
