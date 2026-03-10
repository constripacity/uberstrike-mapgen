using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Runtime diagnostics for MapGen integration.
/// Logs detailed state on every scene load and provides an F12 toggle debug overlay.
/// </summary>
public class MapGenDiagnostics : MonoBehaviour
{
    private static MapGenDiagnostics _instance;
    private bool _showOverlay = false;
    private string _lastDiagLog = "";
    private float _fps;
    private float _fpsTimer;
    private int _fpsFrames;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        if (_instance != null) return;

        var go = new GameObject("[MapGenDiagnostics]");
        go.hideFlags = HideFlags.HideAndDontSave;
        Object.DontDestroyOnLoad(go);
        _instance = go.AddComponent<MapGenDiagnostics>();

        SceneManager.sceneLoaded += _instance.OnSceneLoaded;
        Debug.Log("[MapGenDiag] Initialized. Press F12 to toggle debug overlay.");
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Delay diagnostics by 1 frame to let Awake/Start run on loaded objects
        StartCoroutine(DelayedDiagnostics(scene.name));
    }

    private System.Collections.IEnumerator DelayedDiagnostics(string sceneName)
    {
        yield return null; // wait 1 frame
        yield return null; // wait 2 frames (MapConfiguration.Awake may need a frame)
        RunDiagnostics(sceneName);
    }

    private void RunDiagnostics(string sceneName)
    {
        var log = $"\n{'='}{new string('=', 59)}\n[MapGenDiag] Scene loaded: {sceneName}\n{'='}{new string('=', 59)}";

        // --- LevelManager state ---
        try
        {
            int mapCount = LevelManager.Instance.Count;
            log += $"\n  LevelManager: {mapCount} maps registered";

            var allMaps = LevelManager.Instance.AllMaps;
            if (allMaps != null)
            {
                foreach (var map in allMaps)
                {
                    bool hasSpace = map.Space != null;
                    log += $"\n    Map: {map.Name} (id={map.Id}, sceneName={map.SceneName}, " +
                           $"hasSpace={hasSpace}, enabled={map.IsEnabled})";
                }
            }
        }
        catch (System.Exception ex)
        {
            log += $"\n  LevelManager: ERROR - {ex.Message}";
        }

        // --- MapConfiguration in scene ---
        var configs = Object.FindObjectsOfType<MapConfiguration>();
        log += $"\n  MapConfiguration instances found: {configs.Length}";

        foreach (var config in configs)
        {
            log += $"\n  --- MapConfiguration on '{config.gameObject.name}' ---";
            log += $"\n    MapId: {config.MapId}";
            log += $"\n    IsEnabled: {config.IsEnabled}";
            log += $"\n    Camera: {(config.Camera != null ? config.Camera.name : "NULL")}";
            log += $"\n    DefaultViewPoint: {(config.DefaultViewPoint != null ? config.DefaultViewPoint.name : "NULL")}";
            log += $"\n    SpawnPoints GO: {(config.SpawnPoints != null ? config.SpawnPoints.name : "NULL")}";
            log += $"\n    HasWaterPlane: {config.HasWaterPlane}";

            // Check _staticContentParent via reflection (it's protected)
            var field = typeof(MapConfiguration).GetField("_staticContentParent",
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Protected);
            if (field != null)
            {
                var scp = field.GetValue(config) as GameObject;
                log += $"\n    StaticContentParent: {(scp != null ? scp.name + " (" + scp.transform.childCount + " children)" : "NULL")}";

                if (scp != null)
                {
                    // Calculate bounds
                    var renderers = scp.GetComponentsInChildren<MeshRenderer>();
                    if (renderers.Length > 0)
                    {
                        Bounds b = renderers[0].bounds;
                        for (int i = 1; i < renderers.Length; i++)
                            b.Encapsulate(renderers[i].bounds);
                        log += $"\n    Geometry bounds: center={b.center:F1}, size={b.size:F1}";
                    }
                    log += $"\n    Renderer count: {renderers.Length}";
                }
            }

            // SpawnPoints detail
            if (config.SpawnPoints != null)
            {
                var spawns = config.SpawnPoints.GetComponentsInChildren<SpawnPoint>(true);
                log += $"\n    SpawnPoint components: {spawns.Length}";
                foreach (var sp in spawns)
                {
                    log += $"\n      {sp.name}: pos={sp.Position:F1} mode={sp.GameMode} team={sp.TeamPoint}";
                }
            }
        }

        // --- GameState ---
        try
        {
            var currentSpace = GameState.CurrentSpace;
            if (currentSpace != null)
            {
                log += $"\n  GameState.CurrentSpace: MapId={currentSpace.MapId}, name={currentSpace.gameObject.name}";
            }
            else
            {
                log += "\n  GameState.CurrentSpace: null";
            }
        }
        catch (System.Exception ex)
        {
            log += $"\n  GameState: ERROR - {ex.Message}";
        }

        // --- Player position ---
        try
        {
            if (GameState.LocalPlayer != null && GameState.LocalPlayer.transform != null)
            {
                log += $"\n  LocalPlayer position: {GameState.LocalPlayer.transform.position:F1}";
            }
            else
            {
                log += "\n  LocalPlayer: null or not spawned yet";
            }
        }
        catch (System.Exception)
        {
            log += "\n  LocalPlayer: not available";
        }

        log += $"\n{'='}{new string('=', 59)}";

        _lastDiagLog = log;
        Debug.Log(log);
    }

    private void Update()
    {
        // FPS calculation
        _fpsFrames++;
        _fpsTimer += Time.unscaledDeltaTime;
        if (_fpsTimer >= 0.5f)
        {
            _fps = _fpsFrames / _fpsTimer;
            _fpsFrames = 0;
            _fpsTimer = 0;
        }

        // F12 toggle
        if (Input.GetKeyDown(KeyCode.F12))
        {
            _showOverlay = !_showOverlay;
            Debug.Log($"[MapGenDiag] Overlay {(_showOverlay ? "ON" : "OFF")}");
        }
    }

    private void OnGUI()
    {
        if (!_showOverlay) return;

        // Semi-transparent background
        GUI.color = new Color(0, 0, 0, 0.75f);
        GUI.DrawTexture(new Rect(10, 10, 400, 200), Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.fontSize = 14;
        style.normal.textColor = Color.green;
        style.fontStyle = FontStyle.Bold;

        float y = 15;
        float lineH = 20;

        GUI.Label(new Rect(15, y, 390, lineH), $"[MapGen Diagnostics] FPS: {_fps:F0}", style);
        y += lineH;

        style.fontStyle = FontStyle.Normal;
        style.fontSize = 12;
        style.normal.textColor = Color.white;

        // Player position
        try
        {
            if (GameState.LocalPlayer != null && GameState.LocalPlayer.transform != null)
            {
                var pos = GameState.LocalPlayer.transform.position;
                GUI.Label(new Rect(15, y, 390, lineH), $"Player: ({pos.x:F1}, {pos.y:F1}, {pos.z:F1})", style);
            }
            else
            {
                GUI.Label(new Rect(15, y, 390, lineH), "Player: not spawned", style);
            }
        }
        catch { GUI.Label(new Rect(15, y, 390, lineH), "Player: N/A", style); }
        y += lineH;

        // Current map
        try
        {
            var space = GameState.CurrentSpace;
            if (space != null)
            {
                GUI.Label(new Rect(15, y, 390, lineH), $"Map: {space.gameObject.name} (id={space.MapId})", style);
                y += lineH;

                if (space.SpawnPoints != null)
                {
                    var spawns = space.SpawnPoints.GetComponentsInChildren<SpawnPoint>(true);
                    int dm = 0, tdm = 0;
                    foreach (var sp in spawns)
                    {
                        if (sp.GameMode == GameMode.DeathMatch) dm++;
                        else tdm++;
                    }
                    GUI.Label(new Rect(15, y, 390, lineH), $"Spawns: {spawns.Length} (DM={dm}, TDM={tdm})", style);
                }
                else
                {
                    style.normal.textColor = Color.red;
                    GUI.Label(new Rect(15, y, 390, lineH), "Spawns: NULL (SpawnPoints GO missing!)", style);
                    style.normal.textColor = Color.white;
                }
            }
            else
            {
                GUI.Label(new Rect(15, y, 390, lineH), "Map: none loaded", style);
            }
        }
        catch { GUI.Label(new Rect(15, y, 390, lineH), "Map: error reading state", style); }
        y += lineH;

        // LevelManager
        try
        {
            GUI.Label(new Rect(15, y, 390, lineH), $"LevelManager: {LevelManager.Instance.Count} maps", style);
        }
        catch { GUI.Label(new Rect(15, y, 390, lineH), "LevelManager: N/A", style); }
        y += lineH;

        // Scene info
        GUI.Label(new Rect(15, y, 390, lineH), $"Scene: {SceneManager.GetActiveScene().name}", style);
        y += lineH;

        style.fontSize = 10;
        style.normal.textColor = new Color(0.6f, 0.6f, 0.6f);
        GUI.Label(new Rect(15, y, 390, lineH), "Press F12 to hide overlay", style);
    }
}
