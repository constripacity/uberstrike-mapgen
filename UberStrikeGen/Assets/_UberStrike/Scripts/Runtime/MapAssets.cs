using UnityEngine;

namespace UnityAI
{
    /// <summary>
    /// ScriptableObject that defines visual theme settings for a map.
    /// </summary>
    [CreateAssetMenu(fileName = "NewMapTheme", menuName = "UnityAI/Map Theme")]
    public class MapTheme : ScriptableObject
    {
        [Header("Materials")]
        public Material WallMat;
        public Material FloorMat;
        public Material PlatformMat;
        public Material SpawnMat;
        public Material PickupMat;
        public Material JumpPadMat;
        public Material TeleporterMat;

        [Header("Environment")]
        [Range(0f, 0.1f)]
        public float FogDensity = 0.02f;
        public Color FogColor = new Color(0.1f, 0.1f, 0.2f);

        [Header("Lighting")]
        public Color ambientColor = Color.gray;
        public float ambientIntensity = 1.0f;

        [Header("Props")]
        public GameObject[] decorationPrefabs;
        public float decorationDensity = 0.1f;
    }

    /// <summary>
    /// ScriptableObject that defines gameplay elements for a map.
    /// </summary>
    [CreateAssetMenu(fileName = "NewGameplaySet", menuName = "UnityAI/Map Gameplay Set")]
    public class MapGameplaySet : ScriptableObject
    {
        [Header("Spawn Points")]
        public GameObject SpawnNeutral;
        public GameObject SpawnRed;
        public GameObject SpawnGreen;
        [Range(0.5f, 20f)]
        public float MinSpawnSeparation = 6f;

        [Header("Pickups")]
        public GameObject PickupHealth;
        public GameObject PickupArmor;
        [Range(0.5f, 10f)]
        public float MinPickupSeparation = 4f;

        [Header("Gameplay Elements")]
        public GameObject JumpPad;
        public GameObject Teleporter;

        [Header("Placement Settings")]
        [Range(0.5f, 10f)]
        public float FloorRaycastDown = 5f;
        public float spawnPointHeight = 1.0f;

        [Header("Power-ups")]
        public GameObject[] powerUpPrefabs;
        public float powerUpSpawnChance = 0.1f;

        [Header("AI")]
        public GameObject aiAgentPrefab;
        public int maxAIAgents = 10;
    }
}