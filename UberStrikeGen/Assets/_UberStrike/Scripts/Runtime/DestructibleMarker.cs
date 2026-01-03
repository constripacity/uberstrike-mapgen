using UnityEngine;

/// <summary>
/// Marks a GameObject as destructible and stores its source pixel coordinates.
/// </summary>
public class DestructibleMarker : MonoBehaviour
{
    /// <summary>
    /// The pixel coordinates in the collision layer that spawned this destructible object.
    /// </summary>
    public Vector2Int sourcePixel;

    /// <summary>
    /// Optional: Health or durability of this destructible object.
    /// </summary>
    public float health = 100f;

    /// <summary>
    /// Optional: Prefab to spawn when destroyed.
    /// </summary>
    public GameObject destroyedPrefab;

    /// <summary>
    /// Optional: Particle effect to play when destroyed.
    /// </summary>
    public ParticleSystem destroyEffect;

    private void OnDestroy()
    {
        if (destroyEffect != null && Application.isPlaying)
        {
            Instantiate(destroyEffect, transform.position, Quaternion.identity);
        }

        if (destroyedPrefab != null && Application.isPlaying)
        {
            Instantiate(destroyedPrefab, transform.position, transform.rotation);
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        // Draw a small red cube to indicate this is destructible
        Gizmos.color = new Color(1f, 0f, 0f, 0.3f);
        Gizmos.DrawWireCube(transform.position, Vector3.one * 0.5f);
    }
#endif
}
