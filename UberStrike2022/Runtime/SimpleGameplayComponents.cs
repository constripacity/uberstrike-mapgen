using UnityEngine;

namespace UnityAI
{
    /// <summary>
    /// Minimal jump pad behaviour that launches rigidbodies upward.
    /// </summary>
    [DisallowMultipleComponent]
    public class SimpleJumpPad : MonoBehaviour
    {
        [Tooltip("Force applied to rigidbodies entering the trigger.")]
        public float jumpForce = 10f;

        [Tooltip("Optional local direction override. Defaults to world up.")]
        public Vector3 launchDirection = Vector3.up;

        private void OnTriggerEnter(Collider other)
        {
            if (!other.TryGetComponent<Rigidbody>(out var body))
            {
                return;
            }

            var direction = launchDirection.sqrMagnitude > 0.001f
                ? transform.TransformDirection(launchDirection.normalized)
                : Vector3.up;

            body.velocity = Vector3.zero;
            body.AddForce(direction * jumpForce, ForceMode.VelocityChange);
        }
    }

    /// <summary>
    /// Minimal teleporter that moves actors to the linked target transform.
    /// </summary>
    [DisallowMultipleComponent]
    public class SimpleTeleporter : MonoBehaviour
    {
        [Tooltip("Destination the teleporter will send actors to.")]
        public Transform target;

        [Tooltip("Delay in seconds before teleporting an actor.")]
        public float activationDelay = 0f;

        private bool _isTeleporting;

        private void OnTriggerEnter(Collider other)
        {
            if (target == null || _isTeleporting)
            {
                return;
            }

            if (!other.attachedRigidbody)
            {
                other.transform.position = target.position;
                other.transform.rotation = target.rotation;
                return;
            }

            _isTeleporting = true;
            var body = other.attachedRigidbody;
            body.position = target.position;
            body.velocity = target.forward * body.velocity.magnitude;
            other.transform.rotation = target.rotation;
            _isTeleporting = false;
        }
    }

    /// <summary>
    /// Simple spawn point marker for players.
    /// </summary>
    public class PlayerSpawnPoint : MonoBehaviour
    {
        [Tooltip("Optional team identifier.")]
        public string team = "Neutral";

        [Tooltip("Optional weight used by spawn selection systems.")]
        public float weight = 1f;
    }

    /// <summary>
    /// Base class for all simple pickups used by the generators.
    /// </summary>
    public class UberSimplePickup : MonoBehaviour
    {
        [Tooltip("Seconds before the pickup respawns.")]
        public float respawnTime = 30f;

        [Tooltip("Optional effect spawned when the pickup is collected.")]
        public GameObject pickupEffect;

        [Tooltip("Optional sound played when the pickup is collected.")]
        public AudioClip pickupSound;
    }

    /// <summary>
    /// Restores a small amount of health.
    /// </summary>
    public class SimpleHealthPickup : UberSimplePickup
    {
        [Tooltip("Health restored when collected.")]
        public int healthAmount = 25;
    }

    /// <summary>
    /// Restores a small amount of armor.
    /// </summary>
    public class SimpleArmorPickup : UberSimplePickup
    {
        [Tooltip("Armor restored when collected.")]
        public int armorAmount = 25;
    }

    /// <summary>
    /// Provides ammunition to the collector.
    /// </summary>
    public class SimpleAmmoPickup : UberSimplePickup
    {
        [Tooltip("Amount of ammunition granted when collected.")]
        public int ammoAmount = 30;
    }

    /// <summary>
    /// UberStrike-flavoured jump pad inheriting the simple behaviour.
    /// </summary>
    public class UberJumpPad : SimpleJumpPad
    {
        [Tooltip("Visual effect triggered when the pad fires.")]
        public ParticleSystem fireEffect;
    }

    /// <summary>
    /// UberStrike-flavoured teleporter inheriting the simple behaviour.
    /// </summary>
    public class UberTeleporter : SimpleTeleporter
    {
        [Tooltip("Optional identifier used to link teleporters in pairs.")]
        public int teleporterId;
    }
}
