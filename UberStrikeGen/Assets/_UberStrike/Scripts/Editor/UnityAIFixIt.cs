using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

public class UnityAIFixIt : EditorWindow
{
    const string AgentUrl = "http://127.0.0.1:11435/fix";

    [MenuItem("Tools/UnityAI/Auto-Fix Last Errors")]
    public static void OpenAndRun()
    {
        // Read Unity Editor.log (latest session tail)
        string log = GetEditorLogTail();

        // --- NEW GUARD: Check if log reading failed (missing or locked) ---
        if (string.IsNullOrEmpty(log))
            Debug.LogWarning("[UnityAIFixIt] Editor.log not readable; sending empty error list.");
        // --- END NEW GUARD ---

        var errors = ExtractErrors(log);

        // Collect likely files: all .cs under _UberStrike and any files mentioned in errors
        string projectRoot = Directory.GetParent(Application.dataPath).FullName.Replace("\\", "/");
        var files = new List<FileCtx>();
        var codePaths = new HashSet<string>();

        foreach (var line in errors)
        {
            // naive path scrape
            int idx = line.IndexOf("Assets/");
            if (idx >= 0)
            {
                string p = line.Substring(idx).Split(' ', '\t', ':', ')', '(')[0];
                codePaths.Add(p);
            }
        }
        // Fallback: include all editor scripts if nothing was found
        if (codePaths.Count == 0)
        {
            foreach (var p in Directory.GetFiles(Path.Combine(projectRoot, "Assets"), "*.cs", SearchOption.AllDirectories))
            {
                if (p.Contains("\\Editor\\") || p.Contains("/Editor/"))
                    codePaths.Add(p.Substring(projectRoot.Length + 1).Replace("\\", "/"));
            }
        }

        // Limit to max ~12 files for prompt size
        foreach (var rel in codePaths.Take(12))
        {
            var full = Path.Combine(projectRoot, rel.Replace("/", "\\"));
            if (!File.Exists(full)) continue;
            files.Add(new FileCtx { path = rel, content = File.ReadAllText(full, Encoding.UTF8) });
        }

        var payload = new FixRequest { errors = errors.Take(50).ToList(), files = files, project_root = projectRoot };

        string json = JsonUtility.ToJson(payload, prettyPrint: true);
        var req = new UnityWebRequest(AgentUrl, "POST");
        byte[] body = Encoding.UTF8.GetBytes(json);
        req.uploadHandler = new UploadHandlerRaw(body);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");

        var op = req.SendWebRequest();
        while (!op.isDone) { }
        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("[UnityAIFixIt] HTTP error: " + req.error);
        }
        else
        {
            Debug.Log("[UnityAIFixIt] " + req.downloadHandler.text);
            AssetDatabase.Refresh();
        }
    }

    // --- REPLACED GetEditorLogTail for safe reading ---
    static string GetEditorLogTail()
    {
        // Try both common locations
        string p1 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                 @"Unity\Editor\Editor.log");
        string p2 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                 @"Unity\Editor\Editor.log");

        foreach (var path in new[] { p1, p2 })
        {
            if (!File.Exists(path)) continue;

            // 1) Try shared-read (Editor keeps the file open)
            try
            {
                // FileStream with FileShare.ReadWrite allows other processes (like the editor) to keep the file open.
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: false))
                {
                    string all = sr.ReadToEnd();
                    // return last ~2MB
                    if (all.Length > 2_000_000) return all.Substring(all.Length - 2_000_000);
                    return all;
                }
            }
            catch
            {
                // 2) Fallback: copy to temp and read
                try
                {
                    string tmp = Path.GetTempFileName();
                    File.Copy(path, tmp, true);
                    string all = File.ReadAllText(tmp, Encoding.UTF8);
                    File.Delete(tmp);
                    if (all.Length > 2_000_000) return all.Substring(all.Length - 2_000_000);
                    return all;
                }
                catch { /* ignore and try next path */ }
            }
        }
        return ""; // no log found
    }
    // --- END REPLACED GetEditorLogTail ---

    static List<string> ExtractErrors(string log)
    {
        var lines = new List<string>();
        foreach (var l in log.Split('\n'))
        {
            if (l.Contains(" error CS") || l.StartsWith("Assertion failed") || l.Contains("Exception:"))
                lines.Add(l.Trim());
        }
        return lines;
    }


    [Serializable]
    class FileCtx { public string path; public string content; }
    [Serializable]
    class FixRequest { public List<string> errors; public List<FileCtx> files; public string project_root; }
}
