#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MapGen {
  public static class CLI {
    // Unity.exe -batchmode -quit -projectPath . -executeMethod MapGen.CLI.Run -seed 42 -size 128 -t 2
    public static void Run() {
      var args = Environment.GetCommandLineArgs();
      int seed = GetInt(args,"-seed",1337), size=GetInt(args,"-size",96), t=GetInt(args,"-t",2);
      var tex=MapGenerator.GenerateLegend(size,size,seed,t);
      var dir="Assets/_Generated/Maps/CLI"; Directory.CreateDirectory(dir);
      var legendPath=Path.Combine(dir,$"legend_{seed}_{size}.png"); TextureIO.SavePng(tex,legendPath);
      MapFromLegend.BuildFromLegend(legendPath,null,"Generated_CLI");
      Debug.Log($"CLI complete seed={seed} size={size} legend={legendPath}");
      EditorApplication.Exit(0);
    }
    static int GetInt(string[] a,string key,int def){ for(int i=0;i<a.Length-1;i++) if(a[i]==key && int.TryParse(a[i+1],out var v)) return v; return def; }
  }
}
#endif
