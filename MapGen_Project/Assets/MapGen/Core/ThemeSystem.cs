using UnityEngine;

namespace MapGen.Core
{
    public static class ThemeSystem
    {
        public static void Apply(GameObject root, ThemeDefinition theme)
        {
            if (theme == null) return;

            Transform walls = root.transform.Find("Walls");
            if (walls != null)
            {
                foreach (var r in walls.GetComponentsInChildren<Renderer>())
                    SetMat(r, theme.materialWall);
            }

            Transform floors = root.transform.Find("Floors");
            if (floors != null)
            {
                foreach (var r in floors.GetComponentsInChildren<Renderer>())
                    SetMat(r, theme.materialFloor);
            }
            
            Transform glass = root.transform.Find("Glass");
            if (glass != null)
            {
                foreach (var r in glass.GetComponentsInChildren<Renderer>())
                    SetMat(r, theme.materialGlass);
            }
            
            Transform water = root.transform.Find("Water");
            if (water != null)
            {
                foreach (var r in water.GetComponentsInChildren<Renderer>())
                    SetMat(r, theme.materialWater);
            }
        }

        private static void SetMat(Renderer r, Material m)
        {
            if (m != null) r.sharedMaterial = m;
        }
    }
}
