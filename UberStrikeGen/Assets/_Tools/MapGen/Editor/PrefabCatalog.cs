#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
namespace MapGen {
  public class PrefabCatalog : ScriptableObject {
    public GameObject SpawnPoint, JumpPad, Teleporter, PickupHealth, PickupArmor;
    public Material WaterMat, GlassMat;
    public float WallHeight = 4f;
    public static PrefabCatalog LoadOrCreate() {
      const string path = "Assets/_Tools/MapGen/Editor/PrefabCatalog.asset";
      var asset = AssetDatabase.LoadAssetAtPath<PrefabCatalog>(path);
      if (!asset) { asset = CreateInstance<PrefabCatalog>(); AssetDatabase.CreateAsset(asset, path); AssetDatabase.SaveAssets(); }
      return asset;
    }
  }
  public static class PrefabCatalogMenu {
    [MenuItem("Tools/UberStrike/MapGen/Open Prefab Catalog")]
    static void Open(){ var cat = PrefabCatalog.LoadOrCreate(); Selection.activeObject = cat; EditorGUIUtility.PingObject(cat); }
  }
}
#endif
