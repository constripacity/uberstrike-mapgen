#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityAI
{
    [CreateAssetMenu(fileName = "UberStrikeAssetCatalog", menuName = "UberStrike/Asset Catalog")]
    public class AssetCatalog : ScriptableObject
    {
        [System.Serializable]
        public class PrefabEntry
        {
            public string name;
            public GameObject prefab;
            public string category;
            public float spawnWeight = 1f;
            public string assetPath;
        }

        [System.Serializable]
        public class ThemeMaterialBucket
        {
            public string theme;
            public List<Material> materials = new List<Material>();
        }

        public List<PrefabEntry> weapons = new List<PrefabEntry>();
        public List<PrefabEntry> pickups = new List<PrefabEntry>();
        public List<PrefabEntry> gameplay = new List<PrefabEntry>();
        public List<ThemeMaterialBucket> themeMaterials = new List<ThemeMaterialBucket>();

        private const string CatalogPath = "Assets/_UberStrike/Data/UberStrikeAssetCatalog.asset";
        private static AssetCatalog _instance;

        public static AssetCatalog Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = AssetDatabase.LoadAssetAtPath<AssetCatalog>(CatalogPath);
                }

                return _instance;
            }
        }

        public Dictionary<string, List<Material>> ToMaterialLookup()
        {
            var lookup = new Dictionary<string, List<Material>>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var bucket in themeMaterials)
            {
                if (bucket == null || string.IsNullOrEmpty(bucket.theme))
                {
                    continue;
                }

                if (!lookup.TryGetValue(bucket.theme, out var list))
                {
                    list = new List<Material>();
                    lookup[bucket.theme] = list;
                }

                if (bucket.materials != null)
                {
                    list.AddRange(bucket.materials.Where(m => m));
                }
            }

            return lookup;
        }
    }

    public class AssetCatalogBuilder : EditorWindow
    {
        private AssetCatalog catalog;
        private Vector2 scrollPos;
        private string assetRootPath = "Assets/UberStrike";

        [MenuItem("Tools/UberStrike/Asset Catalog Builder")]
        public static void ShowWindow()
        {
            GetWindow<AssetCatalogBuilder>("Asset Catalog Builder");
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("UberStrike Asset Catalog", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Place the UberStrike assets under Assets/UberStrike/ then scan to build a persistent catalog.", MessageType.Info);

            assetRootPath = EditorGUILayout.TextField("Asset Root Path", assetRootPath);
            catalog = (AssetCatalog)EditorGUILayout.ObjectField("Catalog Asset", catalog, typeof(AssetCatalog), false);

            if (GUILayout.Button("Create New Catalog"))
            {
                CreateNewCatalog();
            }

            using (new EditorGUI.DisabledScope(catalog == null))
            {
                if (GUILayout.Button("Scan UberStrike Assets", GUILayout.Height(28)))
                {
                    ScanAssets();
                }
            }

            if (catalog != null)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Catalog Contents", EditorStyles.boldLabel);
                scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.Height(140));
                EditorGUILayout.LabelField($"Weapons: {catalog.weapons.Count}");
                EditorGUILayout.LabelField($"Pickups: {catalog.pickups.Count}");
                EditorGUILayout.LabelField($"Gameplay: {catalog.gameplay.Count}");
                EditorGUILayout.LabelField($"Themes: {catalog.themeMaterials.Count}");
                EditorGUILayout.EndScrollView();

                if (GUILayout.Button("Save Catalog"))
                {
                    EditorUtility.SetDirty(catalog);
                    AssetDatabase.SaveAssets();
                    Debug.Log("[AssetCatalog] Saved catalog asset.");
                }
            }
        }

        private void CreateNewCatalog()
        {
            const string dir = "Assets/_UberStrike/Data";
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var asset = CreateInstance<AssetCatalog>();
            AssetDatabase.CreateAsset(asset, "Assets/_UberStrike/Data/UberStrikeAssetCatalog.asset");
            AssetDatabase.SaveAssets();
            catalog = asset;
            Debug.Log("[AssetCatalog] Created new catalog asset.");
        }

        private void ScanAssets()
        {
            if (catalog == null)
            {
                return;
            }

            catalog.weapons.Clear();
            catalog.pickups.Clear();
            catalog.gameplay.Clear();
            catalog.themeMaterials.Clear();

            ScanPrefabsInto(catalog.weapons, new[]
            {
                Path.Combine(assetRootPath, "Prefabs/Weapons"),
                Path.Combine(assetRootPath, "Weapons")
            }, "weapon");

            ScanPrefabsInto(catalog.pickups, new[]
            {
                Path.Combine(assetRootPath, "Prefabs/Pickups"),
                Path.Combine(assetRootPath, "Items")
            }, "pickup");

            ScanPrefabsInto(catalog.gameplay, new[]
            {
                Path.Combine(assetRootPath, "Prefabs/Gameplay"),
                Path.Combine(assetRootPath, "Prefabs/Props")
            }, "gameplay");

            ScanMaterials();

            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log($"[AssetCatalog] Scan complete. Weapons={catalog.weapons.Count} Pickups={catalog.pickups.Count} Themes={catalog.themeMaterials.Count}");
        }

        private void ScanPrefabsInto(List<AssetCatalog.PrefabEntry> list, IEnumerable<string> paths, string category)
        {
            foreach (var path in paths)
            {
                if (!Directory.Exists(path))
                {
                    continue;
                }

                var guids = AssetDatabase.FindAssets("t:Prefab", new[] { path });
                foreach (var guid in guids)
                {
                    var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                    if (prefab == null)
                    {
                        continue;
                    }

                    list.Add(new AssetCatalog.PrefabEntry
                    {
                        name = prefab.name,
                        prefab = prefab,
                        category = category,
                        spawnWeight = 1f,
                        assetPath = assetPath
                    });
                }
            }
        }

        private void ScanMaterials()
        {
            string[] materialPaths =
            {
                Path.Combine(assetRootPath, "Materials/Themes"),
                Path.Combine(assetRootPath, "Materials")
            };

            foreach (var path in materialPaths)
            {
                if (!Directory.Exists(path))
                {
                    continue;
                }

                var guids = AssetDatabase.FindAssets("t:Material", new[] { path });
                foreach (var guid in guids)
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    var material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
                    if (!material)
                    {
                        continue;
                    }

                    string theme = ResolveThemeName(assetPath);
                    var bucket = catalog.themeMaterials.FirstOrDefault(b => b.theme.Equals(theme, System.StringComparison.OrdinalIgnoreCase));
                    if (bucket == null)
                    {
                        bucket = new AssetCatalog.ThemeMaterialBucket { theme = theme };
                        catalog.themeMaterials.Add(bucket);
                    }

                    if (!bucket.materials.Contains(material))
                    {
                        bucket.materials.Add(material);
                    }
                }
            }
        }

        private static string ResolveThemeName(string assetPath)
        {
            if (assetPath.IndexOf("Industrial", System.StringComparison.OrdinalIgnoreCase) >= 0) return "Industrial";
            if (assetPath.IndexOf("Warehouse", System.StringComparison.OrdinalIgnoreCase) >= 0) return "Warehouse";
            if (assetPath.IndexOf("SciFi", System.StringComparison.OrdinalIgnoreCase) >= 0) return "SciFi";
            if (assetPath.IndexOf("Tech", System.StringComparison.OrdinalIgnoreCase) >= 0) return "Tech";
            if (assetPath.IndexOf("Outdoor", System.StringComparison.OrdinalIgnoreCase) >= 0) return "Outdoor";
            return "Default";
        }
    }
}
#endif
