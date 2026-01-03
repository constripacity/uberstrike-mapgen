#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityAI;

public struct FlowAnalysisResult
{
    public int spawnCount;
    public int chokePixels;
    public float minSpawnDistance;
    public bool hasDeadEnds;
}

public static class FlowAnalyser
{
    public static FlowAnalysisResult Analyse(StackDefinition definition)
    {
        var result = new FlowAnalysisResult
        {
            spawnCount = 0,
            chokePixels = 0,
            minSpawnDistance = float.MaxValue,
            hasDeadEnds = false
        };

        if (definition == null || !definition.Layers.flow)
            return result;

        var flowTex = definition.Layers.flow;
        var layout = definition.Layers.layout;
        var pixels = flowTex.GetPixels32();
        float cell = definition.metersPerPixel;
        float halfW = flowTex.width * cell * 0.5f;
        float halfH = flowTex.height * cell * 0.5f;

        var spawnPositions = new List<Vector3>();
        for (int y = 0; y < flowTex.height; y++)
        {
            for (int x = 0; x < flowTex.width; x++)
            {
                var col = pixels[y * flowTex.width + x];
                var marker = definition.ClassifyFlow(col);
                if (marker == FlowMarkerType.None)
                    continue;

                Vector3 pos = new Vector3(x * cell - halfW + cell * 0.5f, 0f, halfH - y * cell - cell * 0.5f);
                if (marker == FlowMarkerType.Choke)
                {
                    result.chokePixels++;
                }
                else if (marker == FlowMarkerType.Arrow)
                {
                    // ignore for now
                }
                else
                {
                    spawnPositions.Add(pos);
                    result.spawnCount++;
                }
            }
        }

        for (int i = 0; i < spawnPositions.Count; i++)
        {
            for (int j = i + 1; j < spawnPositions.Count; j++)
            {
                float dist = Vector3.Distance(spawnPositions[i], spawnPositions[j]);
                if (dist < result.minSpawnDistance)
                {
                    result.minSpawnDistance = dist;
                }
            }
        }

        if (result.minSpawnDistance == float.MaxValue)
            result.minSpawnDistance = 0f;

        if (layout)
        {
            var layoutPixels = layout.GetPixels32();
            bool[,] walkable = new bool[layout.width, layout.height];
            for (int y = 0; y < layout.height; y++)
            {
                for (int x = 0; x < layout.width; x++)
                {
                    var lp = layoutPixels[y * layout.width + x];
                    walkable[x, y] = Approximately(lp, new Color32(64, 64, 64, 255)) || Approximately(lp, new Color32(192, 192, 192, 255)) || Approximately(lp, new Color32(128, 0, 128, 255));
                }
            }

            result.hasDeadEnds = DetectDeadEnds(walkable);
        }

        if (result.spawnCount < 2)
        {
            Debug.LogWarning("[FlowAnalyser] Less than two spawn markers detected in flow layer.");
        }

        if (result.chokePixels < 4)
        {
            Debug.LogWarning("[FlowAnalyser] Chokepoint density is low (<4 pixels). Consider adding more choke coverage.");
        }

        if (result.hasDeadEnds)
        {
            Debug.LogWarning("[FlowAnalyser] Potential dead ends detected in layout.");
        }

        if (result.minSpawnDistance < definition.doorWidthMeters)
        {
            Debug.LogWarning($"[FlowAnalyser] Spawn markers are closer than {definition.doorWidthMeters:F1}m (min {result.minSpawnDistance:F2}m).");
        }

        Debug.Log($"[FlowAnalyser] Spawns: {result.spawnCount}, Choke pixels: {result.chokePixels}, Min spawn distance: {result.minSpawnDistance:F2}m");
        return result;
    }

    private static bool DetectDeadEnds(bool[,] walkable)
    {
        int width = walkable.GetLength(0);
        int height = walkable.GetLength(1);
        bool deadEndFound = false;

        for (int y = 1; y < height - 1; y++)
        {
            for (int x = 1; x < width - 1; x++)
            {
                if (!walkable[x, y])
                    continue;

                int neighbors = 0;
                if (walkable[x + 1, y]) neighbors++;
                if (walkable[x - 1, y]) neighbors++;
                if (walkable[x, y + 1]) neighbors++;
                if (walkable[x, y - 1]) neighbors++;

                if (neighbors <= 1)
                {
                    deadEndFound = true;
                }
            }
        }

        return deadEndFound;
    }

    private static bool Approximately(Color32 a, Color32 b)
    {
        const int tol = 15;
        return Mathf.Abs(a.r - b.r) <= tol && Mathf.Abs(a.g - b.g) <= tol && Mathf.Abs(a.b - b.b) <= tol;
    }
}
#endif
