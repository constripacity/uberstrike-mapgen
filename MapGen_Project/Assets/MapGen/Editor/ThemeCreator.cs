using UnityEngine;
using UnityEditor;
using System.IO;
using MapGen.Core;

namespace MapGen.Editor
{
    public static class ThemeCreator
    {
        [MenuItem("MapGen/Utilities/Create Default Theme")]
        public static void CreateDefaultTheme()
        {
            string root = "Assets/MapGen/Resources/DefaultTheme";
            if (!Directory.Exists(root)) Directory.CreateDirectory(root);

            // Create Materials
            Material mFloor = CreateMat(root, "Mat_Floor", new Color(0.5f, 0.5f, 0.5f)); // Grey
            Material mWall = CreateMat(root, "Mat_Wall", new Color(0.2f, 0.2f, 0.2f));   // Dark Grey
            Material mGlass = CreateMat(root, "Mat_Glass", new Color(0, 1, 1, 0.3f), true);
            Material mWater = CreateMat(root, "Mat_Water", new Color(0, 0, 1, 0.5f), true);

            // Create Theme
            ThemeDefinition theme = ScriptableObject.CreateInstance<ThemeDefinition>();
            theme.name = "DefaultTheme_URP";
            theme.materialFloor = mFloor;
            theme.materialWall = mWall;
            theme.materialGlass = mGlass;
            theme.materialWater = mWater;

            string path = Path.Combine(root, "DefaultTheme_URP.asset");
            AssetDatabase.CreateAsset(theme, path);
            AssetDatabase.SaveAssets();
            
            EditorUtility.FocusProjectWindow();
            Selection.activeObject = theme;
            
            Debug.Log($"[MapGen] Created Default Theme at {path}");
        }

        private static Material CreateMat(string root, string name, Color color, bool transparent = false)
        {
            // Use URP Lit if available, else Standard
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");

            Material mat = new Material(shader);
            mat.name = name;
            mat.color = color; // BaseColor for Standard, but URP uses _BaseColor
            
            if (shader.name.Contains("Universal"))
            {
                mat.SetColor("_BaseColor", color);
                mat.SetFloat("_Smoothness", 0.5f);
                if (transparent) {
                    mat.SetFloat("_Surface", 1); // Transparent
                    mat.SetInt("_ZWrite", 0);
                    mat.renderQueue = 3000;
                    mat.SetShaderPassEnabled("ShadowCaster", false);
                }
            }
            else
            {
                if (transparent) {
                    mat.SetInt("_Mode", 3); // Transparent
                    mat.renderQueue = 3000;
                }
            }

            string path = Path.Combine(root, name + ".mat");
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }
    }
}
