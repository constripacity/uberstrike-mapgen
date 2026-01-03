using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UnityAI
{
    /// <summary>
    /// Links two teleporters together for bidirectional teleportation.
    /// </summary>
    public class TeleporterLink : MonoBehaviour
    {
        /// <summary>
        /// Unique identifier for teleporter pairs (1, 2, 3, etc.)
        /// </summary>
        public int GroupId;

        /// <summary>
        /// Reference to the partner teleporter this one links to.
        /// </summary>
        public Transform Partner;

        /// <summary>
        /// Optional: Effect to play when teleporting.
        /// </summary>
        public ParticleSystem teleportEffect;

        /// <summary>
        /// Optional: Sound to play when teleporting.
        /// </summary>
        public AudioClip teleportSound;

        private void OnTriggerEnter(Collider other)
        {
            if (Partner == null)
            {
                return;
            }

            var body = other.attachedRigidbody;

            // Determine target position/direction.
            Transform exit = Partner.Find("Exit");
            Vector3 targetPos = exit ? exit.position : Partner.position;
            Vector3 targetDir = exit ? exit.forward : Partner.forward;

            other.transform.SetPositionAndRotation(targetPos, Quaternion.LookRotation(targetDir, Vector3.up));

            if (body != null)
            {
                float speed = body.linearVelocity.magnitude;
                body.linearVelocity = targetDir * speed;
            }

            if (teleportEffect != null)
            {
                Instantiate(teleportEffect, targetPos, Quaternion.identity);
            }

            if (teleportSound != null && Application.isPlaying)
            {
                AudioSource.PlayClipAtPoint(teleportSound, targetPos);
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (Partner != null)
            {
                Gizmos.color = new Color(1f, 0f, 1f, 0.5f);
                Gizmos.DrawLine(transform.position, Partner.position);

                Vector3 direction = (Partner.position - transform.position).normalized;
                Vector3 arrowPos = transform.position + direction * Vector3.Distance(transform.position, Partner.position) * 0.5f;
                Gizmos.DrawSphere(arrowPos, 0.3f);
            }

            Handles.Label(transform.position + Vector3.up * 2f, $"Teleporter Group {GroupId}");
        }
#endif
    }
}
