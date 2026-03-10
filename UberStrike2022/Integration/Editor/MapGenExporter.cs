using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Exports MapGen-generated maps as AssetBundles compatible with UberStrike's LevelManager.
/// Output format matches what LevelManager.StartLoadingMap() expects.
/// </summary>
public class MapGenExporter : EditorWindow
{
    private string _selectedScene = "";
    private bool _useHashedName = false;
    private string _outputFolder = "";

    [MenuItem("Tools/MapGen/Export Generated Map")]
    static void ShowWindow()
    {
        var window = GetWindow<MapGenExporter>("Map Exporter");
        window.minSize = new Vector2(450, 300);
        window.Show();
    }

    void OnEnable()
    {
        _outputFolder = Application.dataPath + "/../Latest/WindowsStandalone/"
            + ApplicationDataManager.StandaloneFilename + "_Data";
    }

    void OnGUI()
    {
        GUILayout.Label("Export Generated Map as AssetBundle", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // Scene selection
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Scene to Export", GUILayout.Width(120));
        _selectedScene = EditorGUILayout.TextField(_selectedScene);
        if (GUILayout.Button("Browse", GUILayout.Width(60)))
        {
            string path = EditorUtility.OpenFilePanel("Select Scene", "Assets/Scenes", "unity");
            if (!string.IsNullOrEmpty(path))
            {
                // Convert to relative path
                _selectedScene = "Assets" + path.Substring(Application.dataPath.Length);
            }
        }
        EditorGUILayout.EndHorizontal();

        // Output folder
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Output Folder", GUILayout.Width(120));
        _outputFolder = EditorGUILayout.TextField(_outputFolder);
        if (GUILayout.Button("Browse", GUILayout.Width(60)))
        {
            string path = EditorUtility.OpenFolderPanel("Output Folder", _outputFolder, "");
            if (!string.IsNullOrEmpty(path))
            {
                _outputFolder = path;
            }
        }
        EditorGUILayout.EndHorizontal();

        _useHashedName = EditorGUILayout.Toggle("Use Hashed Filename", _useHashedName);

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Exports the scene as a .unity3d AssetBundle.\n" +
            "LevelManager loads maps from: {SceneName}.unity3d\n" +
            "Place the bundle next to the game executable's data folder.",
            MessageType.Info);

        EditorGUILayout.Space();

        // Auto-detect registered maps
        var registeredMaps = MapGenRegistrar.GetRegisteredMaps();
        if (registeredMaps.Count > 0)
        {
            GUILayout.Label("Quick Export Registered Maps:", EditorStyles.boldLabel);
            foreach (var map in registeredMaps)
            {
                string scenePath = "Assets/Scenes/" + map.sceneName + ".unity";
                bool sceneExists = File.Exists(Application.dataPath + "/../" + scenePath);

                EditorGUI.BeginDisabledGroup(!sceneExists);
                if (GUILayout.Button($"Export [{map.mapId}] {map.displayName} ({map.sceneName})"))
                {
                    ExportScene(scenePath, map.sceneName, map.mapId);
                }
                EditorGUI.EndDisabledGroup();

                if (!sceneExists)
                {
                    EditorGUILayout.HelpBox($"Scene not found: {scenePath}", MessageType.Warning);
                }
            }
        }

        EditorGUILayout.Space();

        EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(_selectedScene));
        if (GUILayout.Button("Export Selected Scene", GUILayout.Height(35)))
        {
            string sceneName = Path.GetFileNameWithoutExtension(_selectedScene);
            ExportScene(_selectedScene, sceneName, 0);
        }
        EditorGUI.EndDisabledGroup();
    }

    void ExportScene(string scenePath, string sceneName, int mapId)
    {
        if (!File.Exists(Application.dataPath + "/../" + scenePath))
        {
            EditorUtility.DisplayDialog("Export Error",
                $"Scene not found: {scenePath}", "OK");
            return;
        }

        // Ensure output folder exists
        if (!Directory.Exists(_outputFolder))
        {
            Directory.CreateDirectory(_outputFolder);
        }

        // Build the AssetBundle (scene bundle)
        // UberStrike loads via: {sceneName}.unity3d
        string bundleName = sceneName + ".unity3d";
        string outputPath = Path.Combine(_outputFolder, bundleName);

        Debug.Log($"[MapGenExporter] Building AssetBundle: {outputPath}");

        var result = BuildPipeline.BuildPlayer(
            new string[] { scenePath },
            outputPath,
            BuildTarget.StandaloneWindows,
            BuildOptions.BuildAdditionalStreamedScenes
        );

        if (result.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
        {
            long sizeBytes = new FileInfo(outputPath).Length;
            string sizeStr = sizeBytes > 1024 * 1024
                ? $"{sizeBytes / (1024f * 1024f):F1} MB"
                : $"{sizeBytes / 1024f:F1} KB";

            Debug.Log($"[MapGenExporter] Export success: {outputPath} ({sizeStr})");
            EditorUtility.DisplayDialog("Export Complete",
                $"AssetBundle exported successfully!\n\n" +
                $"Path: {outputPath}\n" +
                $"Size: {sizeStr}\n\n" +
                $"Place this file in the game's data folder to load it.",
                "OK");
        }
        else
        {
            Debug.LogError($"[MapGenExporter] Export failed: {result.summary.result}");
            EditorUtility.DisplayDialog("Export Failed",
                $"Build failed: {result.summary.result}\n\n" +
                "Check the Console for details.", "OK");
        }
    }
}
