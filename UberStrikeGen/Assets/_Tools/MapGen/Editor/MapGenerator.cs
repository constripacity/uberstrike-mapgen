#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MapGen {
  public static class MapGenerator {
    [MenuItem("Tools/UberStrike/MapGen/Quick Generate (64)")]
    static void Quick(){
      var tex=GenerateLegend(64,64,1337,2);
      var dir="Assets/_Generated/Maps/Quick"; Directory.CreateDirectory(dir);
      TextureIO.SavePng(tex, Path.Combine(dir,$"legend_{DateTime.Now:HHmmss}.png"));
    }

    public static Texture2D GenerateLegend(int w,int h,int seed,int telePairs=2){
      var rng=new System.Random(seed); var grid=new Color32[w*h];
      for (int i=0;i<grid.Length;i++) grid[i]=Legend.Wall;

      var rooms=new List<RectInt>(); var root=new RectInt(2,2,w-4,h-4); var stack=new Stack<RectInt>(); stack.Push(root);
      while(stack.Count>0 && rooms.Count<8){
        var r=stack.Pop(); if(r.width<10||r.height<10){ rooms.Add(r); continue; }
        bool horiz=(r.width<r.height) ^ (rng.NextDouble()<0.5);
        if(horiz){ int y=UnityEngine.Random.Range(r.yMin+6,r.yMax-6);
          stack.Push(new RectInt(r.xMin,r.yMin,r.width,y-r.yMin));
          stack.Push(new RectInt(r.xMin,y,r.width,r.yMax-y));
        } else { int x=UnityEngine.Random.Range(r.xMin+6,r.xMax-6);
          stack.Push(new RectInt(r.xMin,r.yMin,x-r.xMin,r.height));
          stack.Push(new RectInt(x,r.yMin,r.xMax-x,r.height));
        }
      }
      void Set(int x,int y, Color32 c)=> grid[y*w+x]=c;
      foreach(var r in rooms) for(int y=r.yMin+1;y<r.yMax-1;y++) for(int x=r.xMin+1;x<r.xMax-1;x++) Set(x,y,Legend.Floor);
      for(int i=0;i<rooms.Count-1;i++){ var a=rooms[i].center; var b=rooms[i+1].center;
        for(int x=Math.Min(a.x,b.x);x<=Math.Max(a.x,b.x);x++) Set(x,a.y,Legend.Floor);
        for(int y=Math.Min(a.y,b.y);y<=Math.Max(a.y,b.y);y++) Set(b.x,y,Legend.Floor);
      }

      var floorCells=new List<Vector2Int>();
      for(int y=1;y<h-1;y++) for(int x=1;x<w-1;x++) if(Legend.Equals(grid[y*w+x],Legend.Floor)) floorCells.Add(new(x,y));
      void Stamp(Color32 v){ var c=floorCells[UnityEngine.Random.Range(0,floorCells.Count)]; Set(c.x,c.y,v); }

      for(int i=0;i<UnityEngine.Random.Range(8,15);i++) Stamp(Legend.Spawn);
      for(int i=0;i<6;i++) Stamp(Legend.Health); for(int i=0;i<4;i++) Stamp(Legend.Armor); for(int i=0;i<4;i++) Stamp(Legend.JumpPad);
      for(int p=0;p<telePairs;p++){ Stamp(Legend.Teleporter); Stamp(Legend.Teleporter); }

      var tex=new Texture2D(w,h,TextureFormat.RGBA32,false,true); tex.SetPixels32(grid); tex.Apply(); return tex;
    }
  }
}
#endif
