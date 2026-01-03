using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace UnityAI
{
    /// <summary>
    /// Flow marker types for gameplay elements.
    /// </summary>
    public enum FlowMarkerType
    {
        None,
        Spawn,
        Choke,
        Arrow,
        Objective
    }

    /// <summary>
    /// Collision classification for physics layers.
    /// </summary>
    public enum CollisionKind
    {
        Unknown,
        Walkable,
        Blocked,
        Climbable,
        Destructible
    }

    /// <summary>
    /// Defines a stack with metadata and references to layer textures.
    /// </summary>
    [System.Serializable]
    public class StackDefinition
    {
        /// <summary>
        /// Container for the six stack layer textures.
        /// </summary>
        public struct StackLayerBundle
        {
            public Texture2D layout;
            public Texture2D height;
            public Texture2D flow;
            public Texture2D theme;
            public Texture2D lighting;
            public Texture2D collision;
        }

        // Flow color configuration (for MapTrainingPipeline)
        [System.Serializable]
        public class FlowColorConfig
        {
            public string spawnColorYellow = "#FFFF00";
            public string spawnColorRed = "#FF0000";
            public string spawnColorGreen = "#00FF00";
            public string chokeColor = "#FFA500";
            public string coverColor = "#808080";
            public string arrowColor = "#00FFFF";
        }

        [System.Serializable]
        public class LightingConfig
        {
            public string pointColor = "#FFD966";
            public string spotColor = "#FF9D3C";
            public float fogDensity = 0f;
            public float[] sunDirDeg = new[] { 50f, -30f, 0f };
        }

        [System.Serializable]
        public class CollisionConfig
        {
            public string climbable = "#00FF00";
            public string destructible = "#FF0000";
        }

        [System.Serializable]
        public struct ThemeMapEntry
        {
            public string color;
            public string theme;
        }

        // JSON-serializable fields
        public string name = "Untitled Stack";
        public string sourceName = "Untitled Stack";
        public string directory;
        public string layoutPath;
        public string heightPath;
        public string flowPath;
        public string themePath;
        public string lightingPath;
        public string collisionPath;

        // Map parameters
        public float metersPerPixel = 1.0f;
        public float wallHeight = 3.0f;
        public float heightScale = 0.1f;
        public float stairsRise = 2.0f;
        public float rampMaxSlopeDeg = 45f;
        public float doorWidthMeters = 1.5f;
        public float bridgeWidthMeters = 2.0f;

        // Gameplay settings
        public bool pairTeleporters = true;
        public bool navmesh = true;

        // Flow configuration
        public FlowColorConfig flow = new FlowColorConfig();
        public LightingConfig lighting = new LightingConfig();
        public CollisionConfig collision = new CollisionConfig();

        [SerializeField]
        private ThemeMapEntry[] themeEntries = Array.Empty<ThemeMapEntry>();

        [System.NonSerialized]
        private Dictionary<string, string> themeMapCache;

        // Runtime data
        private StackLayerBundle layers;
        private bool prepared;

        /// <summary>
        /// Access the layer bundle.
        /// </summary>
        public StackLayerBundle Layers => layers;

        /// <summary>
        /// Width of the layout texture in pixels.
        /// </summary>
        public int Width => layers.layout ? layers.layout.width : 0;

        /// <summary>
        /// Height of the layout texture in pixels.
        /// </summary>
        public int Height => layers.layout ? layers.layout.height : 0;

        /// <summary>
        /// Color-to-theme lookup derived from <see cref="themeEntries"/>.
        /// </summary>
        public Dictionary<string, string> themeMap
        {
            get
            {
                if (themeMapCache == null)
                {
                    themeMapCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    if (themeEntries != null)
                    {
                        foreach (var entry in themeEntries)
                        {
                            if (string.IsNullOrWhiteSpace(entry.color) || string.IsNullOrWhiteSpace(entry.theme))
                            {
                                continue;
                            }

                            string key = entry.color.StartsWith("#", StringComparison.Ordinal) ? entry.color : "#" + entry.color;
                            themeMapCache[key] = entry.theme;
                        }
                    }
                }

                return themeMapCache;
            }
            set
            {
                if (value == null)
                {
                    themeEntries = Array.Empty<ThemeMapEntry>();
                    themeMapCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    return;
                }

                themeEntries = new ThemeMapEntry[value.Count];
                int i = 0;
                foreach (var pair in value)
                {
                    themeEntries[i++] = new ThemeMapEntry
                    {
                        color = pair.Key,
                        theme = pair.Value
                    };
                }

                themeMapCache = null;
            }
        }

        /// <summary>
        /// Load a StackDefinition from a JSON file.
        /// </summary>
        public static StackDefinition LoadFromJSON(string jsonPath)
        {
            if (string.IsNullOrEmpty(jsonPath) || !File.Exists(jsonPath))
            {
                Debug.LogError($"[StackDefinition] JSON file not found: {jsonPath}");
                return null;
            }

            try
            {
                string json = File.ReadAllText(jsonPath);
                var definition = JsonUtility.FromJson<StackDefinition>(json);

                if (definition == null)
                {
                    Debug.LogError($"[StackDefinition] Failed to parse JSON: {jsonPath}");
                    return null;
                }

                // Set directory to the JSON file's directory if not already set
                if (string.IsNullOrEmpty(definition.directory))
                {
                    definition.directory = Path.GetDirectoryName(jsonPath);
                }

                // Set source name from filename if not specified
                if (string.IsNullOrEmpty(definition.sourceName))
                {
                    definition.sourceName = Path.GetFileNameWithoutExtension(jsonPath);
                }

                if (string.IsNullOrEmpty(definition.name))
                {
                    definition.name = definition.sourceName;
                }

                // Initialize flow config if null
                if (definition.flow == null)
                {
                    definition.flow = new FlowColorConfig();
                }

                return definition;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[StackDefinition] Exception loading JSON {jsonPath}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Attach the loaded layer textures to this definition.
        /// </summary>
        public void SetLayers(StackLayerBundle bundle)
        {
            layers = bundle;
        }

        /// <summary>
        /// Get the current layer bundle.
        /// </summary>
        public StackLayerBundle GetLayers()
        {
            return layers;
        }

        /// <summary>
        /// Prepare/bake the stack data for use.
        /// </summary>
        public void Prepare()
        {
            if (prepared)
            {
                Debug.LogWarning("[StackDefinition] Already prepared.");
                return;
            }

            if (layers.layout == null)
            {
                Debug.LogError("[StackDefinition] Cannot prepare - layers not set.");
                return;
            }

            // Add any preparation logic here (baking, caching, etc.)

            prepared = true;
            Debug.Log($"[StackDefinition] Prepared stack '{sourceName}' from {directory}");
        }

        /// <summary>
        /// Check if this definition has been prepared.
        /// </summary>
        public bool IsPrepared()
        {
            return prepared;
        }

        /// <summary>
        /// Classify a pixel from the flow layer into a gameplay marker type.
        /// </summary>
        public FlowMarkerType ClassifyFlow(Color32 pixel)
        {
            // Pure cyan (0, 255, 255) = ignored/void
            if (Approximately(pixel, new Color32(0, 255, 255, 255)))
                return FlowMarkerType.None;

            // Red = Spawn point
            if (Approximately(pixel, new Color32(255, 0, 0, 255)))
                return FlowMarkerType.Spawn;

            // Yellow = Choke point
            if (Approximately(pixel, new Color32(255, 255, 0, 255)))
                return FlowMarkerType.Choke;

            // Green = Direction arrow
            if (Approximately(pixel, new Color32(0, 255, 0, 255)))
                return FlowMarkerType.Arrow;

            // Blue = Objective marker
            if (Approximately(pixel, new Color32(0, 0, 255, 255)))
                return FlowMarkerType.Objective;

            return FlowMarkerType.None;
        }

        /// <summary>
        /// Classify a pixel from the collision layer into a collision type.
        /// </summary>
        public CollisionKind ClassifyCollision(Color32 pixel)
        {
            // Green = Walkable surface
            if (Approximately(pixel, new Color32(0, 255, 0, 255)))
                return CollisionKind.Walkable;

            // Black/Dark = Blocked (walls)
            if (Approximately(pixel, new Color32(0, 0, 0, 255)))
                return CollisionKind.Blocked;

            // Blue = Climbable (ladders)
            if (Approximately(pixel, new Color32(0, 0, 255, 255)))
                return CollisionKind.Climbable;

            // Red = Destructible
            if (Approximately(pixel, new Color32(255, 0, 0, 255)))
                return CollisionKind.Destructible;

            return CollisionKind.Unknown;
        }

        /// <summary>
        /// Check if two colors are approximately equal within tolerance.
        /// </summary>
        private static bool Approximately(Color32 a, Color32 b, int tolerance = 15)
        {
            return Mathf.Abs(a.r - b.r) <= tolerance
                && Mathf.Abs(a.g - b.g) <= tolerance
                && Mathf.Abs(a.b - b.b) <= tolerance;
        }
    }
}