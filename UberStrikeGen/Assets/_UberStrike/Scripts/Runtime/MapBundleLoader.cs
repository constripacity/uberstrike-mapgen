using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MapBundleLoader : MonoBehaviour
{
    [Serializable]
    private class BundleEntry
    {
        public string bundleFileName;
        public string fullPath;
        public string[] scenePaths;
    }

    [Header("Debug")]
    public bool showUI = true;
    public KeyCode toggleKey = KeyCode.F9;

    private readonly List<BundleEntry> _entries = new();
    private readonly Dictionary<string, AssetBundle> _loadedBundles = new();

    private string _status = "";
    private Vector2 _scroll;

    private string BundlesDir => Path.Combine(Application.streamingAssetsPath, "MapBundles");

    private void Awake()
    {
        // Keep this alive when loading scenes, so the UI remains available
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        Scan();
    }

    private void Update()
    {
        if (Input.GetKeyDown(toggleKey))
            showUI = !showUI;
    }

    public void Scan()
    {
        _entries.Clear();
        _status = "";

        if (!Directory.Exists(BundlesDir))
        {
            _status = $"Bundles directory missing: {BundlesDir}";
            return;
        }

        var files = Directory.GetFiles(BundlesDir);
        int count = 0;

        foreach (var f in files)
        {
            var name = Path.GetFileName(f);

            if (name.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase))
                continue;

            // Skip obvious non-bundles
            if (name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                var bundle = AssetBundle.LoadFromFile(f);
                if (bundle == null)
                {
                    _status = $"Failed to load bundle: {name}";
                    continue;
                }

                var scenes = bundle.GetAllScenePaths();
                bundle.Unload(unloadAllLoadedObjects: false); // only needed for scanning

                if (scenes == null || scenes.Length == 0)
                {
                    _status = $"Bundle has no scenes: {name}";
                    continue;
                }

                _entries.Add(new BundleEntry
                {
                    bundleFileName = name,
                    fullPath = f,
                    scenePaths = scenes
                });

                count++;
            }
            catch (Exception ex)
            {
                _status = $"Error scanning {name}: {ex.Message}";
            }
        }

        _status = $"Found {count} bundle(s). Dir: {BundlesDir}";
    }

    private void OnGUI()
    {
        if (!showUI) return;

        const int w = 520;
        const int h = 520;
        GUILayout.BeginArea(new Rect(10, 10, w, h), GUI.skin.box);

        GUILayout.Label("MapBundleLoader (F9 to toggle)");
        GUILayout.Space(6);

        if (!string.IsNullOrEmpty(_status))
        {
            GUILayout.Label(_status);
            GUILayout.Space(6);
        }

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Rescan", GUILayout.Width(120)))
            Scan();

        if (GUILayout.Button("Unload All Bundles", GUILayout.Width(160)))
            UnloadAllBundles();

        GUILayout.EndHorizontal();

        GUILayout.Space(10);
        _scroll = GUILayout.BeginScrollView(_scroll);

        foreach (var e in _entries)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label($"Bundle: {e.bundleFileName}");

            for (int i = 0; i < e.scenePaths.Length; i++)
            {
                var scenePath = e.scenePaths[i];
                var sceneName = Path.GetFileNameWithoutExtension(scenePath);

                GUILayout.BeginHorizontal();
                GUILayout.Label($"Scene: {sceneName}", GUILayout.Width(260));

                if (GUILayout.Button("Load (Single)", GUILayout.Width(120)))
                {
                    _ = LoadSceneFromBundle(e.fullPath, sceneName);
                }

                GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();
        }

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private async System.Threading.Tasks.Task LoadSceneFromBundle(string bundlePath, string sceneName)
    {
        try
        {
            _status = $"Loading bundle: {Path.GetFileName(bundlePath)}";

            if (!_loadedBundles.TryGetValue(bundlePath, out var bundle) || bundle == null)
            {
                bundle = AssetBundle.LoadFromFile(bundlePath);
                if (bundle == null)
                {
                    _status = $"FAILED to load bundle: {bundlePath}";
                    return;
                }
                _loadedBundles[bundlePath] = bundle;
            }

            _status = $"Loading scene: {sceneName}";

            var op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            if (op == null)
            {
                _status = $"FAILED: LoadSceneAsync returned null for {sceneName}";
                return;
            }

            while (!op.isDone)
                await System.Threading.Tasks.Task.Yield();

            // Ensure a camera exists in the loaded scene
            EnsureCameraExists();

            _status = $"Loaded scene: {sceneName}";
        }
        catch (Exception ex)
        {
            _status = $"ERROR loading scene: {ex.Message}";
        }
    }

    private void EnsureCameraExists()
    {
        var cam = Camera.main;
        if (cam == null)
        {
            cam = FindObjectOfType<Camera>();
        }

        if (cam == null)
        {
            var go = new GameObject("FallbackCamera");
            cam = go.AddComponent<Camera>();
            cam.transform.position = new Vector3(0, 5, -10);
            cam.transform.LookAt(Vector3.zero);
            Debug.Log("[MapBundleLoader] Created fallback camera (scene had none).");
        }
    }

    private void UnloadAllBundles()
    {
        foreach (var kvp in _loadedBundles)
        {
            try { kvp.Value?.Unload(unloadAllLoadedObjects: false); } catch { /* ignore */ }
        }
        _loadedBundles.Clear();
        _status = "Unloaded all bundles (objects kept).";
    }
}
