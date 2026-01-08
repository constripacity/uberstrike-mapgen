using UnityEngine;

namespace MapGen.Core
{
    // Container for spawn logic
    public class MapGenSpawnPoint : MonoBehaviour
    {
        public int TeamID = 0; // 0=DM, 1=Red, 2=Blue...
        
        private void OnDrawGizmos()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, 0.5f);
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * 2);
        }
    }

    // Teleporter Node
    public class MapGenTeleporterNode : MonoBehaviour
    {
        public int NodeID;
        public int PairID; // ID of the paired teleporter
        
        // Link to the actual teleporter prefab instance if needed
        public GameObject VisualInstance;

        private void OnDrawGizmos()
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(transform.position, 0.5f);
        }
    }
}
