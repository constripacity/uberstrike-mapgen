#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace UnityAI
{
    /// <summary>
    /// Reads authored scenes and turns them into ML-friendly pattern descriptors.
    /// </summary>
    public static class MapPatternExtractor
    {
        [Serializable]
        public class StyleProfile
        {
            public string mapName;
            public string mapType;
            public Vector3[] spawnPositions = Array.Empty<Vector3>();
            public float[] flowMagnitudes = Array.Empty<float>();
            public float avgHeight;
            public float heightStdDev;
            public float[] chokeWidths = Array.Empty<float>();
            public int pickupCount;
            public float navmeshArea;
        }

        [MenuItem("Tools/UberStrike/MapGen/Extract Patterns (Active Scene)", priority = 500)]
        public static void ExtractActiveScene()
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
            {
                Debug.LogWarning("[MapPatternExtractor] No active scene loaded.");
                return;
            }

            var profile = AnalyzeScene(scene);
            ExportProfiles(new[] { profile });
        }

        [MenuItem("Tools/UberStrike/MapGen/Extract Patterns (All Scenes)", priority = 501)]
        public static void ExtractAllScenes()
        {
            var profiles = new List<StyleProfile>();
            var guids = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" });
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                profiles.Add(AnalyzeScene(scene));
            }

            ExportProfiles(profiles);
        }

        public static StyleProfile AnalyzeScene(Scene scene)
        {
            var profile = new StyleProfile
            {
                mapName = scene.name,
                mapType = GuessMapType(scene.name)
            };

            var spawns = GameObject.FindGameObjectsWithTag("SpawnPoint");
            if (spawns.Length == 0)
            {
                spawns = GameObject.FindObjectsOfType<GameObject>().Where(go => go.name.IndexOf("spawn", StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
            }
            profile.spawnPositions = spawns.Select(s => s.transform.position).ToArray();

            var flowMagnitudes = new List<float>();
            var chokeWidths = new List<float>();
            foreach (var pair in EnumerateChokepoints())
            {
                flowMagnitudes.Add(pair.length);
                chokeWidths.Add(pair.width);
            }
            profile.flowMagnitudes = flowMagnitudes.ToArray();
            profile.chokeWidths = chokeWidths.ToArray();

            var navTri = NavMesh.CalculateTriangulation();
            if (navTri.vertices != null && navTri.vertices.Length > 0)
            {
                float area = 0f;
                for (int i = 0; i < navTri.indices.Length; i += 3)
                {
                    var a = navTri.vertices[navTri.indices[i]];
                    var b = navTri.vertices[navTri.indices[i + 1]];
                    var c = navTri.vertices[navTri.indices[i + 2]];
                    area += Vector3.Cross(b - a, c - a).magnitude * 0.5f;
                }
                profile.navmeshArea = area;
                profile.avgHeight = navTri.vertices.Average(v => v.y);
                profile.heightStdDev = Mathf.Sqrt(navTri.vertices.Select(v => Mathf.Pow(v.y - profile.avgHeight, 2f)).Average());
            }

            profile.pickupCount = GameObject.FindObjectsOfType<GameObject>().Count(go => go.name.IndexOf("pickup", StringComparison.OrdinalIgnoreCase) >= 0);

            return profile;
        }

        private static IEnumerable<(float length, float width)> EnumerateChokepoints()
        {
            var colliders = UnityEngine.Object.FindObjectsOfType<BoxCollider>();
            foreach (var collider in colliders)
            {
                if (collider.size.x < 2f || collider.size.z < 2f)
                {
                    continue;
                }

                float width = Mathf.Min(collider.size.x, collider.size.z);
                float length = Mathf.Max(collider.size.x, collider.size.z);
                yield return (length, width);
            }
        }

        private static void ExportProfiles(IEnumerable<StyleProfile> profiles)
        {
            var exportDir = "Assets/_Generated/Patterns";
            Directory.CreateDirectory(exportDir);
            string path = Path.Combine(exportDir, $"patterns_{DateTime.Now:yyyyMMdd_HHmmss}.json");
            var json = JsonUtility.ToJson(new Wrapper { profiles = profiles.ToArray() }, true);
            File.WriteAllText(path, json);
            AssetDatabase.Refresh();
            Debug.Log($"[MapPatternExtractor] Exported profiles to {path}");
        }

        [Serializable]
        private class Wrapper
        {
            public StyleProfile[] profiles;
        }

        private static string GuessMapType(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName))
            {
                return "Arena";
            }

            sceneName = sceneName.ToLowerInvariant();
            if (sceneName.Contains("ctf")) return "CTF";
            if (sceneName.Contains("dm") || sceneName.Contains("death")) return "Deathmatch";
            if (sceneName.Contains("arena")) return "Arena";
            return "Arena";
        }
    }
}
#endif
