using System;
using UnityEngine;

namespace MapGen.Core
{
    [Serializable]
    public class StackDefinition
    {
        public string sourceName = "New Map";
        public string directory = ".";
        public string layoutPath;
        public string heightPath;
        public string flowPath;
        public string themePath;
        public string lightingPath;
        public string collisionPath;

        public float metersPerPixel = 0.2f;
        public float wallHeight = 4.0f;
        public float heightScale = 1.0f;
        
        public bool navmesh = true;
        public bool pairTeleporters = true;

        /// <summary>
        /// Runtime helper to load textures (Not implemented in Data class to keep it pure if possible, 
        /// but needed for builder).
        /// </summary>
        public StackLayerBundle Layers;

        public class StackLayerBundle
        {
            public Texture2D layout;
            public Texture2D height;
            public Texture2D flow;
            public Texture2D theme;
            public Texture2D lighting;
            public Texture2D collision;
        }

        public static StackDefinition FromJson(string json)
        {
            return JsonUtility.FromJson<StackDefinition>(json);
        }
    }
}
