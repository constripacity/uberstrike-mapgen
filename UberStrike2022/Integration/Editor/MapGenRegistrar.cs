using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Registers MapGen-generated maps with UberStrike's OfflineBypass and SceneExporter systems.
/// Modifies the runtime map list so generated maps appear in the game's map selection.
/// </summary>
public class MapGenRegistrar : EditorWindow
{
    /// <summary>
    /// Stores registered custom maps persistently via EditorPrefs.
    /// Format: "mapId|displayName|sceneName" separated by semicolons.
    /// </summary>
    private const string PREFS_KEY = "MapGen_RegisteredMaps";

    private int _newMapId = 100;
    private string _newDisplayName = "";
    private string _newSceneName = "";
    private Vector2 _scrollPos;

    [MenuItem("Tools/MapGen/Register Map")]
    static void ShowWindow()
    {
        var window = GetWindow<MapGenRegistrar>("Map Registrar");
        window.minSize = new Vector2(450, 400);
        window.Show();
    }

    void OnEnable()
    {
        // Auto-detect from current scene
        var config = Object.FindObjectOfType<MapConfiguration>();
        if (config != null && config.MapId >= 100)
        {
            _newMapId = config.MapId;
            _newSceneName = config.gameObject.name;
            _newDisplayName = _newSceneName.Replace("Level", "");
        }
    }

    void OnGUI()
    {
        GUILayout.Label("Register Generated Maps", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Registers maps with OfflineBypass so they appear in the game's map selection.\n" +
            "Maps are persisted across Unity sessions via EditorPrefs.",
            MessageType.Info);
        EditorGUILayout.Space();

        // New map registration
        GUILayout.Label("Add New Map", EditorStyles.boldLabel);
        _newMapId = EditorGUILayout.IntField("Map ID", _newMapId);
        _newDisplayName = EditorGUILayout.TextField("Display Name", _newDisplayName);
        _newSceneName = EditorGUILayout.TextField("Scene Name", _newSceneName);

        if (string.IsNullOrEmpty(_newSceneName) && !string.IsNullOrEmpty(_newDisplayName))
        {
            _newSceneName = "Level" + _newDisplayName.Replace(" ", "");
        }

        EditorGUILayout.Space();
        EditorGUI.BeginDisabledGroup(_newMapId < 100 || string.IsNullOrEmpty(_newSceneName));
        if (GUILayout.Button("Register Map", GUILayout.Height(30)))
        {
            RegisterMap(_newMapId, _newDisplayName, _newSceneName);
        }
        EditorGUI.EndDisabledGroup();

        if (GUILayout.Button("Auto-detect from Current Scene"))
        {
            AutoDetectFromScene();
        }

        EditorGUILayout.Space();
        EditorGUILayout.Space();

        // List registered maps
        GUILayout.Label("Registered Custom Maps", EditorStyles.boldLabel);
        var maps = GetRegisteredMaps();

        if (maps.Count == 0)
        {
            EditorGUILayout.HelpBox("No custom maps registered yet.", MessageType.None);
        }
        else
        {
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.MaxHeight(200));
            foreach (var map in maps.ToList())
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"[{map.mapId}]", GUILayout.Width(40));
                EditorGUILayout.LabelField(map.displayName, GUILayout.Width(150));
                EditorGUILayout.LabelField(map.sceneName);
                if (GUILayout.Button("X", GUILayout.Width(25)))
                {
                    UnregisterMap(map.mapId);
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }

        EditorGUILayout.Space();
        if (maps.Count > 0)
        {
            if (GUILayout.Button("Clear All Registrations"))
            {
                if (EditorUtility.DisplayDialog("Clear Maps",
                    "Remove all custom map registrations?", "Yes", "Cancel"))
                {
                    EditorPrefs.DeleteKey(PREFS_KEY);
                }
            }
        }

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Registered maps will be injected into OfflineBypass.CreateMapList() at runtime.\n" +
            "Use MapGenOfflineInjector component (auto-added) for runtime injection.",
            MessageType.Info);
    }

    void AutoDetectFromScene()
    {
        var config = Object.FindObjectOfType<MapConfiguration>();
        if (config != null)
        {
            _newMapId = config.MapId;
            _newSceneName = config.gameObject.name;
            _newDisplayName = _newSceneName.Replace("Level", "");
            Debug.Log($"[MapGenRegistrar] Detected: MapId={_newMapId}, Scene={_newSceneName}");
        }
        else
        {
            EditorUtility.DisplayDialog("Auto-detect",
                "No MapConfiguration found in current scene.\n" +
                "Run 'Convert to UberStrike Map' first.", "OK");
        }
    }

    /// <summary>
    /// Register a map and add it to SceneExporter.MapsToExport.
    /// </summary>
    public static void RegisterMap(int mapId, string displayName, string sceneName)
    {
        var maps = GetRegisteredMaps();

        // Remove existing with same ID
        maps.RemoveAll(m => m.mapId == mapId);

        maps.Add(new RegisteredMap
        {
            mapId = mapId,
            displayName = displayName,
            sceneName = sceneName
        });

        SaveRegisteredMaps(maps);

        // Add to SceneExporter.MapsToExport (runtime effect, resets on domain reload)
        if (!SceneExporter.MapsToExport.ContainsKey(mapId))
        {
            SceneExporter.MapsToExport[mapId] = sceneName;
        }

        // Add to EditorBuildSettings
        string scenePath = "Assets/Scenes/" + sceneName + ".unity";
        var scenes = EditorBuildSettings.scenes.ToList();
        if (!scenes.Any(s => s.path == scenePath))
        {
            scenes.Add(new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        Debug.Log($"[MapGenRegistrar] Registered: MapId={mapId}, Name={displayName}, Scene={sceneName}");
    }

    public static void UnregisterMap(int mapId)
    {
        var maps = GetRegisteredMaps();
        maps.RemoveAll(m => m.mapId == mapId);
        SaveRegisteredMaps(maps);

        if (SceneExporter.MapsToExport.ContainsKey(mapId))
        {
            SceneExporter.MapsToExport.Remove(mapId);
        }

        Debug.Log($"[MapGenRegistrar] Unregistered MapId={mapId}");
    }

    /// <summary>
    /// Get all registered custom maps from EditorPrefs.
    /// </summary>
    public static List<RegisteredMap> GetRegisteredMaps()
    {
        var maps = new List<RegisteredMap>();
        string data = EditorPrefs.GetString(PREFS_KEY, "");
        if (string.IsNullOrEmpty(data)) return maps;

        foreach (var entry in data.Split(';'))
        {
            var parts = entry.Split('|');
            if (parts.Length == 3 && int.TryParse(parts[0], out int id))
            {
                maps.Add(new RegisteredMap
                {
                    mapId = id,
                    displayName = parts[1],
                    sceneName = parts[2]
                });
            }
        }
        return maps;
    }

    static void SaveRegisteredMaps(List<RegisteredMap> maps)
    {
        var entries = maps.Select(m => $"{m.mapId}|{m.displayName}|{m.sceneName}");
        EditorPrefs.SetString(PREFS_KEY, string.Join(";", entries));
    }

    /// <summary>
    /// Called on domain reload to re-inject registered maps into SceneExporter.
    /// </summary>
    [InitializeOnLoadMethod]
    static void OnDomainReload()
    {
        foreach (var map in GetRegisteredMaps())
        {
            if (!SceneExporter.MapsToExport.ContainsKey(map.mapId))
            {
                SceneExporter.MapsToExport[map.mapId] = map.sceneName;
            }
        }
    }

    public struct RegisteredMap
    {
        public int mapId;
        public string displayName;
        public string sceneName;
    }
}
