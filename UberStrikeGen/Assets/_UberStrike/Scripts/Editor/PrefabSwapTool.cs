using System;
using System.Collections.Generic;
using System.Linq;
using UnityAI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public class PrefabSwapTool : EditorWindow
{
    public GameObject HealthPickup;
    public GameObject ArmorPickup;
    public GameObject AmmoPickup;
    public GameObject JumpPad;
    public GameObject Teleporter;
    public GameObject PlayerSpawn;

    private Transform searchRoot;
    private bool deletePlaceholders = true;
    private bool snapToGround = true;
    private float snapRayHeight = 10f;

    [MenuItem("Tools/UnityAI/Replace Placeholders With Prefabs…")]
    public static void Open() => GetWindow<PrefabSwapTool>("Prefab Swapper");

    void OnEnable() { AutoLoad(); }

    void OnGUI()
    {
        EditorGUILayout.LabelField("Replace Placeholders With Prefabs", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Assign prefabs (or let AutoLoad find them). Optionally pick a root (e.g. gnn_fixed_01_Generated).", MessageType.Info);

        searchRoot = (Transform)EditorGUILayout.ObjectField("Search Under (optional)", searchRoot, typeof(Transform), true);

        GUILayout.Space(6);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            HealthPickup = (GameObject)EditorGUILayout.ObjectField("HealthPickup", HealthPickup, typeof(GameObject), false);
            ArmorPickup = (GameObject)EditorGUILayout.ObjectField("ArmorPickup", ArmorPickup, typeof(GameObject), false);
            AmmoPickup = (GameObject)EditorGUILayout.ObjectField("AmmoPickup", AmmoPickup, typeof(GameObject), false);
            JumpPad = (GameObject)EditorGUILayout.ObjectField("JumpPad", JumpPad, typeof(GameObject), false);
            Teleporter = (GameObject)EditorGUILayout.ObjectField("Teleporter", Teleporter, typeof(GameObject), false);
            PlayerSpawn = (GameObject)EditorGUILayout.ObjectField("PlayerSpawn", PlayerSpawn, typeof(GameObject), false);
        }

        GUILayout.Space(6);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            deletePlaceholders = EditorGUILayout.ToggleLeft("Delete placeholder objects after replacement", deletePlaceholders);
            snapToGround = EditorGUILayout.ToggleLeft("Snap to ground (raycast down)", snapToGround);
            snapRayHeight = EditorGUILayout.FloatField("Snap ray height", snapRayHeight);
        }

        GUILayout.Space(8);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("AutoLoad Prefabs")) AutoLoad();
            if (GUILayout.Button("Run Replacement", GUILayout.Height(24))) Run();
        }
    }

    void AutoLoad()
    {
        // Prefer our standard folder; otherwise search project by exact name
        TryLoad(ref HealthPickup, "Assets/_UberStrike/Prefabs/Gameplay/HealthPickup.prefab", "HealthPickup");
        TryLoad(ref ArmorPickup, "Assets/_UberStrike/Prefabs/Gameplay/ArmorPickup.prefab", "ArmorPickup");
        TryLoad(ref AmmoPickup, "Assets/_UberStrike/Prefabs/Gameplay/AmmoPickup.prefab", "AmmoPickup");
        TryLoad(ref JumpPad, "Assets/_UberStrike/Prefabs/Gameplay/JumpPad.prefab", "JumpPad");
        TryLoad(ref Teleporter, "Assets/_UberStrike/Prefabs/Gameplay/Teleporter.prefab", "Teleporter");
        TryLoad(ref PlayerSpawn, "Assets/_UberStrike/Prefabs/Gameplay/PlayerSpawnPoint.prefab", "PlayerSpawnPoint");
    }

    static void TryLoad(ref GameObject slot, string preferPath, string exactName)
    {
        if (slot) return;
        slot = AssetDatabase.LoadAssetAtPath<GameObject>(preferPath);
        if (slot) return;
        var guids = AssetDatabase.FindAssets($"{exactName} t:Prefab");
        foreach (var g in guids)
        {
            var p = AssetDatabase.GUIDToAssetPath(g);
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
            if (go && go.name.Equals(exactName, StringComparison.OrdinalIgnoreCase)) { slot = go; return; }
        }
    }

    // --- NEW ALIASES TABLE ---
    static readonly string[][] Aliases = new[]
    {
        new[] { "Pickup_Health", "Healer", "HealthPickup" },
        new[] { "Pickup_Armor",  "Armor_base", "ArmorPickup" },
        new[] { "Pickup_Ammo",   "HandgunAMMO", "MachinegunAMMO", "AmmoPickup" },
        new[] { "JumpPad",       "jumpPadCATALYST", "JumpPadCYAN", "JumpPadYELLOW" },
        new[] { "Teleporter",    "teleport" },
        new[] { "Spawn",         "SpawnPoint", "PlayerSpawn" } // Added "Spawn" back as the primary key
    };

    // --- REPLACED FindByName METHOD (Now uses Aliases) ---
    static System.Collections.Generic.IEnumerable<GameObject> FindByName(Transform root, params string[] keys)
    {
        var all = root
            ? root.GetComponentsInChildren<Transform>(true).Select(t => t.gameObject)
            : GameObject.FindObjectsOfType<GameObject>().Where(go => go.scene.IsValid());

        // 1. Get the primary keys passed in (e.g., "Pickup_Health", "Teleporter")
        var allowed = new System.Collections.Generic.HashSet<string>(keys);

        // 2. Find all alias names corresponding to those keys
        var aliasMap = Aliases
            .Where(a => allowed.Contains(a[0])) // Filter for rows where the primary key matches
            .SelectMany(a => a)                  // Flatten the array of aliases into a single list
            .ToArray();

        // 3. Filter all GameObjects in the scene/root by any name in the alias list
        return all.Where(go => aliasMap.Contains(go.name, StringComparer.OrdinalIgnoreCase));
    }
    // --- END REPLACED FindByName METHOD ---

    static GameObject Spawn(GameObject prefab, Transform parent, Vector3 pos, Quaternion rot)
    {
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        if (!inst) inst = GameObject.Instantiate(prefab);
        Undo.RegisterCreatedObjectUndo(inst, "Spawn Prefab");
        inst.transform.SetParent(parent, true);
        inst.transform.SetPositionAndRotation(pos, rot);
        return inst;
    }

    void GetPlacement(Transform ph, out Vector3 pos, out Quaternion rot)
    {
        pos = ph.position; rot = Quaternion.Euler(0, ph.eulerAngles.y, 0);
        if (!snapToGround) return;
        var origin = ph.position + Vector3.up * snapRayHeight;
        if (Physics.Raycast(origin, Vector3.down, out var hit, snapRayHeight * 2f, ~0, QueryTriggerInteraction.Ignore))
            pos = hit.point + Vector3.up * 0.02f;
    }

    void Run()
    {
        if (!HealthPickup || !ArmorPickup || !AmmoPickup || !JumpPad || !Teleporter || !PlayerSpawn)
        {
            EditorUtility.DisplayDialog("Missing", "Assign all six prefabs first (AutoLoad helps).", "OK");
            return;
        }

        int nH = 0, nA = 0, nAm = 0, nJ = 0, nT = 0, nS = 0;

        // NOTE: Call sites are now shorter and implicitly use the alias table.

        foreach (var ph in FindByName(searchRoot, "Pickup_Health"))
        { GetPlacement(ph.transform, out var p, out var r); Spawn(HealthPickup, ph.transform.parent, p, r); if (deletePlaceholders) Undo.DestroyObjectImmediate(ph); nH++; }

        foreach (var ph in FindByName(searchRoot, "Pickup_Armor"))
        { GetPlacement(ph.transform, out var p, out var r); Spawn(ArmorPickup, ph.transform.parent, p, r); if (deletePlaceholders) Undo.DestroyObjectImmediate(ph); nA++; }

        foreach (var ph in FindByName(searchRoot, "Pickup_Ammo"))
        { GetPlacement(ph.transform, out var p, out var r); Spawn(AmmoPickup, ph.transform.parent, p, r); if (deletePlaceholders) Undo.DestroyObjectImmediate(ph); nAm++; }

        foreach (var ph in FindByName(searchRoot, "JumpPad"))
        { GetPlacement(ph.transform, out var p, out var r); Spawn(JumpPad, ph.transform.parent, p, r); if (deletePlaceholders) Undo.DestroyObjectImmediate(ph); nJ++; }

        var telPH = FindByName(searchRoot, "Teleporter").OrderBy(t => t.transform.position.x).ThenBy(t => t.transform.position.z).ToList();
        var telInst = new List<GameObject>();
        foreach (var ph in telPH)
        { GetPlacement(ph.transform, out var p, out var r); telInst.Add(Spawn(Teleporter, ph.transform.parent, p, r)); if (deletePlaceholders) Undo.DestroyObjectImmediate(ph); }
        for (int i = 0; i + 1 < telInst.Count; i += 2)
        {
            var a = telInst[i]; var b = telInst[i + 1];
            var la = a.GetComponent<TeleporterLink>() ?? a.AddComponent<TeleporterLink>();
            var lb = b.GetComponent<TeleporterLink>() ?? b.AddComponent<TeleporterLink>();
            la.Partner = b.transform; lb.Partner = a.transform; la.GroupId = lb.GroupId = i / 2;
        }
        nT = telInst.Count;

        // NOTE: This call now uses the Aliases map internally to find "Spawn", "SpawnPoint", and "PlayerSpawn"
        foreach (var ph in FindByName(searchRoot, "Spawn"))
        { GetPlacement(ph.transform, out var p, out var r); Spawn(PlayerSpawn, ph.transform.parent, p, r); if (deletePlaceholders) Undo.DestroyObjectImmediate(ph); nS++; }

        EditorSceneManager.MarkAllScenesDirty();
        EditorUtility.DisplayDialog("Prefab Swapper", $"Replaced:\nHealth {nH}\nArmor {nA}\nAmmo {nAm}\nJumpPads {nJ}\nTeleporters {nT}\nSpawns {nS}", "OK");
    }
}
