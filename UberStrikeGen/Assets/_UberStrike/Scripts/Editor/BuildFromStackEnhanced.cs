#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MapGen;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.Rendering;

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
            var layout = ctx.Layers.layout;
            if (!layout)
            {
                Debug.LogWarning("[STACK] Missing layout layer.");
                return;
            }

            int width = layout.width;
            int height = layout.height;
            var pixels = layout.GetPixels32();

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var pixel = pixels[y * width + x];
                    if (pixel.a < 8)
                    {
                        continue;
                    }

                    if (IsCyan(pixel))
                    {
                        continue;
                    }

                    if (IsWallPixel(pixel))
                    {
                        ctx.MarkWall(x, y);
                    }
                    else if (IsFloorPixel(pixel))
                    {
                        ctx.MarkFloor(x, y);
                    }
                }
            }

            ctx.FloorObject = BuildFloorGeometry(ctx);
            ctx.WallObject = BuildWallGeometry(ctx);
        }

        private static void ProcessHeightLayer(BuildContext ctx)
        {
            var heightTex = ctx.Layers.height;
            if (!heightTex)
            {
                return;
            }

            int width = Math.Min(heightTex.width, Math.Max(1, ctx.Width));
            int heightPx = Math.Min(heightTex.height, Math.Max(1, ctx.Height));
            var px = heightTex.GetPixels32();
            float scale = Mathf.Max(0.01f, ctx.StackDef.heightScale);

            for (int y = 0; y < heightPx; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float normalized = px[y * heightTex.width + x].r / 255f;
                    ctx.HeightField[x, y] = normalized * scale;
                }
            }

            ApplyHeightToFloor(ctx);
            RebuildWalls(ctx);
        }

        private static void ProcessFlowLayer(BuildContext ctx)
        {
            var flowTex = ctx.Layers.flow;
            if (flowTex == null || ctx.StackDef.flow == null)
            {
                return;
            }

            var config = ctx.StackDef.flow;
            var samples = flowTex.GetPixels32();
            int width = flowTex.width;
            int height = flowTex.height;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var pixel = samples[y * width + x];
                    var position = ctx.GridToWorld(x, y, ctx.SampleHeight(x, y));

                    if (IsColorMatch(pixel, config.spawnColorYellow))
                    {
                        PlaceSpawn(ctx, position, "Neutral");
                    }
                    else if (IsColorMatch(pixel, config.spawnColorRed))
                    {
                        PlaceSpawn(ctx, position, "Red");
                    }
                    else if (IsColorMatch(pixel, config.spawnColorGreen))
                    {
                        PlaceSpawn(ctx, position, "Green");
                    }
                    else if (IsColorMatch(pixel, config.chokeColor))
                    {
                        CreateFlowMarker(ctx, position, Color.Lerp(Color.yellow, Color.red, 0.5f), "Choke");
                    }
                    else if (IsColorMatch(pixel, config.arrowColor))
                    {
                        CreateFlowMarker(ctx, position + Vector3.up * 0.2f, Color.cyan, "Arrow");
                    }
                }
            }
        }

        private static void ProcessThemeLayer(BuildContext ctx)
        {
            var themeTex = ctx.Layers.theme;
            List<(Color color, string theme)> generatedSwatches = null;

            if (!themeTex)
            {
                int desiredRegions = Math.Max(3, ctx.StackDef?.themeMap?.Count ?? 0);
                if (desiredRegions == 3 && ctx.ThemeSwatches.Count > 0)
                {
                    desiredRegions = Math.Max(desiredRegions, ctx.ThemeSwatches.Count);
                }
                int seed = Math.Abs((ctx.StackDef?.sourceName ?? "Stack").GetHashCode());
                var result = VoronoiThemeGenerator.GenerateForStack(ctx.StackDef, ctx.Layers.layout, desiredRegions, 1.0f, seed);
                themeTex = result.texture;
                generatedSwatches = result.swatches;
                if (result.themeMap != null && result.themeMap.Count > 0)
                {
                    ctx.StackDef.themeMap = result.themeMap;
                }
            }

            if (generatedSwatches != null && generatedSwatches.Count > 0)
            {
                ctx.ThemeSwatches.Clear();
                ctx.ThemeSwatches.AddRange(generatedSwatches);
            }

            if (!themeTex || ctx.Catalog == null || ctx.ThemeSwatches.Count == 0)
            {
                return;
            }

            var renderers = ctx.GeometryRoot.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return;
            }

            foreach (var renderer in renderers)
            {
                Vector3 center = renderer.bounds.center;
                var cell = ctx.WorldToPixel(center, themeTex.width, themeTex.height);
                if (!ctx.IsWithinTexture(cell.x, cell.y, themeTex.width, themeTex.height))
                {
                    continue;
                }

                var color = themeTex.GetPixel(cell.x, cell.y);
                if (!TryResolveTheme(color, ctx.ThemeSwatches, out var theme))
                {
                    continue;
                }

                if (!ctx.Catalog.ThemeMaterials.TryGetValue(theme, out var mats) || mats.Count == 0)
                {
                    continue;
                }

                renderer.sharedMaterial = mats[(renderer.gameObject.GetInstanceID() & 0x7fffffff) % mats.Count];
            }
        }

        private static void ProcessLightingLayer(BuildContext ctx)
        {
            var lighting = ctx.Layers.lighting;
            if (!lighting)
            {
                return;
            }

            var config = ctx.StackDef.lighting ?? new StackDefinition.LightingConfig();
            Color pointColor = Color.yellow;
            Color spotColor = new Color(1f, 0.6f, 0.3f);
            ColorUtility.TryParseHtmlString(config.pointColor, out pointColor);
            ColorUtility.TryParseHtmlString(config.spotColor, out spotColor);

            for (int y = 0; y < lighting.height; y += 2)
            {
                for (int x = 0; x < lighting.width; x += 2)
                {
                    var pixel = lighting.GetPixel(x, y);
                    var position = ctx.GridToWorld(x, y, ctx.SampleHeight(x, y) + 2f);

                    if (IsApprox(pixel, pointColor))
                    {
                        CreateLight(ctx, LightType.Point, position, pointColor, 12f);
                    }
                    else if (IsApprox(pixel, spotColor))
                    {
                        CreateLight(ctx, LightType.Spot, position + Vector3.up, spotColor, 18f);
                    }
                }
            }

            RenderSettings.fogDensity = Mathf.Max(0f, config.fogDensity);
            if (RenderSettings.sun == null)
            {
                var sun = new GameObject("Stack Sun");
                var light = sun.AddComponent<Light>();
                light.type = LightType.Directional;
                sun.transform.rotation = Quaternion.Euler(config.sunDirDeg[0], config.sunDirDeg[1], config.sunDirDeg[2]);
                RenderSettings.sun = light;
            }
        }

        private static void ProcessCollisionLayer(BuildContext ctx)
        {
            var collision = ctx.Layers.collision;
            if (!collision)
            {
                return;
            }

            var colliders = ctx.GeometryRoot.GetComponentsInChildren<Collider>(true);
            if (colliders.Length == 0)
            {
                return;
            }

            foreach (var collider in colliders)
            {
                Vector3 center = collider.bounds.center;
                var cell = ctx.WorldToPixel(center, collision.width, collision.height);
                if (!ctx.IsWithinTexture(cell.x, cell.y, collision.width, collision.height))
                {
                    continue;
                }

                var pixel = collision.GetPixel(cell.x, cell.y);
                var kind = ctx.StackDef.ClassifyCollision(pixel);
                ApplyCollisionKind(collider, kind);
            }
        }

        private static void PairTeleporters(BuildContext ctx)
        {
            var teleporterObjects = ctx.Root.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name.IndexOf("teleporter", StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(t => t.gameObject)
                .ToList();

            if (teleporterObjects.Count < 2)
            {
                return;
            }

            int group = 1;
            for (int i = 0; i + 1 < teleporterObjects.Count; i += 2)
            {
                var a = teleporterObjects[i];
                var b = teleporterObjects[i + 1];

                var linkA = a.GetComponent<TeleporterLink>() ?? a.AddComponent<TeleporterLink>();
                var linkB = b.GetComponent<TeleporterLink>() ?? b.AddComponent<TeleporterLink>();

                linkA.GroupId = group;
                linkB.GroupId = group;
                linkA.Partner = b.transform;
                linkB.Partner = a.transform;
                group++;
            }

            Debug.Log($"[STACK] Paired {teleporterObjects.Count / 2} teleporter sets.");
        }

        private static void BakeNavMesh(BuildContext ctx)
        {
            var surfaces = ctx.Root.GetComponentsInChildren<NavMeshSurface>(true);
            if (surfaces.Length == 0)
            {
                var surface = ctx.Root.AddComponent<NavMeshSurface>();
                surface.collectObjects = CollectObjects.Children;
                surfaces = new[] { surface };
            }

            foreach (var surface in surfaces)
            {
                surface.BuildNavMesh();
            }

            Debug.Log($"[STACK] Baked NavMesh surfaces: {surfaces.Length}");
        }

        private static void OptimizeGeometry(BuildContext ctx)
        {
            var meshFilters = ctx.GeometryRoot.GetComponentsInChildren<MeshFilter>(true);
            foreach (var mf in meshFilters)
            {
                var renderer = mf.GetComponent<Renderer>();
                if (!renderer)
                {
                    continue;
                }

                var lodGroup = mf.GetComponent<LODGroup>();
                if (!lodGroup)
                {
                    lodGroup = mf.gameObject.AddComponent<LODGroup>();
                    var lods = new LOD[3];
                    lods[0] = new LOD(0.6f, new[] { renderer });
                    lods[1] = new LOD(0.3f, new[] { renderer });
                    lods[2] = new LOD(0.1f, new[] { renderer });
                    lodGroup.SetLODs(lods);
                    lodGroup.RecalculateBounds();
                }
            }

            StaticBatchingUtility.Combine(ctx.Root);
            Debug.Log($"[STACK] Optimized geometry for {meshFilters.Length} meshes.");
        }

        private static void GenerateQCReport(BuildContext ctx)
        {
            var metrics = FlowAnalysisCore.Analyze(ctx.Root);
            var report = new StringBuilder();
            report.AppendLine("=== STACK QC REPORT ===");
            report.AppendLine(metrics.Summary());
            report.AppendLine($"Chokepoints: {metrics.chokepoints.Count}");
            report.AppendLine($"Dead Zones: {metrics.deadZones.Count}");
            report.AppendLine($"Strategic Positions: {metrics.strategicPositions.Count}");

            var dir = "Assets/_UberStrike/Analysis";
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var path = Path.Combine(dir, $"{ctx.StackDef.sourceName}_QC.txt");
            File.WriteAllText(path, report.ToString());
            Debug.Log($"[STACK] QC report written to {path}");
        }

        private static bool IsColorMatch(Color32 color, string hex)
        {
            if (!ColorUtility.TryParseHtmlString(hex, out var target))
            {
                return false;
            }

            var normalized = new Vector3(color.r / 255f, color.g / 255f, color.b / 255f);
            return Vector3.Distance(normalized, new Vector3(target.r, target.g, target.b)) < 0.1f;
        }

        private static void PlaceSpawn(BuildContext ctx, Vector3 position, string team)
        {
            var spawn = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            spawn.name = $"Spawn_{team}_{ctx.SpawnPositions.Count}";
            spawn.transform.SetParent(ctx.SpawnParent, false);
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

        private static void CreateFlowMarker(BuildContext ctx, Vector3 position, Color color, string label)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = $"Flow_{label}_{ctx.FlowMarkers.Count}";
            go.transform.SetParent(ctx.FlowRoot, false);
            go.transform.position = position;
            go.transform.localScale = new Vector3(0.5f, 1f, 0.5f);
            var renderer = go.GetComponent<Renderer>();
            if (renderer)
            {
                renderer.sharedMaterial = new Material(Shader.Find("Standard")) { color = color };
            }

            var collider = go.GetComponent<Collider>();
            if (collider)
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }

            ctx.FlowMarkers.Add(go);
        }

        private class BuildContext
        {
            public readonly StackDefinition StackDef;
            public readonly StackDefinition.StackLayerBundle Layers;
            public readonly GameObject Root;
            public readonly GameObject GeometryRoot;
            public readonly Transform SpawnParent;
            public readonly Transform FlowRoot;
            public readonly List<Vector3> SpawnPositions = new List<Vector3>();
            public readonly List<GameObject> FlowMarkers = new List<GameObject>();
            public readonly float StartTime;
            public readonly int Width;
            public readonly int Height;
            public readonly float CellSize;
            public readonly bool[,] FloorMask;
            public readonly bool[,] WallMask;
            public readonly float[,] HeightField;
            public readonly List<Vector2Int> WallCells = new List<Vector2Int>();
            public readonly List<Vector2Int> FloorCells = new List<Vector2Int>();
            public readonly AssetIntegrationSystem.PrefabCatalogSnapshot Catalog;
            public readonly List<(Color color, string theme)> ThemeSwatches;

            public GameObject FloorObject;
            public GameObject WallObject;

            public BuildContext(StackDefinition definition, StackDefinition.StackLayerBundle layerTextures)
            {
                StackDef = definition;
                Layers = layerTextures;
                StartTime = Time.realtimeSinceStartup;
                Root = new GameObject(definition.sourceName ?? "StackBuild");
                GeometryRoot = new GameObject("Geometry");
                GeometryRoot.transform.SetParent(Root.transform, false);
                SpawnParent = new GameObject("Spawns").transform;
                SpawnParent.SetParent(Root.transform, false);
                FlowRoot = new GameObject("Flow").transform;
                FlowRoot.SetParent(Root.transform, false);
                Width = layerTextures.layout ? layerTextures.layout.width : 0;
                Height = layerTextures.layout ? layerTextures.layout.height : 0;
                CellSize = Mathf.Max(0.25f, definition.metersPerPixel);
                FloorMask = new bool[Width, Height];
                WallMask = new bool[Width, Height];
                HeightField = new float[Math.Max(1, Width), Math.Max(1, Height)];
                Catalog = AssetIntegrationSystem.LoadSnapshot();
                ThemeSwatches = BuildThemeSwatches(definition);
            }

            public Vector2Int WorldToPixel(Vector3 position, int overrideWidth = -1, int overrideHeight = -1)
            {
                int width = overrideWidth > 0 ? overrideWidth : Width;
                int height = overrideHeight > 0 ? overrideHeight : Height;
                width = Math.Max(1, width);
                height = Math.Max(1, height);
                int x = Mathf.Clamp(Mathf.RoundToInt(position.x / CellSize), 0, width - 1);
                int y = Mathf.Clamp(Mathf.RoundToInt(position.z / CellSize), 0, height - 1);
                return new Vector2Int(x, y);
            }

            public Vector3 GridToWorld(int x, int y, float height)
            {
                return new Vector3(x * CellSize, height, y * CellSize);
            }

            public bool IsWithinLayout(int x, int y)
            {
                return x >= 0 && x < Width && y >= 0 && y < Height;
            }

            public bool IsWithinTexture(int x, int y, int width, int height)
            {
                return x >= 0 && x < width && y >= 0 && y < height;
            }

            public float SampleHeight(int x, int y)
            {
                if (Width == 0 || Height == 0)
                {
                    return 0f;
                }

                x = Mathf.Clamp(x, 0, Width - 1);
                y = Mathf.Clamp(y, 0, Height - 1);
                return HeightField[x, y];
            }

            public void MarkFloor(int x, int y)
            {
                if (!IsWithinLayout(x, y))
                {
                    return;
                }

                if (!FloorMask[x, y])
                {
                    FloorMask[x, y] = true;
                    FloorCells.Add(new Vector2Int(x, y));
                }
            }

            public void MarkWall(int x, int y)
            {
                if (!IsWithinLayout(x, y))
                {
                    return;
                }

                if (!WallMask[x, y])
                {
                    WallMask[x, y] = true;
                    WallCells.Add(new Vector2Int(x, y));
                }
            }
        }

        private static List<(Color color, string theme)> BuildThemeSwatches(StackDefinition def)
        {
            var list = new List<(Color color, string theme)>();
            if (def?.themeMap == null)
            {
                return list;
            }

            foreach (var entry in def.themeMap)
            {
                if (ColorUtility.TryParseHtmlString(entry.Key, out var color))
                {
                    list.Add((color, entry.Value));
                }
            }

            return list;
        }

        private static bool IsCyan(Color32 c) => Mathf.Abs(c.r) < 5 && Mathf.Abs(c.g - 255) < 5 && Mathf.Abs(c.b - 255) < 5;

        private static bool IsWallPixel(Color32 c) => c.r < 40 && c.g < 40 && c.b < 40;

        private static bool IsFloorPixel(Color32 c) => c.grayscale > 0.2f;

        private static bool IsApprox(Color a, Color b, float tolerance = 0.15f)
        {
            return Vector3.Distance(new Vector3(a.r, a.g, a.b), new Vector3(b.r, b.g, b.b)) <= tolerance;
        }

        private static void ApplyHeightToFloor(BuildContext ctx)
        {
            if (!ctx.FloorObject)
            {
                return;
            }

            var mf = ctx.FloorObject.GetComponent<MeshFilter>();
            if (!mf || !mf.sharedMesh)
            {
                return;
            }

            var mesh = mf.sharedMesh;
            var vertices = mesh.vertices;
            int maxX = Math.Max(0, ctx.Width - 1);
            int maxY = Math.Max(0, ctx.Height - 1);

            for (int i = 0; i < vertices.Length; i++)
            {
                var local = vertices[i];
                int px = Mathf.Clamp(Mathf.RoundToInt(local.x / ctx.CellSize), 0, maxX);
                int py = Mathf.Clamp(Mathf.RoundToInt(local.z / ctx.CellSize), 0, maxY);
                local.y = ctx.HeightField[px, py];
                vertices[i] = local;
            }

            mesh.vertices = vertices;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var collider = ctx.FloorObject.GetComponent<MeshCollider>();
            if (collider)
            {
                collider.sharedMesh = null;
                collider.sharedMesh = mesh;
            }
        }

        private static void RebuildWalls(BuildContext ctx)
        {
            if (ctx.WallObject)
            {
                UnityEngine.Object.DestroyImmediate(ctx.WallObject);
                ctx.WallObject = null;
            }

            ctx.WallObject = BuildWallGeometry(ctx);
        }

        private static GameObject BuildFloorGeometry(BuildContext ctx)
        {
            if (ctx.FloorCells.Count == 0)
            {
                return null;
            }

            var go = new GameObject("Floor");
            go.transform.SetParent(ctx.GeometryRoot.transform, false);
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            var mesh = GenerateFloorMesh(ctx.FloorMask, ctx.CellSize);
            mf.sharedMesh = mesh;
            mr.sharedMaterial = GetDefaultMaterial(new Color(0.55f, 0.56f, 0.58f));
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            return go;
        }

        private static GameObject BuildWallGeometry(BuildContext ctx)
        {
            if (ctx.WallCells.Count == 0)
            {
                return null;
            }

            var go = new GameObject("Walls");
            go.transform.SetParent(ctx.GeometryRoot.transform, false);
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();

            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var baseMesh = cube.GetComponent<MeshFilter>().sharedMesh;
            UnityEngine.Object.DestroyImmediate(cube);

            var combines = new List<CombineInstance>(ctx.WallCells.Count);
            foreach (var cell in ctx.WallCells)
            {
                float height = ctx.HeightField[cell.x, cell.y] + ctx.StackDef.wallHeight;
                var center = ctx.GridToWorld(cell.x, cell.y, height * 0.5f);
                var scale = new Vector3(ctx.CellSize, height, ctx.CellSize);
                combines.Add(new CombineInstance
                {
                    mesh = baseMesh,
                    transform = Matrix4x4.TRS(center, Quaternion.identity, scale)
                });
            }

            var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            mesh.CombineMeshes(combines.ToArray(), true, true, false);
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();

            mf.sharedMesh = mesh;
            mr.sharedMaterial = GetDefaultMaterial(new Color(0.18f, 0.18f, 0.2f));
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            return go;
        }

        private static Mesh GenerateFloorMesh(bool[,] mask, float cellSize)
        {
            int width = mask.GetLength(0);
            int height = mask.GetLength(1);
            var vertices = new List<Vector3>();
            var triangles = new List<int>();

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (!mask[x, y])
                    {
                        continue;
                    }

                    int baseIndex = vertices.Count;
                    float fx = x * cellSize;
                    float fz = y * cellSize;
                    vertices.Add(new Vector3(fx, 0f, fz));
                    vertices.Add(new Vector3(fx + cellSize, 0f, fz));
                    vertices.Add(new Vector3(fx + cellSize, 0f, fz + cellSize));
                    vertices.Add(new Vector3(fx, 0f, fz + cellSize));

                    triangles.Add(baseIndex + 0);
                    triangles.Add(baseIndex + 2);
                    triangles.Add(baseIndex + 1);
                    triangles.Add(baseIndex + 0);
                    triangles.Add(baseIndex + 3);
                    triangles.Add(baseIndex + 2);
                }
            }

            var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Material GetDefaultMaterial(Color color)
        {
            var mat = new Material(Shader.Find("Standard"))
            {
                color = color
            };
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", Color.black);
            return mat;
        }

        private static bool TryResolveTheme(Color color, List<(Color color, string theme)> swatches, out string theme)
        {
            theme = null;
            if (swatches == null || swatches.Count == 0)
            {
                return false;
            }

            float best = float.MaxValue;
            string bestTheme = null;
            foreach (var swatch in swatches)
            {
                float dist = Vector3.Distance(new Vector3(color.r, color.g, color.b), new Vector3(swatch.color.r, swatch.color.g, swatch.color.b));
                if (dist < best)
                {
                    best = dist;
                    bestTheme = swatch.theme;
                }
            }

            if (bestTheme != null && best < 0.25f)
            {
                theme = bestTheme;
                return true;
            }

            return false;
        }

        private static void CreateLight(BuildContext ctx, LightType type, Vector3 position, Color color, float range)
        {
            var lightGo = new GameObject($"Light_{type}_{ctx.FlowMarkers.Count}");
            lightGo.transform.SetParent(ctx.Root.transform, false);
            lightGo.transform.position = position;
            var light = lightGo.AddComponent<Light>();
            light.type = type;
            light.color = color;
            light.range = range;
            if (type == LightType.Spot)
            {
                light.spotAngle = 55f;
                light.transform.rotation = Quaternion.Euler(75f, 0f, 0f);
            }
        }

        private static void ApplyCollisionKind(Collider collider, CollisionKind kind)
        {
            if (!collider)
            {
                return;
            }

            switch (kind)
            {
                case CollisionKind.Walkable:
                    SetLayer(collider.gameObject, "Walkable");
                    collider.sharedMaterial = GetPhysicMaterial("Walkable", 0.8f, 0.9f);
                    break;
                case CollisionKind.Blocked:
                    SetLayer(collider.gameObject, "Wall");
                    collider.sharedMaterial = GetPhysicMaterial("Wall", 0.2f, 0.2f);
                    break;
                case CollisionKind.Climbable:
                    SetLayer(collider.gameObject, "Climbable");
                    collider.isTrigger = true;
                    break;
                case CollisionKind.Destructible:
                    SetLayer(collider.gameObject, "Destructible");
                    var rb = collider.GetComponent<Rigidbody>() ?? collider.gameObject.AddComponent<Rigidbody>();
                    rb.isKinematic = false;
                    break;
            }
        }

        private static void SetLayer(GameObject go, string layerName)
        {
            int layer = LayerMask.NameToLayer(layerName);
            if (layer >= 0)
            {
                go.layer = layer;
            }
        }

        private static PhysicMaterial GetPhysicMaterial(string name, float dynamicFriction, float staticFriction)
        {
            var mat = new PhysicMaterial($"Stack_{name}")
            {
                dynamicFriction = dynamicFriction,
                staticFriction = staticFriction,
                frictionCombine = PhysicMaterialCombine.Average
            };
            return mat;
        }
    }
}
#endif
