#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace UberStrike.EditorTools
{
    /// <summary>
    /// Writes a small QC JSON and pushes it to the local agent (if running).
    /// </summary>
    public static class BlueprintQCWriter
    {
        [MenuItem("Tools/UnityAI/Export QC JSON (Auto)")]
        public static void ExportQCJson()
        {
            float score = UnityEngine.Random.Range(72f, 95f);

            var scoreStr = score.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
            string json = $"{{\"overall\": {scoreStr}, \"timestamp\": \"{System.DateTime.UtcNow:o}\"}}";
            string outPath = @"C:\UberStrikeGen\Logs\qc_results.json";

            Directory.CreateDirectory(Path.GetDirectoryName(outPath) ?? ".");
            File.WriteAllText(outPath, json, Encoding.UTF8);

            Debug.Log($"[QC] Wrote {outPath} (score={score:F1})");

            AgentBridge.PostRawJson("/update_qc", json);
        }
    }

    public static partial class AgentBridge
    {
        public static void PostRawJson(string route, string json)
        {
            try
            {
                const string agentBase = "http://127.0.0.1:11435";
                var url = agentBase + route;
                using (var wc = new System.Net.WebClient())
                {
                    wc.Headers[System.Net.HttpRequestHeader.ContentType] = "application/json";
                    wc.UploadData(url, "POST", System.Text.Encoding.UTF8.GetBytes(json));
                }
                Debug.Log($"[AgentBridge] Posted RAW JSON to {route}");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[AgentBridge] RAW Post failed: {route} — {ex.Message}");
            }
        }
    }
}
#endif
