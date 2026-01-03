#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MapGen {
  public class MapGeneratorWindow : EditorWindow {
    int size=96, seed=1337, telePairs=2;
    [MenuItem("Tools/UberStrike/MapGen/Generator Window")]
    static void Open()=> GetWindow<MapGeneratorWindow>("UberStrike MapGen");
    void OnGUI(){
      GUILayout.Label("Parameters", EditorStyles.boldLabel);
      size = EditorGUILayout.IntSlider("Size (px)", size, 48, 256);
      seed = EditorGUILayout.IntField("Seed", seed);
      telePairs = EditorGUILayout.IntSlider("Teleporter Pairs", telePairs, 0, 4);

      if(GUILayout.Button("Generate Legend PNG")){
        var tex=MapGenerator.GenerateLegend(size,size,seed,telePairs);
        var dir="Assets/_Generated/Maps/Editor"; Directory.CreateDirectory(dir);
        var path=Path.Combine(dir,$"legend_{seed}_{size}.png"); TextureIO.SavePng(tex,path);
        Selection.activeObject=AssetDatabase.LoadAssetAtPath<Object>(path); Debug.Log("Legend saved: "+path);
      }
      if(GUILayout.Button("Build Scene from Selected Legend")){
        var obj=Selection.activeObject as Texture2D;
        string path = obj? AssetDatabase.GetAssetPath(obj) : EditorUtility.OpenFilePanel("Legend PNG", Application.dataPath,"png");
        if(!string.IsNullOrEmpty(path)){ if(!path.StartsWith("Assets/")) path="Assets"+path.Replace(Application.dataPath,"");
          MapFromLegend.BuildFromLegend(path,null,"Generated"); } else Debug.LogWarning("Select a legend Texture2D or choose a PNG.");
      }
    }
  }
}
#endif
