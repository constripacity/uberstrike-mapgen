using System.Collections.Generic;
using UnityEngine;

namespace MapGen.Core
{
    public static class GreedyMesher
    {
        public struct MeshQuad
        {
            public Vector3 position;
            public Vector3 size;
        }

        public static List<MeshQuad> Optimize(bool[] grid, int width, int height, float mpp, float itemHeight)
        {
            List<MeshQuad> quads = new List<MeshQuad>();
            bool[] visited = new bool[grid.Length];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x;
                    if (!grid[i] || visited[i]) continue;

                    // 1. Find width of this strip
                    int w = 1;
                    while (x + w < width && grid[i + w] && !visited[i + w])
                    {
                        w++;
                    }

                    // 2. Find height (can we extend this strip down?)
                    int h = 1;
                    while (y + h < height)
                    {
                        // Check if the next row has a matching segment of width 'w'
                        bool rowMatch = true;
                        for (int k = 0; k < w; k++)
                        {
                            int ni = (y + h) * width + (x + k);
                            if (!grid[ni] || visited[ni])
                            {
                                rowMatch = false;
                                break;
                            }
                        }
                        if (rowMatch) h++;
                        else break;
                    }

                    // 3. Mark visited
                    for (int dy = 0; dy < h; dy++)
                    {
                        for (int dx = 0; dx < w; dx++)
                        {
                            visited[(y + dy) * width + (x + dx)] = true;
                        }
                    }

                    // 4. Create Quad Information
                    // Center of the merged block
                    float centerX = (x + w * 0.5f - width * 0.5f) * mpp;
                    float centerY = (y + h * 0.5f - height * 0.5f) * mpp; // This is actually Z in 3D

                    // Position (Y is up)
                    // If it's a floor, Y=0. If wall, Y=Height/2? 
                    // Let's assume the caller handles the Y offset based on itemHeight.
                    // For walls, we usually want them centered on the Y axis if they are cubes.
                    // But here we return the CENTER position.
                    
                    Vector3 pos = new Vector3(centerX, 0, centerY); 
                    Vector3 size = new Vector3(w * mpp, itemHeight, h * mpp);
                    
                    quads.Add(new MeshQuad { position = pos, size = size });
                }
            }
            return quads;
        }
    }
}
