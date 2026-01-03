#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;

namespace UnityAI
{
    /// <summary>
    /// Batch entry point invoked from CLI (Unity -batchmode -executeMethod UnityAI.BatchProcessor.GenerateMap ...).
    /// </summary>
    public static class BatchProcessor
    {
        [MenuItem("Tools/UberStrike/MapGen/Batch Generate (Test)")]
        public static void MenuGenerate()
        {
            GenerateMap();
        }

        public static void GenerateMap()
        {
            var args = Environment.GetCommandLineArgs();
            string blueprintArg = GetArg(args, "-blueprint", string.Empty);
            string outputScene = GetArg(args, "-output", string.Empty);
            float metersPerPixel = ParseFloat(GetArg(args, "-mpp", "1"), 1f);

            if (string.IsNullOrEmpty(blueprintArg))
            {
                Debug.LogError("[BatchProcessor] Missing -blueprint argument.");
                EditorApplication.Exit(1);
                return;
            }

            if (!File.Exists(blueprintArg))
            {
                Debug.LogWarning($"[BatchProcessor] Blueprint not found at '{blueprintArg}', attempting Assets relative path.");
                var alt = blueprintArg.Replace(Application.dataPath, "Assets");
                if (File.Exists(alt))
                {
                    blueprintArg = alt;
                }
            }

            try
            {
                BuildFromBlueprint.BuildFromPNG(blueprintArg, metersPerPixel, !string.IsNullOrEmpty(outputScene), out var scenePath);

                if (!string.IsNullOrEmpty(outputScene) && !string.IsNullOrEmpty(scenePath))
                {
                    var dir = Path.GetDirectoryName(outputScene);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    FileUtil.ReplaceFile(scenePath, outputScene);
                    AssetDatabase.Refresh();
                    Debug.Log($"[BatchProcessor] Saved scene to {outputScene}");
                }

                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BatchProcessor] Generation failed: {ex.Message}\n{ex.StackTrace}");
                EditorApplication.Exit(1);
            }
        }

        private static string GetArg(string[] args, string key, string defaultValue)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].StartsWith(key, StringComparison.OrdinalIgnoreCase))
                {
                    if (args[i].Contains("="))
                    {
                        var split = args[i].Split('=');
                        return split.Length > 1 ? split[1] : defaultValue;
                    }

                    if (i + 1 < args.Length)
                    {
                        return args[i + 1];
                    }
                }
            }

            return defaultValue;
        }

        private static float ParseFloat(string value, float fallback)
        {
            return float.TryParse(value, out var parsed) ? parsed : fallback;
        }
    }
}
#endif
