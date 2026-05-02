#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Ensures the project tags MapGen-built scenes depend on are defined. Runs
/// once on Editor load (idempotent) and exposes a manual menu entry. Closes
/// carry-forward issue #1 from the 2026-05-02 next-session briefing
/// ("Tag: SpawnPoint is not defined" emitted by BuildFromBlueprint:1260).
/// </summary>
[InitializeOnLoad]
public static class MapGenTagsInitializer
{
    private static readonly string[] RequiredTags = { "SpawnPoint" };

    static MapGenTagsInitializer()
    {
        EnsureTags(silentIfNoChange: true);
    }

    [MenuItem("Tools/UnityAI/Ensure Required Tags")]
    public static void EnsureTagsMenu() => EnsureTags(silentIfNoChange: false);

    private static void EnsureTags(bool silentIfNoChange)
    {
        var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (assets == null || assets.Length == 0)
        {
            Debug.LogWarning("[MapGenTags] Could not load ProjectSettings/TagManager.asset; tags not ensured.");
            return;
        }

        var so = new SerializedObject(assets[0]);
        var tagsProp = so.FindProperty("tags");
        if (tagsProp == null)
        {
            Debug.LogWarning("[MapGenTags] TagManager.asset has no 'tags' property; layout changed?");
            return;
        }

        int added = 0;
        foreach (var required in RequiredTags)
        {
            if (HasTag(tagsProp, required)) continue;
            tagsProp.InsertArrayElementAtIndex(tagsProp.arraySize);
            tagsProp.GetArrayElementAtIndex(tagsProp.arraySize - 1).stringValue = required;
            added++;
            Debug.Log($"[MapGenTags] Added required tag: '{required}'");
        }

        if (added > 0)
        {
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
        }
        else if (!silentIfNoChange)
        {
            Debug.Log($"[MapGenTags] All {RequiredTags.Length} required tags already defined.");
        }
    }

    private static bool HasTag(SerializedProperty tagsProp, string tag)
    {
        for (int i = 0; i < tagsProp.arraySize; i++)
        {
            if (tagsProp.GetArrayElementAtIndex(i).stringValue == tag) return true;
        }
        return false;
    }
}
#endif
