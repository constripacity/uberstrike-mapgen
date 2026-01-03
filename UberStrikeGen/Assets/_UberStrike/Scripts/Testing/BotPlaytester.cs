#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace UnityAI
{
    /// <summary>
    /// Automated NavMesh based play-test that samples spawn points and logs
    /// navigation behaviour for quick balance assessments.
    /// </summary>
    public class BotPlaytester : MonoBehaviour
    {
        private const float DefaultDuration = 120f;
        private const int HeatmapResolution = 128;

        private readonly List<NavMeshAgent> _agents = new List<NavMeshAgent>();
        private readonly List<Vector3> _samples = new List<Vector3>();
        private float _duration;
        private float _elapsed;
        private Bounds _playArea;

        public static BotPlaytester StartPlaytest(float duration = DefaultDuration)
        {
            var tester = new GameObject("BotPlaytester").AddComponent<BotPlaytester>();
            tester.Begin(duration);
            return tester;
        }

        private void Begin(float duration)
        {
            _duration = Mathf.Max(10f, duration);
            _elapsed = 0f;
            _playArea = EstimatePlayArea();
            SpawnAgents();
        }

        private void Update()
        {
            if (_agents.Count == 0)
                return;

            _elapsed += Time.deltaTime;
            foreach (var agent in _agents)
            {
                if (!agent.hasPath || agent.remainingDistance < 1f)
                {
                    Vector3 target = RandomNavmeshPoint(_playArea);
                    agent.SetDestination(target);
                }

                _samples.Add(agent.transform.position);
            }

            if (_elapsed >= _duration)
            {
                Finish();
            }
        }

        private void Finish()
        {
            SaveReport();
            foreach (var agent in _agents)
            {
                if (agent != null)
                {
                    DestroyImmediate(agent.gameObject);
                }
            }

            _agents.Clear();
            DestroyImmediate(gameObject);
            Debug.Log("[BotPlaytester] Playtest completed");
        }

        private void SpawnAgents()
        {
            var spawnPoints = FindSpawnPositions();
            int botCount = Mathf.Clamp(spawnPoints.Count, 4, 12);
            if (botCount == 0)
            {
                Debug.LogWarning("[BotPlaytester] No spawn points located – aborting");
                Finish();
                return;
            }

            for (int i = 0; i < botCount; i++)
            {
                Vector3 spawn = spawnPoints[i % spawnPoints.Count];
                if (!NavMesh.SamplePosition(spawn, out NavMeshHit hit, 5f, NavMesh.AllAreas))
                {
                    continue;
                }

                var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                go.name = $"TestBot_{i}";
                go.transform.position = hit.position + Vector3.up * 0.5f;
                var agent = go.AddComponent<NavMeshAgent>();
                agent.speed = UnityEngine.Random.Range(5f, 7f);
                agent.angularSpeed = 720f;
                agent.acceleration = 16f;
                agent.radius = 0.4f;
                agent.height = 2f;
                _agents.Add(agent);
            }
        }

        private static Bounds EstimatePlayArea()
        {
            var renderers = GameObject.FindObjectsOfType<MeshRenderer>();
            if (renderers.Length == 0)
                return new Bounds(Vector3.zero, Vector3.one * 50f);

            Bounds bounds = renderers[0].bounds;
            foreach (var renderer in renderers)
            {
                bounds.Encapsulate(renderer.bounds);
            }

            bounds.Expand(10f);
            return bounds;
        }

        private static List<Vector3> FindSpawnPositions()
        {
            var result = new List<Vector3>();
            foreach (var spawn in GameObject.FindGameObjectsWithTag("Spawn"))
            {
                result.Add(spawn.transform.position);
            }

            // fallback – use spheres placed by quick build pipeline
            foreach (var go in GameObject.FindObjectsOfType<GameObject>())
            {
                if (go.name.StartsWith("Spawn_", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(go.transform.position);
                }
            }

            return result;
        }

        private static Vector3 RandomNavmeshPoint(Bounds area)
        {
            for (int i = 0; i < 20; i++)
            {
                Vector3 candidate = new Vector3(
                    UnityEngine.Random.Range(area.min.x, area.max.x),
                    area.center.y,
                    UnityEngine.Random.Range(area.min.z, area.max.z));

                if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 6f, NavMesh.AllAreas))
                    return hit.position;
            }

            return area.center;
        }

        private void SaveReport()
        {
            string folder = Path.Combine(Application.dataPath, "_UberStrike/Testing/Reports");
            Directory.CreateDirectory(folder);

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string reportPath = Path.Combine(folder, $"PlaytestReport_{timestamp}.json");
            string heatmapPath = Path.Combine(folder, $"PlaytestHeatmap_{timestamp}.png");

            var heatmap = BuildHeatmapTexture(_samples, _playArea);
            File.WriteAllBytes(heatmapPath, heatmap.EncodeToPNG());

            var report = new PlaytestReport
            {
                duration = _duration,
                agents = _agents.Count,
                sampleCount = _samples.Count,
                bounds = new SerializableBounds(_playArea),
                heatmapImage = Path.GetFileName(heatmapPath)
            };

            File.WriteAllText(reportPath, JsonUtility.ToJson(report, true));
            AssetDatabase.Refresh();

            Debug.Log($"[BotPlaytester] Report saved to {reportPath}");
        }

        private static Texture2D BuildHeatmapTexture(List<Vector3> samples, Bounds bounds)
        {
            var tex = new Texture2D(HeatmapResolution, HeatmapResolution, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point
            };

            int[] hits = new int[HeatmapResolution * HeatmapResolution];
            foreach (var pos in samples)
            {
                float nx = Mathf.InverseLerp(bounds.min.x, bounds.max.x, pos.x);
                float nz = Mathf.InverseLerp(bounds.min.z, bounds.max.z, pos.z);
                int x = Mathf.Clamp(Mathf.RoundToInt(nx * (HeatmapResolution - 1)), 0, HeatmapResolution - 1);
                int y = Mathf.Clamp(Mathf.RoundToInt(nz * (HeatmapResolution - 1)), 0, HeatmapResolution - 1);
                hits[y * HeatmapResolution + x]++;
            }

            int max = 1;
            foreach (int value in hits)
                max = Mathf.Max(max, value);

            var colors = new Color32[HeatmapResolution * HeatmapResolution];
            for (int y = 0; y < HeatmapResolution; y++)
            {
                for (int x = 0; x < HeatmapResolution; x++)
                {
                    int value = hits[y * HeatmapResolution + x];
                    float t = value / (float)max;
                    colors[y * HeatmapResolution + x] = Color.Lerp(new Color(0f, 0f, 0.2f), Color.red, Mathf.Pow(t, 0.5f));
                }
            }

            tex.SetPixels32(colors);
            tex.Apply();
            return tex;
        }

        [MenuItem("Tools/UberStrike/Test with Bots", priority = 210)]
        private static void RunMenuPlaytest()
        {
            if (NavMesh.CalculateTriangulation().vertices.Length == 0)
            {
                Debug.LogWarning("[BotPlaytester] NavMesh missing – bake before running playtest");
                return;
            }

            StartPlaytest();
        }

        [Serializable]
        private struct PlaytestReport
        {
            public float duration;
            public int agents;
            public int sampleCount;
            public SerializableBounds bounds;
            public string heatmapImage;
        }

        [Serializable]
        private struct SerializableBounds
        {
            public Vector3 center;
            public Vector3 size;

            public SerializableBounds(Bounds bounds)
            {
                center = bounds.center;
                size = bounds.size;
            }
        }
    }
}
#endif
