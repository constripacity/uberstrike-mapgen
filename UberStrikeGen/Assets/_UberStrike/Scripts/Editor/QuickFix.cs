// csharp
#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

public static class QuickFix
{
    private const float TARGET_WALL_HEIGHT = 4.0f; // meters

    [MenuItem("Tools/QUICK FIX/Fix Walls Height")]
    public static void FixWalls()
    {
        var walls = GameObject.Find("Walls_Combined");
        if (walls == null)
        {
            Debug.LogError("[QuickFix] No Walls_Combined found!");
            return;
        }

        var mf = walls.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null)
        {
            Debug.LogError("[QuickFix] Walls_Combined has no MeshFilter/mesh.");
            return;
        }

        // Compute current world-space height from mesh bounds and current scale
        float meshHeight = mf.sharedMesh.bounds.size.y;
        float currentScaleY = walls.transform.localScale.y;
        float currentWorldHeight = meshHeight * currentScaleY;

        if (meshHeight <= 0.0001f || currentWorldHeight <= 0.0001f)
        {
            // Fallback: set a large localScale if mesh bounds are degenerate
            walls.transform.localScale = new Vector3(walls.transform.localScale.x, TARGET_WALL_HEIGHT, walls.transform.localScale.z);
            Debug.Log($"[QuickFix] Mesh had degenerate height. Set localScale.y to {TARGET_WALL_HEIGHT} (fallback).");
            return;
        }

        float requiredScaleFactor = TARGET_WALL_HEIGHT / currentWorldHeight;
        walls.transform.localScale = new Vector3(walls.transform.localScale.x, walls.transform.localScale.y * requiredScaleFactor, walls.transform.localScale.z);

        Debug.Log($"[QuickFix] Scaled Walls_Combined from world-height {currentWorldHeight:F3}m -> {TARGET_WALL_HEIGHT}m (scale factor {requiredScaleFactor:F3}).");
    }

    [MenuItem("Tools/QUICK FIX/Delete All Cyan")]
    public static void DeleteCyan()
    {
        int deleted = 0;
        var renderers = Object.FindObjectsOfType<Renderer>();
        foreach (var renderer in renderers)
        {
            if (renderer == null || renderer.sharedMaterial == null) continue;

            Color c = renderer.sharedMaterial.color;
            // Loose cyan-ish test: high G and B, low R
            if (c.g > 0.8f && c.b > 0.8f && c.r < 0.3f)
            {
                // Destroy the whole GameObject immediately in editor
                Object.DestroyImmediate(renderer.gameObject);
                deleted++;
                Debug.Log($"[QuickFix] Deleted cyan object: {renderer.gameObject.name}");
            }
        }

        Debug.Log($"[QuickFix] Deleted {deleted} cyan objects.");
    }

    [MenuItem("Tools/QUICK FIX/Both Fixes")]
    public static void BothFixes()
    {
        DeleteCyan();
        FixWalls();
        Debug.Log("[QuickFix] DONE - Walls tall, cyan deleted!");
    }
}
#endif
