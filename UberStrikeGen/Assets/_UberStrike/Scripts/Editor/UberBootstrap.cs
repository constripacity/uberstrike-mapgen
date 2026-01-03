using UnityAI;
using UnityEditor;
using UnityEngine;

public class UberBootstrap : EditorWindow
{
    [MenuItem("Tools/UnityAI/Bootstrap Project")]
    static void Run() { CreateMaterials(); CreateSimplePrefabs(); Debug.Log("✅ UberBootstrap done."); }

    static void CreateMaterials()
    {
        MakeMat("Assets/_UberStrike/Materials/M_Floor.mat", Color.grey, 0f);
        MakeMat("Assets/_UberStrike/Materials/M_Wall.mat", new Color(.15f, .15f, .15f), 0f);
        MakeMat("Assets/_UberStrike/Materials/M_Glass.mat", new Color(0f, 1f, 1f, .2f), 0f, true);
        MakeMat("Assets/_UberStrike/Materials/M_Water.mat", new Color(0f, .3f, 1f, .6f), 0f, true);
        MakeMat("Assets/_UberStrike/Materials/M_JumpPad.mat", Color.green, 2f);
        MakeMat("Assets/_UberStrike/Materials/M_Teleporter.mat", Color.magenta, 2f);
        MakeMat("Assets/_UberStrike/Materials/M_Ramp.mat", new Color(.9f, .9f, .7f), 0f);
        MakeMat("Assets/_UberStrike/Materials/M_Pickup.mat", new Color(1f, .6f, .1f), 1f);
        AssetDatabase.SaveAssets();
    }

    static void CreateSimplePrefabs()
    {
        MakePrefabCylinder("Assets/_UberStrike/Prefabs/Gameplay/PF_JumpPad.prefab", "M_JumpPad", addJump: true);
        MakePrefabCylinder("Assets/_UberStrike/Prefabs/Gameplay/PF_Teleporter.prefab", "M_Teleporter", addTele: true);
        MakePrefabSphere("Assets/_UberStrike/Prefabs/Gameplay/PF_Pickup_Health.prefab", Color.red);
        MakePrefabSphere("Assets/_UberStrike/Prefabs/Gameplay/PF_Pickup_Armor.prefab", new Color(1f, .5f, 0f));
        MakePrefabSphere("Assets/_UberStrike/Prefabs/Gameplay/PF_Pickup_Ammo.prefab", new Color(0f, .68f, .94f));
        var spawn = new GameObject("PF_SpawnPoint");
        PrefabUtility.SaveAsPrefabAsset(spawn, "Assets/_UberStrike/Prefabs/Gameplay/PF_SpawnPoint.prefab");
        GameObject.DestroyImmediate(spawn);
    }

    static void MakeMat(string path, Color c, float emission, bool transparent = false)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (!shader) shader = Shader.Find("Standard");
        var m = new Material(shader);
        m.color = c;
        if (emission > 0f) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Color.white * emission); }
        if (transparent) { m.SetFloat("_Surface", 1); m.renderQueue = 3000; }
        AssetDatabase.CreateAsset(m, path);
    }

    static void MakePrefabCylinder(string path, string matName, bool addTele = false, bool addJump = false)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = System.IO.Path.GetFileNameWithoutExtension(path);
        var r = go.GetComponent<Renderer>();
        foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
            if (m && m.name == matName) { r.sharedMaterial = m; break; }
        if (addTele) go.AddComponent<UberTeleporter>();
        if (addJump) go.AddComponent<UberJumpPad>();
        PrefabUtility.SaveAsPrefabAsset(go, path);
        GameObject.DestroyImmediate(go);
    }

    static void MakePrefabSphere(string path, Color tint)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = System.IO.Path.GetFileNameWithoutExtension(path);
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = new Material(Shader.Find("Standard")) { color = tint };
        go.AddComponent<UberSimplePickup>();
        PrefabUtility.SaveAsPrefabAsset(go, path);
        GameObject.DestroyImmediate(go);
    }
}
