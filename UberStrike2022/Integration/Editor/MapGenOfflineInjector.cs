using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime component that injects MapGen-registered custom maps into OfflineBypass's map list.
/// Attach to a persistent GameObject (e.g., in Latest.unity) or use [RuntimeInitializeOnLoadMethod].
///
/// This bridges the gap between MapGenRegistrar (editor-time) and OfflineBypass (runtime).
/// Custom maps are registered with LevelManager after OfflineBypass.Bootstrap() completes.
/// </summary>
public class MapGenOfflineInjector : MonoBehaviour
{
    /// <summary>
    /// List of custom maps to inject at runtime.
    /// Populated from MapGenRegistrar's EditorPrefs data via the editor script,
    /// or manually configured in the inspector.
    /// </summary>
    [System.Serializable]
    public struct CustomMapEntry
    {
        public int mapId;
        public string displayName;
        public string sceneName;
    }

    [SerializeField]
    private List<CustomMapEntry> _customMaps = new List<CustomMapEntry>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoInject()
    {
        // Find injector in scene or create one
        var injector = FindObjectOfType<MapGenOfflineInjector>();
        if (injector != null)
        {
            injector.InjectMaps();
        }
    }

    public void InjectMaps()
    {
        if (_customMaps == null || _customMaps.Count == 0)
        {
            return;
        }

        foreach (var map in _customMaps)
        {
            if (map.mapId < 100 || string.IsNullOrEmpty(map.sceneName))
            {
                continue;
            }

            // Use LevelManager.AddMapView() to register the map at runtime
            // This creates a MapView entry that the game can load
            try
            {
                var existing = LevelManager.Instance.GetMapWithId(map.mapId);
                if (existing != null && existing.Id == map.mapId)
                {
                    Debug.Log($"[MapGenInjector] Map {map.mapId} already registered, skipping");
                    continue;
                }
            }
            catch
            {
                // Map not found, proceed to register
            }

            LevelManager.Instance.AddMapView(map.mapId, map.displayName);
            Debug.Log($"[MapGenInjector] Injected custom map: [{map.mapId}] {map.displayName} ({map.sceneName})");
        }
    }
}
