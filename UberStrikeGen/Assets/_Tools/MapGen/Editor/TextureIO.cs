#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
namespace MapGen {
  public static class TextureIO {
    public static Texture2D LoadPng(string path) {
      var bytes = File.ReadAllBytes(path);
      var tex = new Texture2D(2,2,TextureFormat.RGBA32,false,true);
      ImageConversion.LoadImage(tex, bytes, false);
      tex.Apply(); return tex;
    }
    public static void SavePng(Texture2D tex, string path) {
      var bytes = tex.EncodeToPNG();
      Directory.CreateDirectory(Path.GetDirectoryName(path)!);
      File.WriteAllBytes(path, bytes);
      AssetDatabase.Refresh();
    }
    public static Color32[] GetPixels(Texture2D tex, out int w, out int h) { w=tex.width; h=tex.height; return tex.GetPixels32(); }
  }
}
#endif
