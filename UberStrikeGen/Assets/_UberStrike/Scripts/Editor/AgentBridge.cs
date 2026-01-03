#if UNITY_EDITOR
using System;
using System.IO;
using System.Net;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.Globalization; // Needed for InvariantCulture

namespace UberStrike.EditorTools
{
    /// <summary>
    /// Tiny helper that posts JSON to the local agent.
    /// Safe to call even if the agent is down.
    /// NOTE: Must be partial because other files (like BlueprintQCWriter) extend it.
    /// </summary>
    public static partial class AgentBridge 
    {
        // Change if you run the agent on another port/host
        private const string AgentBase = "http://127.0.0.1:11435"; 

        /// <summary>
        /// Escapes a string for use as a JSON string value.
        /// </summary>
        private static string JsonEscape(string s)
        {
            // Escape backslashes and double quotes, and wrap in quotes.
            return "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        /// <summary>
        /// Posts JSON data to the local agent server.
        /// </summary>
        public static void Post(string route, string json)
        {
            try
            {
                var url = AgentBase + route;
                using (var wc = new WebClient())
                {
                    wc.Headers[HttpRequestHeader.ContentType] = "application/json";
                    wc.UploadData(url, "POST", Encoding.UTF8.GetBytes(json));
                }

                // Optional debug
                // Debug.Log($"[AgentBridge] POST {route} OK");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[AgentBridge] POST failed: {route} — {ex.Message}");
            }
        }
        
        public static void NotifyRunStart(string job, int etaSeconds = 0)
        {
            var payload = $"{{\"job\":{JsonEscape(job)},\"eta_sec\":{etaSeconds}}}";
            Post("/run/" + job, payload);
        }

        public static void NotifyRunProgress(string job, double progress01, string note = "")
        {
            var progressStr = progress01.ToString("0.###", CultureInfo.InvariantCulture);
            var payload = $"{{\"job\":{JsonEscape(job)},\"progress\":{progressStr},\"note\":{JsonEscape(note)}}}";
            Post("/run/progress", payload);
        }

        public static void NotifyRunComplete(string job, bool success, string message = "")
        {
            var payload = $"{{\"job\":{JsonEscape(job)},\"success\":{(success ? "true" : "false")},\"message\":{JsonEscape(message)}}}";
            Post("/run/complete", payload);
        }

        /// <summary>
        /// Notify QC results to the local agent.
        /// </summary>
        public static void NotifyQC(float overallScore, string extra = null)
        {
            try
            {
                // Format float with invariant culture to ensure '.' as decimal separator
                var overallStr = overallScore.ToString("F1", CultureInfo.InvariantCulture);
                
                var payload = $"{{\"overall\": {overallStr}, " +
                              $"\"timestamp\": {JsonEscape(System.DateTime.Now.ToString("u"))}" + 
                              (string.IsNullOrEmpty(extra) ? "" : $", \"note\": {JsonEscape(extra)}") +
                              $"}}";

                Post("/update_qc", payload);
                Debug.Log("[AgentBridge] ✓ Posted /update_qc");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[AgentBridge] /update_qc failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Notify the agent when a map scene is saved.
        /// </summary>
        public static void NotifyMapSaved(string scenePath)
        {
            try
            {
                var fi = new FileInfo(scenePath);
                var name = Path.GetFileNameWithoutExtension(scenePath);
                var sizeMB = fi.Exists ? (fi.Length / (1024.0 * 1024.0)) : 0.0;
                var sizeStr = sizeMB.ToString("0.##", CultureInfo.InvariantCulture);
                var ts = System.DateTime.Now.ToString("u"); // Using 'u' for ISO 8601 UTC

                var payload =
                    $"{{\"maps\":[{{\"name\":{JsonEscape(name)},\"path\":{JsonEscape(scenePath)},\"size_mb\":{sizeStr},\"timestamp\":{JsonEscape(ts)}}}]}}";
                
                Post("/update_maps", payload);
                Debug.Log("[AgentBridge] posted /update_maps");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AgentBridge] NotifyMapSaved failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Auto-hook: when any scene is saved under _UberStrike/Maps, notify the agent.
    /// </summary>
    [InitializeOnLoad]
    public class AgentSceneHooks
    {
        static AgentSceneHooks()
        {
            EditorSceneManager.sceneSaved += OnSceneSaved;
        }

        static void OnSceneSaved(UnityEngine.SceneManagement.Scene scene)
        {
            var p = scene.path.Replace('\\', '/');
            if (p.Contains("/_UberStrike/Maps/"))
                AgentBridge.NotifyMapSaved(scene.path);
        }
    }
}
#endif
