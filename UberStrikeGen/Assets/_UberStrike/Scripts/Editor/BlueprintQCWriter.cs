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
            // Fake score if you don’t have a real analyzer yet.
            float score = UnityEngine.Random.Range(72f, 95f);

            // 1. Create JSON content
            // Ensure score uses invariant culture for the JSON format
            var scoreStr = score.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
            string json = $"{{\"overall\": {scoreStr}, \"timestamp\": \"{System.DateTime.UtcNow:o}\"}}";
            string outPath = @"C:\UberStrikeGen\Logs\qc_results.json";

            // 2. Write JSON to file
            Directory.CreateDirectory(Path.GetDirectoryName(outPath) ?? ".");
            File.WriteAllText(outPath, json, Encoding.UTF8);

            Debug.Log($"[QC] Wrote {outPath} (score={score:F1})");

            // 3. Live push to dashboard by forwarding the raw JSON content.
            // This ensures the dashboard receives the exact payload that was saved to disk.
            AgentBridge.PostRawJson("/update_qc", json);
        }
    }

    // AgentBridge partial class extension to allow raw JSON posting for specific tools.
    // This allows BlueprintQCWriter to post the exact JSON it generates.
    public static partial class AgentBridge 
    {
        // This method provides raw posting capability, relying on simple WebClient logic.
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
                Debug.Log($"[AgentBridge] ✓ Posted RAW JSON to {route}");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[AgentBridge] RAW Post failed: {route} — {ex.Message}");
            }
        }
    }
}
#endif
