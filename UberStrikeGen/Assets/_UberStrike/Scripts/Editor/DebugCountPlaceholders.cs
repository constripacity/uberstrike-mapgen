using UnityEditor;
using UnityEngine;
using System.Linq;

public static class DebugCountPlaceholders
{
    [MenuItem("Tools/UnityAI/Debug/Count Placeholders In Scene")]
    public static void Count()
    {
        string[] names = { "Pickup_Health", "Pickup_Armor", "Pickup_Ammo", "JumpPad", "Teleporter", "Spawn", "SpawnPoint", "PlayerSpawn" };
        var all = Object.FindObjectsOfType<GameObject>().Where(go => go.scene.IsValid());
        foreach (var n in names)
        {
            int c = all.Count(go => go.name == n);
            Debug.Log($"{n}: {c}");
        }
    }
}
