using UnityEngine;

namespace MapGen.Core
{
    [CreateAssetMenu(fileName = "NewTheme", menuName = "MapGen/Theme Definition")]
    public class ThemeDefinition : ScriptableObject
    {
        [Header("Surfaces")]
        public Material materialFloor;
        public Material materialWall;
        public Material materialGlass;
        public Material materialWater;
    }
}
