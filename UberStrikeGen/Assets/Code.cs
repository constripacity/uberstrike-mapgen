#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;

namespace UnityAI
{
    public static class BuildFromStackEnhanced
    {
        public static void BuildFromStack(StackDefinition stackDef)
        {
            if (stackDef == null)
            {
                Debug.LogError("[STACK] Stack definition is null.");
                return;
            }

            var layers = stackDef.GetLayers();
            if (layers.layout == null)
            {
                Debug.LogError("[STACK] Unable to load or validate stack layers.");
                return;
            }

            Debug.Log($"[STACK] Starting multi-layer build: {stackDef.sourceName}");

            var context = new BuildContext(stackDef, layers);

            ProcessLayoutLayer(context);
            ProcessHeightLayer(context);
            ProcessFlowLayer(context);
            ProcessThemeLayer(context);
            ProcessLightingLayer(context);
            ProcessCollisionLayer(context);

            if (stackDef.pairTeleporters)
            {
                PairTeleporters(context);
            }

            if (stackDef.navmesh)
            {
                BakeNavMesh(context);
            }

            OptimizeGeometry(context);
            GenerateQCReport(context);
        }

        private static void ProcessLayoutLayer(BuildContext ctx)
        {
            // Implementation for processing layout layer
        }

        private static void ProcessHeightLayer(BuildContext ctx)
        {
            // Implementation for processing height layer
        }

        private static void ProcessFlowLayer(BuildContext ctx)
        {
            var flowTex = ctx.Layers.flow;
            if (flowTex == null || ctx.StackDef.flow == null)
            {
                return;
            }

            float mpp = ctx.StackDef.metersPerPixel;

            for (int x = 0; x < flowTex.width; x += 4)
            {
                for (int y = 0; y < flowTex.height; y += 4)
                {
                    Color pixel = flowTex.GetPixel(x, y);
                    Vector3 position = new Vector3(x * mpp, 0f, y * mpp);

                    if (IsColorMatch(pixel, ctx.StackDef.flow.spawnColorYellow))
                    {
                        PlaceSpawn(ctx, position, "Neutral");
                    }
                }
            }
        }

        private static void ProcessThemeLayer(BuildContext ctx)
        {
            // Implementation for processing theme layer
        }

        private static void ProcessLightingLayer(BuildContext ctx)
        {
            // Implementation for processing lighting layer
        }

        private static void ProcessCollisionLayer(BuildContext ctx)
        {
            // Implementation for processing collision layer
        }

        private static void PairTeleporters(BuildContext ctx)
        {
            // Implementation for pairing teleporters
        }

        private static void BakeNavMesh(BuildContext ctx)
        {
            // Implementation for baking navmesh
        }

        private static void OptimizeGeometry(BuildContext ctx)
        {
            // Implementation for optimizing geometry
        }

        private static void GenerateQCReport(BuildContext ctx)
        {
            // Implementation for generating quality control report
        }

        private static bool IsColorMatch(Color color, string hex)
        {
            if (!ColorUtility.TryParseHtmlString(hex, out var target))
            {
                return false;
            }

            return Vector3.Distance(new Vector3(color.r, color.g, color.b), new Vector3(target.r, target.g, target.b)) < 0.1f;
        }

        private static void PlaceSpawn(BuildContext ctx, Vector3 position, string team)
        {
            var spawn = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            spawn.name = $"Spawn_{team}_{ctx.SpawnPositions.Count}";
            spawn.transform.SetParent(ctx.Root.transform, false);
            spawn.transform.position = position + Vector3.up;
            spawn.transform.localScale = Vector3.one * 0.8f;

            var renderer = spawn.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = new Material(Shader.Find("Standard"))
                {
                    color = team switch
                    {
                        "Red" => Color.red,
                        "Green" => Color.green,
                        _ => Color.yellow
                    }
                };
            }

            ctx.SpawnPositions.Add(spawn.transform.position);
        }

        private class BuildContext
        {
            public readonly StackDefinition StackDef;
            public readonly StackDefinition.StackLayerBundle Layers;
            public readonly GameObject Root;
            public readonly GameObject GeometryRoot;
            public readonly List<Vector3> SpawnPositions = new List<Vector3>();
            public readonly float StartTime;

            public BuildContext(StackDefinition definition, StackDefinition.StackLayerBundle layerTextures)
            {
                StackDef = definition;
                Layers = layerTextures;
                StartTime = Time.realtimeSinceStartup;
                Root = new GameObject(definition.sourceName ?? "StackBuild");
                GeometryRoot = new GameObject("Geometry");
                GeometryRoot.transform.SetParent(Root.transform, false);
            }
        }
    }
}
#endif
