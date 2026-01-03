#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System;
using System.IO;
using System.Linq;

public class QuickBuildSelect : EditorWindow
{
    string pngPath = "";
    float metersPerPixel = 0.20f;

    [MenuItem("Tools/UnityAI/Quick Build ► Select PNG...")]
    static void Open() => GetWindow<QuickBuildSelect>("Quick Build");

    void OnGUI()
    {
        GUILayout.Label("Build From Blueprint PNG", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Select a blueprint PNG (absolute path OK).", MessageType.Info);

        EditorGUILayout.Space();
        EditorGUILayout.BeginHorizontal();
        pngPath = EditorGUILayout.TextField("PNG Path", pngPath);
        if (GUILayout.Button("Browse", GUILayout.Width(80)))
        {
            var pick = EditorUtility.OpenFilePanel("Choose blueprint PNG", Application.dataPath, "png");
            if (!string.IsNullOrEmpty(pick))
                pngPath = pick;
        }
        EditorGUILayout.EndHorizontal();

        metersPerPixel = EditorGUILayout.Slider("Meters/Pixel", metersPerPixel, 0.05f, 0.5f);

        if (GUILayout.Button("Build & Save Scene", GUILayout.Height(32)))
        {
            Build(pngPath, metersPerPixel);
        }
    }

    void Build(string fullPng, float mpp)
    {
        try
        {
            if (string.IsNullOrEmpty(fullPng) || !File.Exists(fullPng))
                throw new Exception("PNG path is invalid: " + fullPng);

            // Create a fresh empty scene for the build
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);

            // Invoke BuildFromBlueprint.BuildFromPNGPath(fullPng, mpp) via reflection to ensure we pass the selected file
            var type = typeof(QuickBuildSelect).Assembly.GetTypes()
                       .FirstOrDefault(t => t.Name == "BuildFromBlueprint");
            if (type == null) throw new Exception("BuildFromBlueprint class not found.");
            var mi = type.GetMethod("BuildFromPNGPath",
                     System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (mi == null) throw new Exception("BuildFromPNGPath(string,float) method not found.");
            mi.Invoke(null, new object[] { fullPng, mpp });

            // Save scene to playable maps folder
            var outDir = "Assets/_UberStrike/Maps/Playable";
            System.IO.Directory.CreateDirectory(outDir);
            string outPath = System.IO.Path.Combine(outDir, "Arena_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".unity").Replace("\\", "/");
            bool ok = UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, outPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            if (!ok) throw new Exception("Failed to save: " + outPath);
            Debug.Log("[QuickBuildSelect] ✅ Saved: " + outPath);
        }
        catch (Exception ex)
        {
            Debug.LogError("[QuickBuildSelect] ❌ " + ex);
            EditorUtility.DisplayDialog("Quick Build Failed", ex.Message, "OK");
        }
    }
}
#endif
