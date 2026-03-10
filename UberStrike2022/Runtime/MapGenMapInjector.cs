using System.Collections.Generic;
using UberStrike.Core.Models.Views;
using UberStrike.Core.Types;
using UberStrike.Realtime.Common;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Injects MapGen-generated custom maps into LevelManager at runtime.
///
/// The game gets its map list from the server (ws-dev.uberforever.eu) via
/// ApplicationWebServiceClient.GetMaps() → LevelManager.InitializeMapsToLoad().
/// OfflineBypass.Bootstrap() is never called, so editing OfflineBypass has no effect.
///
/// This script hooks into sceneLoaded and polls until LevelManager has been
/// initialized with server maps, then adds our custom maps via AddMapView().
/// Uses the same [RuntimeInitializeOnLoadMethod] pattern as BeastLightmapLoader.
/// </summary>
public static class MapGenMapInjector
{
    private static bool _injected = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        _injected = false;
        SceneManager.sceneLoaded += OnSceneLoaded;
        // Also start polling via a helper MonoBehaviour
        var go = new GameObject("[MapGenMapInjector]");
        go.hideFlags = HideFlags.HideAndDontSave;
        Object.DontDestroyOnLoad(go);
        go.AddComponent<MapGenMapInjectorPoller>();
        Debug.Log("[MapGenMapInjector] Initialized — waiting for LevelManager to load server maps...");
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        TryInject();
    }

    public static void TryInject()
    {
        if (_injected) return;

        // Wait until LevelManager has been populated with server maps
        // Count > 1 means the server maps have been loaded (0 = just the lobby)
        if (LevelManager.Instance.Count <= 1) return;

        // Don't add if already present (e.g. if the server already knows about this map)
        if (LevelManager.Instance.HasMapWithId(100)) return;

        var defaultSettings = new MapSettings
        {
            KillsMin = 10,
            KillsMax = 200,
            KillsCurrent = 50,
            PlayersMin = 2,
            PlayersMax = 16,
            PlayersCurrent = 8,
            TimeMin = 5,
            TimeMax = 30,
            TimeCurrent = 10
        };

        var mapView = new MapView
        {
            MapId = 100,
            DisplayName = "GeneratedArena",
            Description = "MapGen-generated arena map",
            SceneName = "LevelGeneratedArena",
            FileName = "LevelGeneratedArena.unity3d",
            IsBlueBox = false,
            SupportedGameModes = 7, // DM + TDM + Elim
            MaxPlayers = 16,
            Settings = new Dictionary<GameModeType, MapSettings>
            {
                { GameModeType.DeathMatch, defaultSettings },
                { GameModeType.TeamDeathMatch, defaultSettings },
                { GameModeType.EliminationMode, defaultSettings }
            }
        };

        LevelManager.Instance.AddMapView(mapView);
        _injected = true;

        Debug.Log("[MapGenMapInjector] Injected custom map: GeneratedArena (MapId=100). LevelManager now has " + LevelManager.Instance.Count + " maps.");
    }
}

/// <summary>
/// Helper MonoBehaviour that polls every frame until injection succeeds.
/// Needed because the server map data arrives asynchronously via coroutines
/// and there's no guaranteed scene load event after it completes.
/// </summary>
public class MapGenMapInjectorPoller : MonoBehaviour
{
    private void Update()
    {
        MapGenMapInjector.TryInject();
    }
}
