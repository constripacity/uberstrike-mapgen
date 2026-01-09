using UnityEngine;

/// <summary>
/// Ensures MapBundleLoader exists at runtime, even if scene authors forget to add it.
/// Attach this to a persistent GameObject or call from a startup script.
/// </summary>
public class RuntimeBootstrap : MonoBehaviour
{
    [Header("Runtime Map Loader")]
    [Tooltip("Prefab containing MapBundleLoader component")]
    public GameObject mapLoaderPrefab;

    [Header("Auto-Create if Missing")]
    public bool autoCreateIfMissing = true;

    private void Awake()
    {
        EnsureMapLoaderExists();
    }

    private void EnsureMapLoaderExists()
    {
        // Check if MapBundleLoader already exists
        var existing = FindObjectOfType<MapBundleLoader>();
        if (existing != null)
        {
            Debug.Log("[RuntimeBootstrap] MapBundleLoader already exists.");
            return;
        }

        // Try to instantiate from prefab
        if (mapLoaderPrefab != null)
        {
            var instance = Instantiate(mapLoaderPrefab);
            instance.name = "RuntimeMapLoader";
            Debug.Log("[RuntimeBootstrap] Instantiated MapBundleLoader from prefab.");
            return;
        }

        // Fallback: create GameObject with component
        if (autoCreateIfMissing)
        {
            var go = new GameObject("RuntimeMapLoader");
            go.AddComponent<MapBundleLoader>();
            Debug.Log("[RuntimeBootstrap] Created MapBundleLoader (no prefab assigned).");
        }
        else
        {
            Debug.LogWarning("[RuntimeBootstrap] MapBundleLoader missing and autoCreate disabled!");
        }
    }
}
