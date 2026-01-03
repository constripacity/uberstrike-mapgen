
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UnityAI
{
    public class StackImportWindow : EditorWindow
    {
        private string _jsonPath;
        private StackDefinition _definition;
        private Vector2 _scroll;

        private bool _pairTeleporters = true;
        private bool _bakeNavMesh = true;

        [MenuItem("Tools/UnityAI/Build From Layer Stack…", priority = 100)]
        public static void Open()
        {
            GetWindow<StackImportWindow>(true, "Layer Stack Builder").Show();
        }

        protected virtual void OnEnable()
        {
            titleContent = new GUIContent("Layer Stack Builder");
        }

        protected virtual void OnGUI()
        {
            EditorGUILayout.Space();
            DrawPathSelectorUI();

            if (_definition == null)
            {
                EditorGUILayout.HelpBox("Select a .stack.json file to begin.", MessageType.Info);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawDefinitionDetails();
            EditorGUILayout.EndScrollView();

            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.enabled = _definition != null;
                if (GUILayout.Button("Preview", GUILayout.Height(32f)))
                {
                    StackPreviewer.Preview(_definition);
                }

                if (GUILayout.Button("Build", GUILayout.Height(32f)))
                {
                    BuildFromStackEnhanced.BuildFromStack(_definition);
                }
                GUI.enabled = true;
            }
        }

        private void DrawPathSelectorUI()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Stack JSON", EditorStyles.boldLabel);
                if (GUILayout.Button("Browse…", GUILayout.Width(90f)))
                {
                    string path = EditorUtility.OpenFilePanel("Select Stack Definition", Application.dataPath, "json");
                    if (!string.IsNullOrEmpty(path))
                    {
                        LoadStackDefinition(path);
                    }
                }
            }

            EditorGUI.BeginChangeCheck();
            _jsonPath = EditorGUILayout.TextField(_jsonPath);
            if (EditorGUI.EndChangeCheck() && File.Exists(_jsonPath))
            {
                LoadStackDefinition(_jsonPath);
            }
        }

        private void LoadStackDefinition(string path)
        {
            var def = StackDefinition.LoadFromJSON(path);
            if (def == null)
            {
                _definition = null;
                return;
            }

            var bundle = LoadStackLayers(def);
            def.SetLayers(bundle);

            _jsonPath = path;
            _definition = def;

            _pairTeleporters = def.pairTeleporters;
            _bakeNavMesh = def.navmesh;

            Repaint();
        }

        private StackDefinition.StackLayerBundle LoadStackLayers(StackDefinition def)
        {
            Texture2D LoadTextureFromPath(string path)
            {
                if (string.IsNullOrEmpty(path)) return null;

                string absolutePath = Path.IsPathRooted(path) ? path : Path.Combine(def.directory, path);
                if (!File.Exists(absolutePath)) return null;

                try
                {
                    byte[] imageData = File.ReadAllBytes(absolutePath);
                    var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (texture.LoadImage(imageData))
                    {
                        texture.name = Path.GetFileName(absolutePath);
                        return texture;
                    }
                }
                catch { }
                return null;
            }

            return new StackDefinition.StackLayerBundle
            {
                layout = LoadTextureFromPath(def.layoutPath),
                height = LoadTextureFromPath(def.heightPath),
                flow = LoadTextureFromPath(def.flowPath),
                theme = LoadTextureFromPath(def.themePath),
                lighting = LoadTextureFromPath(def.lightingPath),
                collision = LoadTextureFromPath(def.collisionPath)
            };
        }

        private void DrawDefinitionDetails()
        {
            var def = _definition;

            EditorGUILayout.LabelField("Stack", def.sourceName, EditorStyles.largeLabel);
            EditorGUILayout.LabelField("Directory", def.directory);
            EditorGUILayout.Space();

            def.metersPerPixel = EditorGUILayout.FloatField("Meters Per Pixel", Mathf.Max(0.01f, def.metersPerPixel));
            def.wallHeight = EditorGUILayout.FloatField("Wall Height", Mathf.Max(0.1f, def.wallHeight));
            def.heightScale = EditorGUILayout.FloatField("Height Scale", Mathf.Max(0f, def.heightScale));
            def.stairsRise = EditorGUILayout.FloatField("Stairs Rise", Mathf.Max(0f, def.stairsRise));
            def.rampMaxSlopeDeg = EditorGUILayout.FloatField("Ramp Max Slope (deg)", Mathf.Clamp(def.rampMaxSlopeDeg, 0f, 89f));
            def.doorWidthMeters = EditorGUILayout.FloatField("Door Width (m)", Mathf.Max(0f, def.doorWidthMeters));
            def.bridgeWidthMeters = EditorGUILayout.FloatField("Bridge Width (m)", Mathf.Max(0f, def.bridgeWidthMeters));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Layers", EditorStyles.boldLabel);
            DrawLayerPreviewUI("Layout", def.Layers.layout);
            DrawLayerPreviewUI("Height", def.Layers.height);
            DrawLayerPreviewUI("Flow", def.Layers.flow);
            DrawLayerPreviewUI("Theme", def.Layers.theme);
            DrawLayerPreviewUI("Lighting", def.Layers.lighting);
            DrawLayerPreviewUI("Collision", def.Layers.collision);

            EditorGUILayout.Space();
            _pairTeleporters = EditorGUILayout.Toggle("Pair Teleporters", _pairTeleporters);
            _bakeNavMesh = EditorGUILayout.Toggle("Bake NavMesh", _bakeNavMesh);
        }

        private void DrawLayerPreviewUI(string label, Texture2D texture)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, GUILayout.Width(90f));
                if (texture)
                {
                    Rect rect = GUILayoutUtility.GetRect(96f, 96f, GUILayout.ExpandWidth(false));
                    EditorGUI.DrawPreviewTexture(rect, texture, null, ScaleMode.ScaleToFit);
                    GUILayout.Label($"{texture.width}×{texture.height}", GUILayout.Width(80f));
                }
                else
                {
                    GUILayout.Label("Missing", GUILayout.Width(80f));
                }
            }
        }
    }
}
#endif