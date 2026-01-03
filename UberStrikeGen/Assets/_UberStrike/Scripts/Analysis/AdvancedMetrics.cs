#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace UnityAI
{
    public static class AdvancedMetrics
    {
        [Serializable]
        public class MapMetrics
        {
            public float ConnectivityScore;
            public float VerticalityIndex;
            public float CoverDensity;
            public float SightlineAverage;
            public float ChokePointRatio;
            public float SpawnSafety;
            public float PickupBalance;
            public float PathDiversity;
        }

        public static MapMetrics AnalyzeMap(GameObject root)
        {
            if (root == null)
                throw new ArgumentNullException(nameof(root));

            var metrics = new MapMetrics();
            var navMesh = NavMesh.CalculateTriangulation();

            metrics.ConnectivityScore = ComputeConnectivity(navMesh);
            metrics.VerticalityIndex = ComputeVerticality(root);
            metrics.CoverDensity = ComputeCoverDensity(root);
            metrics.SightlineAverage = ComputeSightLines(root);
            metrics.ChokePointRatio = ComputeChokePointRatio(navMesh);
            metrics.SpawnSafety = ComputeSpawnSafety(root);
            metrics.PickupBalance = ComputePickupBalance(root);
            metrics.PathDiversity = ComputePathDiversity(navMesh);

            return metrics;
        }

        public static void ExportHeatmaps(string mapName)
        {
            if (string.IsNullOrEmpty(mapName))
                mapName = "Unnamed";

            string folder = Path.Combine(Application.dataPath, "_UberStrike/Analysis", mapName);
            Directory.CreateDirectory(folder);

            var traffic = GenerateNoiseHeatmap(Color.blue, Color.red);
            File.WriteAllBytes(Path.Combine(folder, "traffic_heatmap.png"), traffic.EncodeToPNG());

            var combat = GenerateNoiseHeatmap(Color.black, Color.yellow);
            File.WriteAllBytes(Path.Combine(folder, "combat_heatmap.png"), combat.EncodeToPNG());

            var camping = GenerateNoiseHeatmap(Color.black, Color.magenta);
            File.WriteAllBytes(Path.Combine(folder, "camping_heatmap.png"), camping.EncodeToPNG());

            AssetDatabase.Refresh();
        }

        public static List<string> GenerateRecommendations(MapMetrics metrics)
        {
            var recommendations = new List<string>();

            if (metrics.ConnectivityScore < 0.6f)
                recommendations.Add("Add additional pathways to increase connectivity.");
            if (metrics.VerticalityIndex < 0.3f)
                recommendations.Add("Introduce more vertical gameplay elements like ramps or lifts.");
            if (metrics.SpawnSafety < 0.5f)
                recommendations.Add("Improve spawn cover or separation to reduce spawn vulnerability.");
            if (metrics.CoverDensity < 0.2f)
                recommendations.Add("Place additional cover props in large open areas.");
            if (metrics.PathDiversity < 0.4f)
                recommendations.Add("Create alternative routes between major objectives.");

            return recommendations;
        }

        private static float ComputeConnectivity(NavMeshTriangulation navMesh)
        {
            if (navMesh.vertices == null || navMesh.vertices.Length == 0)
                return 0f;

            var graph = new Dictionary<int, HashSet<int>>();
            for (int i = 0; i < navMesh.indices.Length; i += 3)
            {
                int a = navMesh.indices[i];
                int b = navMesh.indices[i + 1];
                int c = navMesh.indices[i + 2];

                AddEdge(graph, a, b);
                AddEdge(graph, b, c);
                AddEdge(graph, c, a);
            }

            var visited = new HashSet<int>();
            void Traverse(int node)
            {
                var stack = new Stack<int>();
                stack.Push(node);
                while (stack.Count > 0)
                {
                    int current = stack.Pop();
                    if (!visited.Add(current))
                        continue;

                    if (!graph.TryGetValue(current, out var neighbours))
                        continue;

                    foreach (int neighbour in neighbours)
                    {
                        if (!visited.Contains(neighbour))
                            stack.Push(neighbour);
                    }
                }
            }

            int components = 0;
            foreach (var kv in graph)
            {
                if (visited.Contains(kv.Key))
                    continue;

                Traverse(kv.Key);
                components++;
            }

            return Mathf.Clamp01(1f / Mathf.Max(1, components));
        }

        private static float ComputeVerticality(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<MeshRenderer>();
            if (renderers.Length == 0)
                return 0f;

            float min = float.MaxValue;
            float max = float.MinValue;
            foreach (var renderer in renderers)
            {
                min = Mathf.Min(min, renderer.bounds.min.y);
                max = Mathf.Max(max, renderer.bounds.max.y);
            }

            float heightRange = max - min;
            return Mathf.Clamp01(heightRange / 20f);
        }

        private static float ComputeCoverDensity(GameObject root)
        {
            int coverCount = 0;
            foreach (var go in root.GetComponentsInChildren<Transform>())
            {
                if (go.name.IndexOf("cover", StringComparison.OrdinalIgnoreCase) >= 0)
                    coverCount++;
            }

            float area = Mathf.Max(1f, EstimateWalkableArea(root));
            return Mathf.Clamp01(coverCount / (area / 100f));
        }

        private static float ComputeSightLines(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<MeshRenderer>();
            if (renderers.Length == 0)
                return 0f;

            float total = 0f;
            int samples = Mathf.Min(200, renderers.Length);
            for (int i = 0; i < samples; i++)
            {
                var renderer = renderers[i];
                Vector3 origin = renderer.bounds.center + Vector3.up * 1.5f;
                if (Physics.Raycast(origin, renderer.transform.forward, out RaycastHit hit, 100f))
                    total += hit.distance;
                else
                    total += 100f;
            }

            return total / Mathf.Max(1, samples);
        }

        private static float ComputeChokePointRatio(NavMeshTriangulation navMesh)
        {
            if (navMesh.vertices == null || navMesh.vertices.Length == 0)
                return 0f;

            // Estimate chokepoints by counting triangles with thin edges
            int narrow = 0;
            int total = navMesh.indices.Length / 3;
            for (int i = 0; i < navMesh.indices.Length; i += 3)
            {
                Vector3 a = navMesh.vertices[navMesh.indices[i]];
                Vector3 b = navMesh.vertices[navMesh.indices[i + 1]];
                Vector3 c = navMesh.vertices[navMesh.indices[i + 2]];

                float ab = Vector3.Distance(a, b);
                float bc = Vector3.Distance(b, c);
                float ca = Vector3.Distance(c, a);
                float minEdge = Mathf.Min(ab, Mathf.Min(bc, ca));
                if (minEdge < 1.5f)
                    narrow++;
            }

            return Mathf.Clamp01(total > 0 ? narrow / (float)total : 0f);
        }

        private static float ComputeSpawnSafety(GameObject root)
        {
            var spawns = new List<Transform>();
            foreach (var spawn in GameObject.FindGameObjectsWithTag("Spawn"))
                spawns.Add(spawn.transform);

            if (spawns.Count == 0)
                return 0.5f;

            float total = 0f;
            foreach (var spawn in spawns)
            {
                float coverDistance = Physics.SphereCast(spawn.position + Vector3.up, 1f, Vector3.forward, out _, 3f)
                    ? 0.2f : 1f;
                total += coverDistance;
            }

            return Mathf.Clamp01(total / spawns.Count);
        }

        private static float ComputePickupBalance(GameObject root)
        {
            int health = 0;
            int armor = 0;
            foreach (var go in root.GetComponentsInChildren<Transform>())
            {
                string lower = go.name.ToLowerInvariant();
                if (lower.Contains("health"))
                    health++;
                if (lower.Contains("armor"))
                    armor++;
            }

            if (health + armor == 0)
                return 0.5f;

            return 1f - Mathf.Abs(health - armor) / Mathf.Max(1f, health + armor);
        }

        private static float ComputePathDiversity(NavMeshTriangulation navMesh)
        {
            if (navMesh.vertices == null || navMesh.vertices.Length == 0)
                return 0f;

            // approximate as ratio of navmesh area to bounding area
            Bounds bounds = new Bounds(navMesh.vertices[0], Vector3.zero);
            foreach (var vertex in navMesh.vertices)
                bounds.Encapsulate(vertex);

            float navArea = 0f;
            for (int i = 0; i < navMesh.indices.Length; i += 3)
            {
                Vector3 a = navMesh.vertices[navMesh.indices[i]];
                Vector3 b = navMesh.vertices[navMesh.indices[i + 1]];
                Vector3 c = navMesh.vertices[navMesh.indices[i + 2]];
                navArea += Vector3.Cross(b - a, c - a).magnitude * 0.5f;
            }

            float boundArea = bounds.size.x * bounds.size.z;
            return Mathf.Clamp01(navArea / Mathf.Max(1f, boundArea));
        }

        private static float EstimateWalkableArea(GameObject root)
        {
            float area = 0f;
            foreach (var collider in root.GetComponentsInChildren<MeshCollider>())
            {
                area += collider.sharedMesh != null ? collider.sharedMesh.bounds.size.x * collider.sharedMesh.bounds.size.z : 0f;
            }

            return area;
        }

        private static Texture2D GenerateNoiseHeatmap(Color start, Color end)
        {
            int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point
            };

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float n = Mathf.PerlinNoise(x / 32f, y / 32f);
                    tex.SetPixel(x, y, Color.Lerp(start, end, n));
                }
            }

            tex.Apply();
            return tex;
        }

        private static void AddEdge(Dictionary<int, HashSet<int>> graph, int a, int b)
        {
            if (!graph.TryGetValue(a, out var setA))
            {
                setA = new HashSet<int>();
                graph[a] = setA;
            }

            if (!graph.TryGetValue(b, out var setB))
            {
                setB = new HashSet<int>();
                graph[b] = setB;
            }

            setA.Add(b);
            setB.Add(a);
        }
    }
}
#endif
