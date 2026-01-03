#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityAI;

public static class StackPreviewer
{
    public static void Preview(StackDefinition definition)
    {
        if (definition == null)
        {
            Debug.LogWarning("[StackPreviewer] No stack definition to preview.");
            return;
        }

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        scene.name = $"Preview_{definition.sourceName}";

        var gizmoHost = new GameObject("StackPreviewGizmos");
        var drawer = gizmoHost.AddComponent<StackPreviewDrawer>();
        drawer.Initialize(definition);

        var view = SceneView.lastActiveSceneView;
        if (view != null)
        {
            float size = Mathf.Max(definition.Width, definition.Height) * definition.metersPerPixel;
            view.pivot = new Vector3(0f, size * 0.5f, 0f);
            view.rotation = Quaternion.Euler(45f, 45f, 0f);
            view.size = Mathf.Max(10f, size * 0.75f);
            view.Repaint();
        }

        Debug.Log($"[StackPreviewer] Preview scene ready for {definition.sourceName}.");
    }

    private class StackPreviewDrawer : MonoBehaviour
    {
        private StackDefinition _definition;

        public void Initialize(StackDefinition definition)
        {
            _definition = definition;
        }

        private void OnDrawGizmos()
        {
            if (_definition == null)
                return;

            var layout = _definition.Layers.layout;
            if (!layout)
                return;

            float cell = _definition.metersPerPixel;
            float halfW = layout.width * cell * 0.5f;
            float halfH = layout.height * cell * 0.5f;

            // Draw bounds
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(Vector3.up * (_definition.wallHeight * 0.5f), new Vector3(layout.width * cell, _definition.wallHeight, layout.height * cell));

            var pixels = layout.GetPixels32();
            for (int y = 0; y < layout.height; y++)
            {
                for (int x = 0; x < layout.width; x++)
                {
                    var color = pixels[y * layout.width + x];
                    Vector3 center = new Vector3(x * cell - halfW + cell * 0.5f, 0f, halfH - y * cell - cell * 0.5f);

                    if (Approximately(color, new Color32(0, 0, 0, 255)))
                    {
                        Gizmos.color = Color.black;
                        Gizmos.DrawCube(center + Vector3.up * (_definition.wallHeight * 0.5f), new Vector3(cell, _definition.wallHeight, cell));
                    }
                    else if (Approximately(color, new Color32(64, 64, 64, 255)) || Approximately(color, new Color32(192, 192, 192, 255)))
                    {
                        Gizmos.color = Color.green;
                        Gizmos.DrawCube(center + Vector3.up * 0.02f, new Vector3(cell, 0.04f, cell));
                    }
                    else if (Approximately(color, new Color32(128, 0, 128, 255)))
                    {
                        Gizmos.color = Color.magenta;
                        Gizmos.DrawCube(center + Vector3.up * 0.02f, new Vector3(cell, 0.04f, cell));
                    }
                }
            }

            // Height legend
            Handles.color = Color.white;
            Handles.Label(new Vector3(-halfW, 0f, halfH + 0.5f), $"Height Scale: {_definition.heightScale:F2} m/value");
        }

        private static bool Approximately(Color32 a, Color32 b)
        {
            const int tol = 12;
            return Mathf.Abs(a.r - b.r) <= tol && Mathf.Abs(a.g - b.g) <= tol && Mathf.Abs(a.b - b.b) <= tol;
        }
    }
}
#endif
