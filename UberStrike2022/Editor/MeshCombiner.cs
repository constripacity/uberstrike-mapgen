using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public class MeshCombiner : EditorWindow
{
    [MenuItem("Tools/UnityAI/Combine Meshes")]
    static void ShowWindow() => GetWindow<MeshCombiner>("Mesh Combiner");

    void OnGUI()
    {
        GUILayout.Label("Combine all floor/wall cubes into one mesh", EditorStyles.boldLabel);
        if (GUILayout.Button("Combine Selected"))
        {
            CombineSelected();
        }
    }

    static void CombineSelected()
    {
        var selected = Selection.activeGameObject;
        if (!selected)
        {
            EditorUtility.DisplayDialog("Mesh Combiner", "Select the parent object (e.g. Arena_Generated).", "OK");
            return;
        }

        MeshFilter[] filters = selected.GetComponentsInChildren<MeshFilter>();
        List<CombineInstance> combines = new List<CombineInstance>();

        foreach (var mf in filters)
        {
            if (!mf.sharedMesh) continue;
            CombineInstance ci = new CombineInstance
            {
                mesh = mf.sharedMesh,
                transform = mf.transform.localToWorldMatrix
            };
            combines.Add(ci);
        }

        Mesh combined = new Mesh();
        combined.CombineMeshes(combines.ToArray());

        GameObject newGO = new GameObject(selected.name + "_Merged");
        newGO.AddComponent<MeshFilter>().sharedMesh = combined;
        newGO.AddComponent<MeshRenderer>().sharedMaterial = new Material(Shader.Find("Standard"));
        newGO.AddComponent<MeshCollider>().sharedMesh = combined;
        newGO.isStatic = true;

        EditorUtility.DisplayDialog("Mesh Combiner", $"Combined {filters.Length} meshes → {combined.vertexCount} vertices.", "OK");
    }
}
