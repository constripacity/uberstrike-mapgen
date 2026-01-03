using UnityAI;
using UnityEditor;
using UnityEngine;
using System.IO;

public static class CreateGameplayPrefabsFallback
{
    [MenuItem("Tools/UnityAI/Create Gameplay Prefabs (Fallback)")]
    public static void CreateAll()
    {
        string folder = "Assets/_UberStrike/Prefabs/Gameplay";
        Directory.CreateDirectory(folder);

        // HEALTH / ARMOR / AMMO (cube trigger with colored Lit material)
        MakePickup("HealthPickup", new Color(1f, 0.2f, 0.2f), folder);   // red
        MakePickup("ArmorPickup", new Color(1f, 0.6f, 0.1f), folder);   // orange
        MakePickup("AmmoPickup", new Color(0.1f, 0.7f, 1f), folder);   // cyan

        // JumpPad (thin cylinder trigger)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "JumpPad";
            go.transform.localScale = new Vector3(1.2f, 0.15f, 1.2f);
            var col = go.GetComponent<Collider>(); col.isTrigger = true;
            go.GetComponent<MeshRenderer>().sharedMaterial = MakeMat(new Color(0.3f, 1f, 0.3f)); // green
            EnsureComponent<SimpleJumpPad>(go);
            SavePrefab(go, folder);
        }

        // Teleporter (tall cylinder trigger)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "Teleporter";
            go.transform.localScale = new Vector3(1.0f, 1.2f, 1.0f);
            var col = go.GetComponent<Collider>(); col.isTrigger = true;
            go.GetComponent<MeshRenderer>().sharedMaterial = MakeMat(new Color(1f, 0f, 1f)); // magenta
            EnsureComponent<SimpleTeleporter>(go);
            SavePrefab(go, folder);
        }

        // PlayerSpawnPoint (empty)
        {
            var go = new GameObject("PlayerSpawnPoint");
            EnsureComponent<PlayerSpawnPoint>(go);
            SavePrefab(go, folder);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("UnityAI", "Gameplay prefabs created/updated:\n" + folder, "OK");
    }

    // ---------- helpers ----------

    private static void MakePickup(string name, Color color, string folder)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.localScale = new Vector3(0.6f, 0.6f, 0.6f);

        var col = go.GetComponent<Collider>();
        col.isTrigger = true;

        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = MakeMat(color);

        // Attach simple behaviours if present in project
        if (name == "HealthPickup") EnsureComponent<SimpleHealthPickup>(go);
        if (name == "ArmorPickup") EnsureComponent<SimpleArmorPickup>(go);
        if (name == "AmmoPickup") EnsureComponent<SimpleAmmoPickup>(go);

        SavePrefab(go, folder);
    }

    private static Material MakeMat(Color color)
    {
        var shader = TryGetAnyLitShader();
        var mat = new Material(shader);
        mat.color = color;
        return mat;
    }

    // URP / Built-in / HDRP / Unlit fallback
    private static Shader TryGetAnyLitShader()
    {
        string[] candidates = new[]
        {
            "Universal Render Pipeline/Lit", // URP
            "Standard",                      // Built-in
            "HDRP/Lit",                      // HDRP
            "Unlit/Color"                    // ultimate fallback
        };

        foreach (var name in candidates)
        {
            var sh = Shader.Find(name);
            if (sh != null) return sh;
        }

        // As a last resort, Unity always ships this:
        return Shader.Find("Sprites/Default");
    }

    private static T EnsureComponent<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        if (!c) c = go.AddComponent<T>();
        return c;
    }

    private static void SavePrefab(GameObject go, string folder)
    {
        string path = $"{folder}/{go.name}.prefab";
        PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
    }
}
