#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UnityAI
{
    public static class MapOptimizer
    {
        [MenuItem("Tools/UberStrike/Optimize Current Map", priority = 240)]
        public static void OptimizeSelection()
        {
            var active = Selection.activeGameObject;
            if (active == null)
            {
                Debug.LogWarning("[MapOptimizer] Select a root object to optimize");
                return;
            }

            OptimizeMap(active);
            Debug.Log("[MapOptimizer] Optimization complete");
        }

        public static void OptimizeMap(GameObject root)
        {
            if (root == null)
                return;

            CombineStaticMeshes(root);
            GenerateLods(root);
            SimplifyColliders(root);
            BakeOcclusionData(root);
            GenerateReport(root);
        }

        private static void CombineStaticMeshes(GameObject root)
        {
            var filters = root.GetComponentsInChildren<MeshFilter>();
            var renderers = root.GetComponentsInChildren<MeshRenderer>();
            if (filters.Length == 0)
                return;

            var combine = new Dictionary<Material, List<CombineInstance>>();
            for (int i = 0; i < filters.Length; i++)
            {
                var filter = filters[i];
                var renderer = filter.GetComponent<MeshRenderer>();
                if (filter.sharedMesh == null || renderer == null)
                    continue;

                foreach (var material in renderer.sharedMaterials)
                {
                    if (!combine.TryGetValue(material, out var list))
                    {
                        list = new List<CombineInstance>();
                        combine[material] = list;
                    }

                    list.Add(new CombineInstance
                    {
                        mesh = filter.sharedMesh,
                        transform = filter.transform.localToWorldMatrix
                    });
                }

                if (Application.isEditor)
                {
                    filter.gameObject.SetActive(false);
                }
            }

            var optimizedRoot = new GameObject("OptimizedStaticGeometry");
            optimizedRoot.transform.SetParent(root.transform);

            foreach (var pair in combine)
            {
                var go = new GameObject($"Combined_{pair.Key?.name ?? "Default"}");
                go.transform.SetParent(optimizedRoot.transform);
                var mf = go.AddComponent<MeshFilter>();
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = pair.Key;

                var mesh = new Mesh();
                mesh.CombineMeshes(pair.Value.ToArray(), true, true);
                mesh.RecalculateNormals();
                mf.sharedMesh = mesh;
            }
        }

        private static void GenerateLods(GameObject root)
        {
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>())
            {
                if (renderer.TryGetComponent(out LODGroup _))
                    continue;

                var lodGroup = renderer.gameObject.AddComponent<LODGroup>();
                var lods = new List<LOD>();

                var lod0 = new LOD(0.6f, new[] { renderer });
                lods.Add(lod0);

                var proxy = Object.Instantiate(renderer.gameObject, renderer.transform);
                proxy.name = renderer.name + "_LOD1";
                var proxyRenderer = proxy.GetComponent<MeshRenderer>();
                var proxyFilter = proxy.GetComponent<MeshFilter>();
                if (proxyRenderer != null && proxyFilter != null && proxyFilter.sharedMesh != null)
                {
                    var simplified = Object.Instantiate(proxyFilter.sharedMesh);
                    MeshUtility.Optimize(simplified);
                    proxyFilter.sharedMesh = simplified;
                    lods.Add(new LOD(0.25f, new[] { proxyRenderer }));
                }

                lodGroup.SetLODs(lods.ToArray());
                lodGroup.RecalculateBounds();
            }
        }

        private static void SimplifyColliders(GameObject root)
        {
            foreach (var collider in root.GetComponentsInChildren<MeshCollider>())
            {
                if (collider.sharedMesh == null)
                    continue;

                var mesh = collider.sharedMesh;
                if (!mesh.isReadable)
                    continue;

                if (mesh.vertexCount > 1000)
                {
                    var simplified = new Mesh();
                    simplified.vertices = mesh.vertices;
                    simplified.triangles = mesh.triangles;
                    simplified.RecalculateBounds();
                    collider.sharedMesh = simplified;
                }
            }
        }

        private static void BakeOcclusionData(GameObject root)
        {
            // placeholder for occlusion – in-editor we can mark static and rely on Unity's occlusion bake
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>())
            {
                GameObjectUtility.SetStaticEditorFlags(renderer.gameObject, StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            }
        }

        private static void GenerateReport(GameObject root)
        {
            string folder = Path.Combine(Application.dataPath, "_UberStrike/OptimizationReports");
            Directory.CreateDirectory(folder);
            string reportPath = Path.Combine(folder, $"optimization_{root.name}.txt");

            var filters = root.GetComponentsInChildren<MeshFilter>();
            int vertexCount = 0;
            foreach (var filter in filters)
            {
                if (filter.sharedMesh != null)
                    vertexCount += filter.sharedMesh.vertexCount;
            }

            File.WriteAllText(reportPath, $"MeshFilters: {filters.Length}\nTotal Vertices: {vertexCount}\nGenerated: {System.DateTime.Now:O}");
            AssetDatabase.Refresh();
        }
    }
}
#endif
