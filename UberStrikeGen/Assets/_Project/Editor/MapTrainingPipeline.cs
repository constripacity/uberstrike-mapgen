#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityAI
{
    /// <summary>
    /// Exports gameplay relevant metrics from blueprint stacks so that external
    /// machine-learning tooling can learn structural patterns.
    /// </summary>
    public static class MapTrainingPipeline
    {
        private const string DatasetFolder = "Assets/_UberStrike/TrainingData";
        private const string DatasetFileName = "map_dataset.json";
        private const int FeatureResolution = 64;

        [Serializable]
        private class MapDataset
        {
            public List<MapSample> maps = new List<MapSample>();
        }

        [Serializable]
        private class MapSample
        {
            public string name;
            public int width;
            public int height;
            public float wallCoverage;
            public float walkableCoverage;
            public float spawnBalance;
            public float flowScore;
            public float verticality;
            public bool successful;
            public List<int> layoutFeatures;
            public List<Vector2Serializable> spawnPositions = new List<Vector2Serializable>();
        }

        [Serializable]
        private struct Vector2Serializable
        {
            public float x;
            public float y;

            public Vector2Serializable(float x, float y)
            {
                this.x = x;
                this.y = y;
            }
        }

        [MenuItem("Tools/UberStrike/MapGen/Export Training Data", priority = 200)]
        public static void ExportTrainingData()
        {
            string projectStacksPath = Path.Combine(Application.dataPath, "_UberStrike/Blueprints/Stacks");
            if (!Directory.Exists(projectStacksPath))
            {
                Debug.LogWarning("[MapTrainingPipeline] No stacks directory found");
                return;
            }

            Directory.CreateDirectory(Path.Combine(Application.dataPath, "_UberStrike/TrainingData"));
            string datasetPath = Path.Combine(Application.dataPath, "_UberStrike/TrainingData", DatasetFileName);

            var dataset = new MapDataset();
            foreach (var file in Directory.EnumerateFiles(projectStacksPath, "*.stack.json", SearchOption.AllDirectories))
            {
                try
                {
                    var def = StackDefinition.LoadFromJSON(file);
                    if (def == null)
                        continue;

                    var layers = StackLoader.LoadStackLayers(def);
                    if (!StackLoader.ValidateLayers(layers))
                        continue;

                    dataset.maps.Add(AnalyseStack(def, layers));
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[MapTrainingPipeline] Failed to analyse {file}: {ex.Message}");
                }
            }

            string json = JsonUtility.ToJson(dataset, true);
            File.WriteAllText(datasetPath, json);
            AssetDatabase.Refresh();

            Debug.Log($"[MapTrainingPipeline] Exported {dataset.maps.Count} samples to {datasetPath}");
        }

        private static MapSample AnalyseStack(StackDefinition def, Dictionary<string, Texture2D> layers)
        {
            var sample = new MapSample
            {
                name = def.name,
                width = layers["layout"].width,
                height = layers["layout"].height,
                layoutFeatures = new List<int>(FeatureResolution * FeatureResolution)
            };

            Texture2D layout = layers["layout"];
            Texture2D height = layers["height"];
            Texture2D flow = layers["flow"];

            int wallCount = 0;
            int walkableCount = 0;

            for (int y = 0; y < layout.height; y++)
            {
                for (int x = 0; x < layout.width; x++)
                {
                    Color pixel = layout.GetPixel(x, y);
                    if (IsWall(pixel))
                    {
                        wallCount++;
                    }
                    else if (IsWalkable(pixel))
                    {
                        walkableCount++;
                    }
                }
            }

            int total = layout.width * layout.height;
            sample.wallCoverage = total > 0 ? (float)wallCount / total : 0f;
            sample.walkableCoverage = total > 0 ? (float)walkableCount / total : 0f;

            // Down-sample the layout into a coarse binary grid for training.
            float scaleX = layout.width / (float)FeatureResolution;
            float scaleY = layout.height / (float)FeatureResolution;
            for (int fy = 0; fy < FeatureResolution; fy++)
            {
                for (int fx = 0; fx < FeatureResolution; fx++)
                {
                    int sx = Mathf.Clamp(Mathf.RoundToInt((fx + 0.5f) * scaleX), 0, layout.width - 1);
                    int sy = Mathf.Clamp(Mathf.RoundToInt((fy + 0.5f) * scaleY), 0, layout.height - 1);
                    sample.layoutFeatures.Add(IsWall(layout.GetPixel(sx, sy)) ? 1 : 0);
                }
            }

            // Spawn metrics & flow score
            var spawns = ExtractSpawnPositions(flow, def);
            sample.spawnPositions = spawns.Select(p => new Vector2Serializable(p.x, p.y)).ToList();
            sample.spawnBalance = CalculateSpawnBalance(spawns, layout.width, layout.height);
            sample.flowScore = EstimateFlowScore(flow, layout.width, layout.height);

            // Height variance indicates verticality
            sample.verticality = EstimateVerticality(height);

            // Successful by default – downstream tools can override once trained
            sample.successful = sample.walkableCoverage > 0.3f && sample.spawnPositions.Count >= 2;

            return sample;
        }

        private static float EstimateVerticality(Texture2D height)
        {
            if (height == null)
                return 0f;

            float sum = 0f;
            float sumSq = 0f;
            int total = height.width * height.height;

            for (int y = 0; y < height.height; y++)
            {
                for (int x = 0; x < height.width; x++)
                {
                    float value = height.GetPixel(x, y).grayscale;
                    sum += value;
                    sumSq += value * value;
                }
            }

            float mean = total > 0 ? sum / total : 0f;
            float variance = total > 0 ? (sumSq / total) - (mean * mean) : 0f;
            return Mathf.Sqrt(Mathf.Max(variance, 0f));
        }

        private static List<Vector2> ExtractSpawnPositions(Texture2D flow, StackDefinition def)
        {
            var result = new List<Vector2>();
            if (flow == null)
                return result;

            Color yellow = HexToColor(def.flow.spawnColorYellow);
            Color red = HexToColor(def.flow.spawnColorRed);
            Color green = HexToColor(def.flow.spawnColorGreen);

            for (int y = 0; y < flow.height; y++)
            {
                for (int x = 0; x < flow.width; x++)
                {
                    Color pixel = flow.GetPixel(x, y);
                    if (IsColorMatch(pixel, yellow) || IsColorMatch(pixel, red) || IsColorMatch(pixel, green))
                    {
                        result.Add(new Vector2(x, y));
                    }
                }
            }

            return result;
        }

        private static float CalculateSpawnBalance(List<Vector2> spawns, int width, int height)
        {
            if (spawns == null || spawns.Count == 0)
                return 0f;

            Vector2 center = new Vector2(width * 0.5f, height * 0.5f);
            float averageDist = 0f;
            foreach (var spawn in spawns)
            {
                averageDist += Vector2.Distance(spawn, center);
            }

            averageDist /= spawns.Count;
            float maxDist = Vector2.Distance(Vector2.zero, center);
            if (maxDist <= 0.001f)
                return 1f;

            // 1 - normalized variance from evenly distributed circle
            float variance = 0f;
            foreach (var spawn in spawns)
            {
                float dist = Vector2.Distance(spawn, center);
                float normalized = dist / maxDist;
                variance += Mathf.Abs(normalized - (averageDist / maxDist));
            }

            variance /= spawns.Count;
            return Mathf.Clamp01(1f - variance);
        }

        private static float EstimateFlowScore(Texture2D flow, int width, int height)
        {
            if (flow == null)
                return 0f;

            int chokePixels = 0;
            int coverPixels = 0;
            int arrowPixels = 0;
            int total = width * height;

            Color choke = new Color(1f, 0.647f, 0f); // approx orange
            Color cover = new Color(0.5f, 0.5f, 0.5f);
            Color arrow = new Color(0f, 1f, 1f);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Color pixel = flow.GetPixel(x, y);
                    if (IsColorMatch(pixel, choke))
                        chokePixels++;
                    else if (IsColorMatch(pixel, cover))
                        coverPixels++;
                    else if (IsColorMatch(pixel, arrow))
                        arrowPixels++;
                }
            }

            float coverage = (chokePixels + coverPixels + arrowPixels) / Mathf.Max(1f, total);
            // reward having some of each category
            float diversity = 0f;
            if (chokePixels > 0) diversity += 0.33f;
            if (coverPixels > 0) diversity += 0.33f;
            if (arrowPixels > 0) diversity += 0.34f;
            return Mathf.Clamp01(coverage * 0.5f + diversity * 0.5f);
        }

        private static bool IsWall(Color pixel) => pixel.r < 0.2f && pixel.g < 0.2f && pixel.b < 0.2f;
        private static bool IsWalkable(Color pixel) => pixel.grayscale > 0.25f && pixel.grayscale < 0.95f && !IsWall(pixel);

        private static bool IsColorMatch(Color a, Color b)
        {
            return Vector3.Distance(new Vector3(a.r, a.g, a.b), new Vector3(b.r, b.g, b.b)) < 0.1f;
        }

        private static Color HexToColor(string hex)
        {
            if (ColorUtility.TryParseHtmlString(hex, out Color color))
                return color;
            return Color.white;
        }
    }
}
#endif
