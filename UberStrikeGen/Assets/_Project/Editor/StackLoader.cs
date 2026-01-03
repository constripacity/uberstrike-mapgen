#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace UnityAI
{
    /// <summary>
    /// Loads a StackDefinition JSON and its six layer images (.png or .png.txt base64),
    /// validates them, and attaches the bundle to the definition.
    /// </summary>
    public static class StackLoader
    {
        /// <summary>
        /// Load the stack JSON + all layers, validate, and prepare the definition.
        /// </summary>
        public static bool TryLoad(string jsonPath, out StackDefinition definition)
        {
            definition = null;

            if (string.IsNullOrEmpty(jsonPath))
            {
                Debug.LogError("[StackLoader] JSON path is null or empty.");
                return false;
            }

            if (!File.Exists(jsonPath))
            {
                Debug.LogError($"[StackLoader] JSON file not found: {jsonPath}");
                return false;
            }

            // Use the project's StackDefinition loader (JsonUtility-based)
            var def = StackDefinition.LoadFromJSON(jsonPath);
            if (def == null)
            {
                Debug.LogError("[StackLoader] Failed to parse stack definition.");
                return false;
            }

            // Load textures from the paths embedded/derived in the definition
            var bundle = new StackDefinition.StackLayerBundle
            {
                layout = LoadTexture(def.directory, def.layoutPath),
                height = LoadTexture(def.directory, def.heightPath),
                flow = LoadTexture(def.directory, def.flowPath),
                theme = LoadTexture(def.directory, def.themePath),
                lighting = LoadTexture(def.directory, def.lightingPath),
                collision = LoadTexture(def.directory, def.collisionPath)
            };

            // Validate presence and dimensions
            if (!ValidateBundle(bundle, out string err))
            {
                Debug.LogError($"[StackLoader] {err}");
                DestroyBundle(bundle);
                return false;
            }

            // Attach and bake
            def.SetLayers(bundle);
            def.Prepare();

            definition = def;
            return true;
        }

        /// <summary>
        /// Loads all layer textures referenced by the definition and returns them in a dictionary.
        /// </summary>
        public static Dictionary<string, Texture2D> LoadStackLayers(StackDefinition definition)
        {
            if (definition == null)
            {
                Debug.LogError("[StackLoader] Cannot load layers for a null definition.");
                return null;
            }

            var bundle = new StackDefinition.StackLayerBundle
            {
                layout = LoadTexture(definition.directory, definition.layoutPath),
                height = LoadTexture(definition.directory, definition.heightPath),
                flow = LoadTexture(definition.directory, definition.flowPath),
                theme = LoadTexture(definition.directory, definition.themePath),
                lighting = LoadTexture(definition.directory, definition.lightingPath),
                collision = LoadTexture(definition.directory, definition.collisionPath)
            };

            // Validate the loaded layers
            if (!ValidateBundle(bundle, out string err))
            {
                Debug.LogError($"[StackLoader] {err}");
                DestroyBundle(bundle);
                return null;
            }

            definition.SetLayers(bundle);
            definition.Prepare();

            return BundleToDictionary(bundle);
        }

        /// <summary>
        /// Validate a dictionary of layer textures loaded by <see cref="LoadStackLayers"/> or <see cref="TryLoad"/>.
        /// </summary>
        public static bool ValidateLayers(Dictionary<string, Texture2D> layers)
        {
            return ValidateLayers(layers, out _);
        }

        /// <summary>
        /// Validate a dictionary of layer textures loaded by <see cref="LoadStackLayers"/> or <see cref="TryLoad"/>.
        /// </summary>
        public static bool ValidateLayers(Dictionary<string, Texture2D> layers, out string error)
        {
            if (layers == null)
            {
                error = "Layer dictionary is null.";
                return false;
            }

            if (!layers.TryGetValue("layout", out var layout) || layout == null)
            {
                error = "Layout layer is missing.";
                return false;
            }

            var bundle = new StackDefinition.StackLayerBundle
            {
                layout = layout,
                height = layers.TryGetValue("height", out var height) ? height : null,
                flow = layers.TryGetValue("flow", out var flow) ? flow : null,
                theme = layers.TryGetValue("theme", out var theme) ? theme : null,
                lighting = layers.TryGetValue("lighting", out var lighting) ? lighting : null,
                collision = layers.TryGetValue("collision", out var collision) ? collision : null
            };

            return ValidateBundle(bundle, out error);
        }

        // --- Helpers ---
        private static Dictionary<string, Texture2D> BundleToDictionary(in StackDefinition.StackLayerBundle bundle)
        {
            return new Dictionary<string, Texture2D>
            {
                ["layout"] = bundle.layout,
                ["height"] = bundle.height,
                ["flow"] = bundle.flow,
                ["theme"] = bundle.theme,
                ["lighting"] = bundle.lighting,
                ["collision"] = bundle.collision
            };
        }

        private static Texture2D LoadTexture(string defDirectory, string pathFromDef)
        {
            // Accept absolute JSON-provided path; else resolve relative to JSON directory.
            string path = pathFromDef;
            if (!Path.IsPathRooted(path) && !string.IsNullOrEmpty(defDirectory))
                path = Path.Combine(defDirectory, pathFromDef ?? string.Empty);

            // Try binary .png first, then base64 in .txt fallback
            if (!TryReadImageData(path, out var data, out var usedPath))
            {
                Debug.LogError($"[StackLoader] Missing layer file: {path} (and {path}.txt)");
                return null;
            }

            try
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    name = Path.GetFileName(usedPath)
                };

                if (!ImageConversion.LoadImage(tex, data, markNonReadable: false))
                {
                    Debug.LogError($"[StackLoader] Failed to decode image: {usedPath}");
                    UnityEngine.Object.DestroyImmediate(tex);
                    return null;
                }

                return tex;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[StackLoader] Exception loading texture {usedPath}: {ex.Message}");
                return null;
            }
        }

        private static bool TryReadImageData(string primaryPath, out byte[] data, out string usedPath)
        {
            usedPath = primaryPath;
            data = null;

            // 1) Raw image file
            if (File.Exists(primaryPath))
            {
                data = File.ReadAllBytes(primaryPath);
                return true;
            }

            // 2) Base64 fallback in .txt (same path + ".txt")
            string txt = primaryPath + ".txt";
            if (!File.Exists(txt))
                return false;

            try
            {
                usedPath = txt;
                string raw = File.ReadAllText(txt);
                string normalized = raw.Replace("\r", string.Empty).Replace("\n", string.Empty).Trim();
                data = Convert.FromBase64String(normalized);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[StackLoader] Failed to decode base64 for {txt}: {ex.Message}");
                data = null;
                return false;
            }
        }

        private static bool ValidateBundle(in StackDefinition.StackLayerBundle b, out string error)
        {
            error = null;

            if (b.layout == null) { error = "Layout layer is missing."; return false; }
            if (b.height == null) { error = "Height layer is missing."; return false; }
            if (b.flow == null) { error = "Flow layer is missing."; return false; }
            if (b.theme == null) { error = "Theme layer is missing."; return false; }
            if (b.lighting == null) { error = "Lighting layer is missing."; return false; }
            if (b.collision == null) { error = "Collision layer is missing."; return false; }

            int w = b.layout.width, h = b.layout.height;

            // Check height dimension
            if (b.height.width != w || b.height.height != h)
            {
                error = $"Layer 'height' has mismatched size {b.height.width}x{b.height.height} (expected {w}x{h}).";
                return false;
            }

            // Check flow dimension
            if (b.flow.width != w || b.flow.height != h)
            {
                error = $"Layer 'flow' has mismatched size {b.flow.width}x{b.flow.height} (expected {w}x{h}).";
                return false;
            }

            // Check theme dimension
            if (b.theme.width != w || b.theme.height != h)
            {
                error = $"Layer 'theme' has mismatched size {b.theme.width}x{b.theme.height} (expected {w}x{h}).";
                return false;
            }

            // Check lighting dimension
            if (b.lighting.width != w || b.lighting.height != h)
            {
                error = $"Layer 'lighting' has mismatched size {b.lighting.width}x{b.lighting.height} (expected {w}x{h}).";
                return false;
            }

            // Check collision dimension
            if (b.collision.width != w || b.collision.height != h)
            {
                error = $"Layer 'collision' has mismatched size {b.collision.width}x{b.collision.height} (expected {w}x{h}).";
                return false;
            }

            return true;
        }

        private static void DestroyBundle(StackDefinition.StackLayerBundle bundle)
        {
            if (bundle.layout) UnityEngine.Object.DestroyImmediate(bundle.layout);
            if (bundle.height) UnityEngine.Object.DestroyImmediate(bundle.height);
            if (bundle.flow) UnityEngine.Object.DestroyImmediate(bundle.flow);
            if (bundle.theme) UnityEngine.Object.DestroyImmediate(bundle.theme);
            if (bundle.lighting) UnityEngine.Object.DestroyImmediate(bundle.lighting);
            if (bundle.collision) UnityEngine.Object.DestroyImmediate(bundle.collision);
        }
    }
}
#endif
