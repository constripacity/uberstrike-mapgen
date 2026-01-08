using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.IO;
using MapGen.Core;

namespace MapGen.Editor
{
    public class MapGenWindow : EditorWindow
    {
        private string stackPath;
        private StackDefinition currentStack;

        [MenuItem("MapGen/Generator Window")]
        public static void ShowWindow()
        {
            GetWindow<MapGenWindow>("MapGen");
        }

        private ThemeDefinition theme;
        private string vocabPath = "Assets/MapGen/Documentation/UberUnityExtract/ubervocab.json";

        private void OnGUI()
        {
            GUILayout.Label("Map Generator V2", EditorStyles.boldLabel);

            if (GUILayout.Button("Load Stack JSON"))
            {
                string path = EditorUtility.OpenFilePanel("Select Stack JSON", "", "json");
                if (!string.IsNullOrEmpty(path))
                {
                    LoadStack(path);
                }
            }
            
            theme = (ThemeDefinition)EditorGUILayout.ObjectField("Theme", theme, typeof(ThemeDefinition), false);

            if (currentStack != null)
            {
                GUILayout.Label($"Source: {currentStack.sourceName}");
                GUILayout.Label($"Resolution: {currentStack.Layers?.layout?.width}x{currentStack.Layers?.layout?.height}");

                if (GUILayout.Button("Build Greybox"))
                {
                    Build();
                }
            }
        }

        private void LoadStack(string path)
        {
            stackPath = path;
            try
            {
                string json = File.ReadAllText(path);
                currentStack = StackDefinition.FromJson(json);
                currentStack.directory = Path.GetDirectoryName(path);
                
                // Load Textures
                currentStack.Layers = new StackDefinition.StackLayerBundle();
                currentStack.Layers.layout = LoadTexture(currentStack.layoutPath);
                currentStack.Layers.flow = LoadTexture(currentStack.flowPath);
                
                Debug.Log($"Loaded stack: {currentStack.sourceName}");
                
                // Load Vocab
                if (File.Exists(vocabPath)) UberVocab.Load(vocabPath);
                else Debug.LogWarning("UberVocab not found: " + vocabPath);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Failed to load stack: {ex}");
            }
        }

        private Texture2D LoadTexture(string relPath)
        {
            if (string.IsNullOrEmpty(relPath)) return null;
            string absPath = Path.Combine(currentStack.directory, relPath);
            if (File.Exists(absPath))
            {
                byte[] bytes = File.ReadAllBytes(absPath);
                Texture2D tex = new Texture2D(2, 2);
                tex.LoadImage(bytes); 
                return tex;
            }
            return null;
        }

        private void Build()
        {
            // Auto Create Default Theme if none
            if (theme == null) {
                var guids = AssetDatabase.FindAssets("t:ThemeDefinition DefaultTheme_URP");
                if (guids.Length > 0) 
                    theme = AssetDatabase.LoadAssetAtPath<ThemeDefinition>(AssetDatabase.GUIDToAssetPath(guids[0]));
            }

            var newScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            
            var builder = new GreyboxBuilder();
            var root = builder.Generate(currentStack, theme);
            
            if (root != null)
            {
                string savePath = $"Assets/_Generated/Maps/{currentStack.sourceName}.unity";
                Directory.CreateDirectory(Path.GetDirectoryName(savePath));
                EditorSceneManager.SaveScene(newScene, savePath);
                Debug.Log($"Map saved to {savePath}");
            }
        }
    }
}
