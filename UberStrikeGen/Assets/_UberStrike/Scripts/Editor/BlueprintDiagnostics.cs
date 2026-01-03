using UnityEditor;
using UnityEngine;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;

/// <summary>
/// Diagnostics helper to verify each blueprint PNG is unique and readable.
/// Menu: Tools → UberStrike → Diagnostics → Dump Blueprints
/// Logs: path, exists, bytes, SHA1(short), size, sample pixels (TL, Center, BR), AssetDatabase readability.
/// </summary>
public static class BlueprintDiagnostics
{
    private const string MAP_LAYOUTS = "Assets/_UberStrike/Blueprints/MapLayouts";

    [MenuItem("Tools/UberStrike/Diagnostics/Dump Blueprints")]
    public static void DumpBlueprints()
    {
        try
        {
            Directory.CreateDirectory(MAP_LAYOUTS);
            var full = Path.GetFullPath(MAP_LAYOUTS);
            var di = new DirectoryInfo(full);
            var files = di.Exists ? di.GetFiles("*.png", SearchOption.TopDirectoryOnly) : new FileInfo[0];

            if (files.Length == 0)
            {
                Debug.Log($"[BlueprintDiagnostics] No PNG files found in: {MAP_LAYOUTS}");
                return;
            }

            Debug.Log($"[BlueprintDiagnostics] Found {files.Length} blueprints in: {MAP_LAYOUTS}");
            foreach (var f in files)
            {
                string abs = f.FullName;
                string rel = "Assets/_UberStrike/Blueprints/MapLayouts/" + f.Name;
                Debug.Log($"--- Blueprint: {f.Name} ---");
                Debug.Log($"[BlueprintDiagnostics] Path (abs): {abs}");
                Debug.Log($"[BlueprintDiagnostics] Path (asset): {rel}");
                Debug.Log($"[BlueprintDiagnostics] File Exists: {File.Exists(abs)} Size(bytes): {f.Length}");

                // SHA1 short
                try
                {
                    var bytes = File.ReadAllBytes(abs);
                    using (var sha1 = SHA1.Create())
                    {
                        var hash = sha1.ComputeHash(bytes);
                        string shortHash = BitConverter.ToString(hash, 0, Math.Min(6, hash.Length)).Replace("-", "");
                        Debug.Log($"[BlueprintDiagnostics] SHA1(short): {shortHash}");
                    }

                    // Raw load via Texture2D.LoadImage
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    bool loaded = tex.LoadImage(bytes);
                    Debug.Log($"[BlueprintDiagnostics] Raw LoadImage success: {loaded}");
                    if (loaded)
                    {
                        Debug.Log($"[BlueprintDiagnostics] Raw Texture size: {tex.width}x{tex.height} isReadable: {tex.isReadable}");
                        try
                        {
                            Color tl = tex.GetPixel(0, 0);
                            Color center = tex.GetPixel(Mathf.Max(0, tex.width / 2), Mathf.Max(0, tex.height / 2));
                            Color br = tex.GetPixel(Mathf.Max(0, tex.width - 1), Mathf.Max(0, tex.height - 1));
                            Debug.Log($"[BlueprintDiagnostics] Raw Sample - TL: {tl}, Center: {center}, BR: {br}");
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[BlueprintDiagnostics] Raw pixel sampling failed: {ex}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[BlueprintDiagnostics] File read/load failed: {ex}");
                }

                // AssetDatabase load (if imported)
                try
                {
                    var assetTex = AssetDatabase.LoadAssetAtPath<Texture2D>(rel);
                    if (assetTex != null)
                    {
                        Debug.Log($"[BlueprintDiagnostics] AssetDatabase.LoadAssetAtPath succeeded. tex.name={assetTex.name} size={assetTex.width}x{assetTex.height} isReadable={assetTex.isReadable}");
                        try
                        {
                            Color atl = assetTex.GetPixel(0, 0);
                            Color acenter = assetTex.GetPixel(Mathf.Max(0, assetTex.width / 2), Mathf.Max(0, assetTex.height / 2));
                            Color abr = assetTex.GetPixel(Mathf.Max(0, assetTex.width - 1), Mathf.Max(0, assetTex.height - 1));
                            Debug.Log($"[BlueprintDiagnostics] Asset Sample - TL: {atl}, Center: {acenter}, BR: {abr}");
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[BlueprintDiagnostics] Asset pixel sampling failed: {ex}");
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[BlueprintDiagnostics] AssetDatabase.LoadAssetAtPath returned null for {rel}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[BlueprintDiagnostics] AssetDatabase check failed: {ex}");
                }
            }

            Debug.Log("[BlueprintDiagnostics] Dump complete.");
        }
        catch (Exception e)
        {
            Debug.LogError($"[BlueprintDiagnostics] Exception: {e}");
        }
    }
}
