#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
namespace MapGen {
  public static class MeshBuilders {
    public static Mesh BuildFloor(bool[,] floor, float cell=Legend.CellSizeMeters) {
      int w=floor.GetLength(0), h=floor.GetLength(1);
      var verts=new List<Vector3>(); var tris=new List<int>();
      for (int y=0;y<h;y++) for (int x=0;x<w;x++) if (floor[x,y]) {
        int b=verts.Count; float fx=x*cell, fz=y*cell;
        verts.Add(new(fx,0,fz)); verts.Add(new(fx+cell,0,fz));
        verts.Add(new(fx+cell,0,fz+cell)); verts.Add(new(fx,0,fz+cell));
        tris.Add(b+0); tris.Add(b+2); tris.Add(b+1); tris.Add(b+0); tris.Add(b+3); tris.Add(b+2);
      }
      var m=new Mesh{indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};
      m.SetVertices(verts); m.SetTriangles(tris,0); m.RecalculateNormals(); m.RecalculateBounds(); return m;
    }
    public static List<Mesh> BuildPerimeterWalls(bool[,] floor, float hgt, float cell=Legend.CellSizeMeters) {
      int w=floor.GetLength(0), h=floor.GetLength(1); var list=new List<Mesh>();
      var dirs=new (int dx,int dy)[]{(-1,0),(1,0),(0,-1),(0,1)};
      for (int y=0;y<h;y++) for (int x=0;x<w;x++) if (floor[x,y]) {
        foreach (var d in dirs) {
          int nx=x+d.dx, ny=y+d.dy; bool neighbor=nx>=0&&nx<w&&ny>=0&&ny<h&&floor[nx,ny]; if (neighbor) continue;
          var m=new Mesh(); var v=new List<Vector3>(); var t=new List<int>();
          Vector3 p0=new(x*cell,0,y*cell); if (d.dx==1) p0+=new Vector3(cell,0,0); if (d.dy==1) p0+=new Vector3(0,0,cell);
          Vector3 right = (d.dx!=0)? Vector3.forward*cell : Vector3.right*cell; Vector3 up = Vector3.up*hgt;
          v.Add(p0); v.Add(p0+right); v.Add(p0+right+up); v.Add(p0+up); t.AddRange(new[]{0,2,1,0,3,2});
          m.SetVertices(v); m.SetTriangles(t,0); m.RecalculateNormals(); m.RecalculateBounds(); list.Add(m);
        }
      }
      return list;
    }
  }
}
#endif
