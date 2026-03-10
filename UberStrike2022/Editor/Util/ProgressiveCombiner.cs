#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace UnityAI
{
    /// <summary>
    /// Utility for combining large numbers of meshes progressively to avoid Unity's vertex limit.
    /// Combines meshes in chunks, then combines the chunks.
    /// </summary>
    public static class ProgressiveCombiner
    {
        private const int DEFAULT_CHUNK_SIZE = 1000;
        private const int MAX_VERTICES_PER_CHUNK = 60000;

        /// <summary>Tracks how many stage-1 chunks were created in the last Combine operation.</summary>
        public static int LastStage1Chunks { get; private set; }

        /// <summary>Combines a large list of CombineInstances into a single mesh using a two-stage process.</summary>
        public static Mesh Combine(List<CombineInstance> instances, int chunkSize = DEFAULT_CHUNK_SIZE)
        {
            if (instances == null || instances.Count == 0)
            {
                LastStage1Chunks = 0;
                return null;
            }

            if (chunkSize <= 0)
            {
                chunkSize = DEFAULT_CHUNK_SIZE;
            }

            if (instances.Count <= chunkSize)
            {
                LastStage1Chunks = 1;
                var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                mesh.CombineMeshes(instances.ToArray(), true, true);
                return mesh;
            }

            var chunkMeshes = new List<Mesh>();
            var chunkCount = 0;

            for (int i = 0; i < instances.Count; i += chunkSize)
            {
                int count = Mathf.Min(chunkSize, instances.Count - i);
                var chunkInstances = new CombineInstance[count];
                for (int j = 0; j < count; j++)
                {
                    chunkInstances[j] = instances[i + j];
                }

                var chunkMesh = new Mesh { name = $"Chunk_{chunkCount}", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                chunkMesh.CombineMeshes(chunkInstances, true, true);

                if (chunkMesh.vertexCount > MAX_VERTICES_PER_CHUNK)
                {
                    Debug.LogWarning($"[ProgressiveCombiner] Chunk {chunkCount} has {chunkMesh.vertexCount} vertices (may cause issues)");
                }

                chunkMeshes.Add(chunkMesh);
                chunkCount++;
            }

            LastStage1Chunks = chunkCount;

            if (chunkMeshes.Count == 1)
            {
                return chunkMeshes[0];
            }

            var finalCombines = new CombineInstance[chunkMeshes.Count];
            for (int i = 0; i < chunkMeshes.Count; i++)
            {
                finalCombines[i] = new CombineInstance { mesh = chunkMeshes[i], transform = Matrix4x4.identity };
            }

            var finalMesh = new Mesh { name = "CombinedMesh", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            finalMesh.CombineMeshes(finalCombines, true, true);

            foreach (var chunk in chunkMeshes)
            {
                if (chunk != finalMesh)
                {
                    Object.DestroyImmediate(chunk);
                }
            }

            Debug.Log($"[ProgressiveCombiner] Combined {instances.Count} instances into {chunkCount} chunks, final mesh has {finalMesh.vertexCount} vertices");

            return finalMesh;
        }
    }
}
#endif
