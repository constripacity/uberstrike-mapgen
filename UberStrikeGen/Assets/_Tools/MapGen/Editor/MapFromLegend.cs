#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace MapGen {
  public static class MapFromLegend {
    [MenuItem("Tools/UberStrike/MapGen/Import From Legend PNG...")]
    static void ImportMenu(){
      var path = EditorUtility.OpenFilePanel("Legend PNG", Application.dataPath, "png");
      if (!string.IsNullOrEmpty(path)) BuildFromLegend(ToAssetsPath(path), null, "Generated");
    }
    static string ToAssetsPath(string p)=> p.StartsWith("Assets/")? p : "Assets"+p.Replace(Application.dataPath,"");

    public static void BuildFromLegend(string legendPngPath, string? heightPngPath, string parentName){
      var cat = PrefabCatalog.LoadOrCreate();
      var tex = TextureIO.LoadPng(legendPngPath);
      int w,h; var px = TextureIO.GetPixels(tex, out w, out h);

      var floor=new bool[w,h];
      var spawns=new List<Vector2Int>(); var jumps=new(); var teles=new(); var heals=new(); var armors=new(); var water=new(); var glass=new();

      int k=0; for(int y=0;y<h;y++) for(int x=0;x<w;x++,k++){
        var c=px[k];
        if (Legend.Equals(c, Legend.Floor)) floor[x,y]=true;
        else if (Legend.Equals(c, Legend.Spawn)) { floor[x,y]=true; spawns.Add(new(x,y)); }
        else if (Legend.Equals(c, Legend.JumpPad)) { floor[x,y]=true; jumps.Add(new(x,y)); }
        else if (Legend.Equals(c, Legend.Teleporter)) { floor[x,y]=true; teles.Add(new(x,y)); }
        else if (Legend.Equals(c, Legend.Health)) { floor[x,y]=true; heals.Add(new(x,y)); }
        else if (Legend.Equals(c, Legend.Armor)) { floor[x,y]=true; armors.Add(new(x,y)); }
        else if (Legend.Equals(c, Legend.Water)) water.Add(new(x,y));
        else if (Legend.Equals(c, Legend.GlassBridge)) { glass.Add(new(x,y)); floor[x,y]=true; }
      }

      var root = new GameObject($"{parentName}_{Path.GetFileNameWithoutExtension(legendPngPath)}");
      var staticRoot = new GameObject("Static"); staticRoot.transform.SetParent(root.transform);

      var floorGo = new GameObject("Floor"); floorGo.transform.SetParent(staticRoot.transform);
      var mf = floorGo.AddComponent<MeshFilter>(); mf.sharedMesh = MeshBuilders.BuildFloor(floor);
      floorGo.AddComponent<MeshRenderer>();

      var wallsRoot = new GameObject("Walls"); wallsRoot.transform.SetParent(staticRoot.transform);
      var walls = MeshBuilders.BuildPerimeterWalls(floor, cat.WallHeight);
      int wi=0; foreach(var wm in walls){
        var go=new GameObject($"Wall_{wi++}"); go.transform.SetParent(wallsRoot.transform);
        go.AddComponent<MeshFilter>().sharedMesh=wm; go.AddComponent<MeshRenderer>();
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.NavigationStatic);
      }

      Vector3 WPos(Vector2Int c)=> new(c.x*Legend.CellSizeMeters+0.5f,0,c.y*Legend.CellSizeMeters+0.5f);

      void Place(GameObject prefab, IEnumerable<Vector2Int> cells, string group){
        if(!prefab) return; var g=new GameObject(group); g.transform.SetParent(root.transform);
        foreach(var c in cells) PrefabUtility.InstantiatePrefab(prefab, g.transform).As<GameObject>().transform.position=WPos(c);
      }

      Place(cat.SpawnPoint, spawns, "Spawns");
      Place(cat.JumpPad, jumps, "JumpPads");
      Place(cat.PickupHealth, heals, "Health");
      Place(cat.PickupArmor, armors, "Armor");

      if (cat.Teleporter && teles.Count>=2) {
        var tgroup=new GameObject("Teleporters"); tgroup.transform.SetParent(root.transform);
        var list=new List<Vector2Int>(teles);
        while(list.Count>=2){
          var a=list[0]; int best=1; float bestD=float.MaxValue;
          for(int i=1;i<list.Count;i++){ float d=Vector2Int.Distance(a,list[i]); if(d<bestD){bestD=d; best=i;} }
          var b=list[best]; list.RemoveAt(best); list.RemoveAt(0);
          var A=(GameObject)PrefabUtility.InstantiatePrefab(cat.Teleporter, tgroup.transform);
          var B=(GameObject)PrefabUtility.InstantiatePrefab(cat.Teleporter, tgroup.transform);
          A.transform.position=WPos(a); B.transform.position=WPos(b);
        }
      }

      void Tile(IEnumerable<Vector2Int> cells, string name, Material mat, float y=0f){
        if(!mat) return; var g=new GameObject(name); g.transform.SetParent(staticRoot.transform);
        foreach(var c in cells){ var quad=GameObject.CreatePrimitive(PrimitiveType.Quad);
          quad.name=name+"_tile"; quad.transform.SetParent(g.transform);
          quad.transform.position=WPos(c)+new Vector3(0,y,0); quad.transform.rotation=Quaternion.Euler(90,0,0);
          quad.transform.localScale=new Vector3(Legend.CellSizeMeters, Legend.CellSizeMeters,1);
          quad.GetComponent<MeshRenderer>().sharedMaterial=mat; Object.DestroyImmediate(quad.GetComponent<MeshCollider>());}
      }

      Tile(water,"Water",cat.WaterMat,-0.1f);
      Tile(glass,"GlassBridge",cat.GlassMat,0.01f);

      var surface = Object.FindFirstObjectByType<NavMeshSurface>(); if (surface) surface.BuildNavMesh();
      Selection.activeGameObject=root; Debug.Log($"MapGen: Imported legend {w}x{h} from {legendPngPath}");
    }
    static T As<T>(this Object o) where T:Object => (T)o;
  }
}
#endif
