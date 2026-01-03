#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityAI
{
    public class AdaptiveLODGenerator : EditorWindow
    {
        [System.Serializable]
        public class LODSettings
        {
            public float[] screenRelativeTransitions = { 0.6f, 0.3f, 0.15f, 0.05f };
            public float[] qualityReductions = { 1.0f, 0.5f, 0.25f, 0.1f };
            public bool preserveBorders = true;
            public bool preserveUVs = true;
            public float maxError = 0.01f;
        }

        private GameObject targetMap;
        private LODSettings settings = new();
        private bool useImportanceMap = true;

        [MenuItem("Tools/UberStrike/MapGen/Adaptive LOD Generator")]
        public static void ShowWindow()
        {
            GetWindow<AdaptiveLODGenerator>("LOD Generator");
        }

        private void OnGUI()
        {
            GUILayout.Label("Adaptive LOD Generator", EditorStyles.boldLabel);
            targetMap = (GameObject)EditorGUILayout.ObjectField("Target Map", targetMap, typeof(GameObject), true);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("LOD Levels", EditorStyles.boldLabel);
            for (int i = 0; i < settings.screenRelativeTransitions.Length; i++)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"LOD {i}:", GUILayout.Width(50));
                settings.screenRelativeTransitions[i] = EditorGUILayout.Slider("Screen %", settings.screenRelativeTransitions[i], 0.01f, 1f);
                settings.qualityReductions[i] = EditorGUILayout.Slider("Quality", settings.qualityReductions[i], 0.01f, 1f);
                EditorGUILayout.EndHorizontal();
            }

            settings.preserveBorders = EditorGUILayout.Toggle("Preserve Borders", settings.preserveBorders);
            settings.preserveUVs = EditorGUILayout.Toggle("Preserve UVs", settings.preserveUVs);
            useImportanceMap = EditorGUILayout.Toggle("Use Importance Map", useImportanceMap);
            EditorGUILayout.Space();

            if (GUILayout.Button("Generate LODs", GUILayout.Height(30)))
            {
                GenerateLODs();
            }

            if (GUILayout.Button("Auto-Optimize All Maps"))
            {
                OptimizeAllMaps();
            }
        }

        private void GenerateLODs()
        {
            if (!targetMap)
            {
                EditorUtility.DisplayDialog("Error", "Please select a map", "OK");
                return;
            }

            var meshFilters = targetMap.GetComponentsInChildren<MeshFilter>();
            int processed = 0;
            Texture2D importanceMap = useImportanceMap ? CreateImportanceMap(targetMap) : null;

            foreach (var mf in meshFilters)
            {
                if (!mf.sharedMesh)
                    continue;

                var lodGroup = mf.GetComponent<LODGroup>() ?? mf.gameObject.AddComponent<LODGroup>();
                var lodMeshes = GenerateLODMeshes(mf.sharedMesh, GetImportance(mf.transform.position, importanceMap));
                SetupLODGroup(lodGroup, mf, lodMeshes);
                processed++;
            }

            Debug.Log($"Generated LODs for {processed} meshes");
            EditorUtility.SetDirty(targetMap);
        }

        private List<Mesh> GenerateLODMeshes(Mesh original, float importance)
        {
            var lods = new List<Mesh> { original };
            for (int i = 1; i < settings.qualityReductions.Length; i++)
            {
                float quality = settings.qualityReductions[i] * (0.5f + importance * 0.5f);
                var simplified = SimplifyMesh(lods[i - 1], quality);
                if (simplified != null && simplified.triangles.Length > 10)
                {
                    lods.Add(simplified);
                }
                else
                {
                    break;
                }
            }

            return lods;
        }

        private Mesh SimplifyMesh(Mesh input, float quality)
        {
            var meshSimplifier = new UnityMeshSimplifier.MeshSimplifier();
            meshSimplifier.Initialize(input);
            meshSimplifier.SimplifyMesh(quality);
            var simplified = meshSimplifier.ToMesh();
            simplified.name = $"{input.name}_LOD_{quality:F2}";
            MeshUtility.Optimize(simplified);
            return simplified;
        }

        private void SetupLODGroup(LODGroup lodGroup, MeshFilter original, List<Mesh> lodMeshes)
        {
            var lods = new LOD[lodMeshes.Count];
            for (int i = 0; i < lodMeshes.Count; i++)
            {
                GameObject lodObj;
                MeshFilter mf;
                MeshRenderer mr;

                if (i == 0)
                {
                    lodObj = original.gameObject;
                    mf = original;
                    mr = original.GetComponent<MeshRenderer>();
                }
                else
                {
                    lodObj = original.transform.Find($"LOD{i}")?.gameObject ?? new GameObject($"LOD{i}");
                    lodObj.transform.SetParent(original.transform, false);
                    mf = lodObj.GetComponent<MeshFilter>() ?? lodObj.AddComponent<MeshFilter>();
                    mr = lodObj.GetComponent<MeshRenderer>() ?? lodObj.AddComponent<MeshRenderer>();
                    mr.sharedMaterials = original.GetComponent<MeshRenderer>().sharedMaterials;
                    mf.sharedMesh = lodMeshes[i];
                    lodObj.SetActive(false);
                }

                lods[i] = new LOD(settings.screenRelativeTransitions[i], new Renderer[] { mr });
            }

            lodGroup.SetLODs(lods);
            lodGroup.RecalculateBounds();
        }

        private Texture2D CreateImportanceMap(GameObject map)
        {
            var bounds = GetMapBounds(map);
            int resolution = 256;
            var importanceMap = new Texture2D(resolution, resolution, TextureFormat.RFloat, false)
            {
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new float[resolution * resolution];
            var spawns = GameObject.FindGameObjectsWithTag("Spawn");
            var items = GameObject.FindGameObjectsWithTag("Item");
            var chokepoints = GameObject.FindGameObjectsWithTag("Chokepoint");

            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    Vector3 worldPos = new(
                        Mathf.Lerp(bounds.min.x, bounds.max.x, x / (float)resolution),
                        bounds.center.y,
                        Mathf.Lerp(bounds.min.z, bounds.max.z, y / (float)resolution)
                    );

                    float importance = 0.3f;
                    foreach (var spawn in spawns)
                    {
                        float dist = Vector3.Distance(worldPos, spawn.transform.position);
                        if (dist < 30f)
                            importance = Mathf.Max(importance, 1f - dist / 30f);
                    }

                    foreach (var item in items)
                    {
                        float dist = Vector3.Distance(worldPos, item.transform.position);
                        if (dist < 20f)
                            importance = Mathf.Max(importance, 0.7f - dist / 40f);
                    }

                    foreach (var choke in chokepoints)
                    {
                        float dist = Vector3.Distance(worldPos, choke.transform.position);
                        if (dist < 20f)
                            importance = Mathf.Max(importance, 0.9f - dist / 20f);
                    }

                    pixels[y * resolution + x] = importance;
                }
            }

            importanceMap.SetPixelData(pixels, 0);
            importanceMap.Apply();
            return importanceMap;
        }

        private float GetImportance(Vector3 position, Texture2D importanceMap)
        {
            if (!importanceMap)
                return 0.5f;

            float u = Mathf.InverseLerp(-128f, 128f, position.x);
            float v = Mathf.InverseLerp(-128f, 128f, position.z);
            int x = Mathf.Clamp(Mathf.RoundToInt(u * importanceMap.width), 0, importanceMap.width - 1);
            int y = Mathf.Clamp(Mathf.RoundToInt(v * importanceMap.height), 0, importanceMap.height - 1);
            return importanceMap.GetPixel(x, y).r;
        }

        private Bounds GetMapBounds(GameObject map)
        {
            var renderers = map.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return new Bounds(Vector3.zero, Vector3.one * 1f);
            var bounds = renderers[0].bounds;
            foreach (var r in renderers)
            {
                bounds.Encapsulate(r.bounds);
            }

            return bounds;
        }

        private void OptimizeAllMaps()
        {
            var mapPrefabs = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_UberStrike/Maps" });
            foreach (var guid in mapPrefabs)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (!prefab)
                    continue;
                targetMap = prefab;
                GenerateLODs();
                PrefabUtility.SavePrefabAsset(prefab);
            }

            Debug.Log($"Optimized {mapPrefabs.Length} maps");
        }
    }

    // Simplified mesh simplifier; in production replace with a robust quadric error library.
    public static class UnityMeshSimplifier
    {
        public class MeshSimplifier
        {
            private Mesh mesh;

            public void Initialize(Mesh input)
            {
                mesh = Object.Instantiate(input);
            }

            public void SimplifyMesh(float quality)
            {
                // Placeholder: hook up a real simplifier for production use.
                // Quality is ignored here, but kept for API symmetry.
                MeshUtility.Optimize(mesh);
            }

            public Mesh ToMesh()
            {
                return mesh;
            }
        }
    }
}
#endif
