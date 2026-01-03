#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UnityAI
{
    public static class TournamentValidator
    {
        [Serializable]
        public class ValidationResult
        {
            public bool IsValid;
            public List<string> Errors = new List<string>();
            public List<string> Warnings = new List<string>();
            public CompetitiveScore Score;
        }

        [Serializable]
        public struct CompetitiveScore
        {
            public float Overall;
            public float Balance;
            public float Flow;
            public float Performance;
        }

        [MenuItem("Tools/UberStrike/Validate For Tournament", priority = 250)]
        public static void ValidateSelection()
        {
            var active = Selection.activeGameObject;
            if (active == null)
            {
                Debug.LogWarning("[TournamentValidator] Select a map root to validate");
                return;
            }

            var result = ValidateForTournament(active.name, active);
            Debug.Log($"[TournamentValidator] Result: {(result.IsValid ? "VALID" : "INVALID")} (Score {result.Score.Overall:F2})");
            foreach (var error in result.Errors)
                Debug.LogError(error);
            foreach (var warning in result.Warnings)
                Debug.LogWarning(warning);

            SaveReport(active.name, result);
        }

        public static ValidationResult ValidateForTournament(string mapName, GameObject root)
        {
            var result = new ValidationResult
            {
                Score = new CompetitiveScore
                {
                    Balance = 0.5f,
                    Flow = 0.5f,
                    Performance = 0.5f,
                    Overall = 0.5f
                }
            };

            var metrics = AdvancedMetrics.AnalyzeMap(root);
            if (metrics.SpawnSafety < 0.5f)
            {
                result.Errors.Add("Spawn safety below threshold");
            }

            if (metrics.CoverDensity < 0.2f)
            {
                result.Warnings.Add("Low cover density – consider adding cover props");
            }

            if (metrics.PathDiversity < 0.3f)
            {
                result.Errors.Add("Insufficient alternate paths for competitive play");
            }

            // Additional heuristics
            if (metrics.SightlineAverage > 80f)
            {
                result.Warnings.Add("Long average sightlines may favour snipers");
            }

            if (metrics.ChokePointRatio > 0.4f)
            {
                result.Warnings.Add("High choke point ratio – verify pacing manually");
            }

            float balance = metrics.SpawnSafety;
            float flow = (metrics.ConnectivityScore + metrics.PathDiversity) * 0.5f;
            float performance = Mathf.Clamp01(1f - Mathf.Abs(metrics.CoverDensity - 0.3f));

            result.Score = new CompetitiveScore
            {
                Balance = balance,
                Flow = flow,
                Performance = performance,
                Overall = Mathf.Clamp01((balance + flow + performance) / 3f)
            };

            result.IsValid = result.Errors.Count == 0 && result.Score.Overall >= 0.7f;

            return result;
        }

        public static void GenerateTournamentReport(string mapName, GameObject root)
        {
            var result = ValidateForTournament(mapName, root);
            SaveReport(mapName, result);
        }

        private static void SaveReport(string mapName, ValidationResult result)
        {
            string folder = Path.Combine(Application.dataPath, "_UberStrike/TournamentReports");
            Directory.CreateDirectory(folder);

            string path = Path.Combine(folder, $"{mapName}_tournament_report.json");
            File.WriteAllText(path, JsonUtility.ToJson(result, true));
            AssetDatabase.Refresh();
        }
    }
}
#endif
