#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace MapGen {
  public static class MapQC {
    public struct Result {
      public bool navmeshOk;
      public float navmeshCoveragePercent;
      public int numSpawns;
      public int redSpawns;
      public int blueSpawns;
      public int greenSpawns;
      public float spawnBalanceScore;
      public int estimatedSniperLanes;
      public float avgLongLoS;
      public float longestLoS;
      public int chokepointCount;
      public float triangleCount;
      public int drawCalls;
      public float performanceCost;
    }

    public static Result Run(GameObject root){
      if(!root) return default;
      return Analyze(new[]{root});
    }

    public static Result Run(Scene scene){
      var roots = scene.GetRootGameObjects();
      return Analyze(roots);
    }

    static Result Analyze(IReadOnlyList<GameObject> roots){
      var result = new Result();
      if (roots == null || roots.Count == 0) return result;

      var renderers = CollectComponents<Renderer>(roots);
      var meshFilters = CollectComponents<MeshFilter>(roots);
      var spawnTransforms = CollectComponents<Transform>(roots, t => t.name.ToLowerInvariant().Contains("spawn"));

      var bounds = CalculateBounds(renderers);
      result.drawCalls = renderers.Count;
      result.triangleCount = CalculateTriangles(meshFilters);
      result.performanceCost = Mathf.Round((result.triangleCount / 15000f + result.drawCalls / 100f) * 100f) / 100f;

      var nav = NavMesh.CalculateTriangulation();
      if (nav.vertices != null && nav.vertices.Length >= 3)
      {
        float navArea = CalculateNavArea(nav);
        float groundArea = Mathf.Max(bounds.size.x * bounds.size.z, 1f);
        result.navmeshCoveragePercent = Mathf.Clamp(navArea / groundArea * 100f, 0f, 100f);
        result.navmeshOk = navArea > 1f;
      }

      AnalyzeSpawns(spawnTransforms, ref result);

      var sightline = SampleSightLines(bounds, 48);
      result.avgLongLoS = sightline.avg;
      result.longestLoS = sightline.max;
      result.estimatedSniperLanes = sightline.lanes;

      result.chokepointCount = EstimateChokepoints(bounds, 16);
      return result;
    }

    static List<T> CollectComponents<T>(IReadOnlyList<GameObject> roots, System.Func<T,bool> filter = null) where T : Component {
      var list = new List<T>();
      foreach (var root in roots){
        if(!root) continue;
        var components = root.GetComponentsInChildren<T>(true);
        foreach(var comp in components){
          if (filter == null || filter(comp)) list.Add(comp);
        }
      }
      return list;
    }

    static Bounds CalculateBounds(List<Renderer> renderers){
      if(renderers == null || renderers.Count == 0) return new Bounds(Vector3.zero, Vector3.one * 10f);
      var bounds = renderers[0].bounds;
      for(int i=1;i<renderers.Count;i++) bounds.Encapsulate(renderers[i].bounds);
      return bounds;
    }

    static float CalculateTriangles(List<MeshFilter> filters){
      double total = 0;
      foreach(var filter in filters){
        if(!filter || filter.sharedMesh==null) continue;
        total += filter.sharedMesh.triangles.Length / 3.0;
      }
      return (float)total;
    }

    static float CalculateNavArea(NavMeshTriangulation nav){
      float area = 0f;
      var indices = nav.indices;
      var verts = nav.vertices;
      for(int i=0;i<indices.Length;i+=3){
        if(i+2>=indices.Length) break;
        var a = verts[indices[i]];
        var b = verts[indices[i+1]];
        var c = verts[indices[i+2]];
        area += Mathf.Abs((b.x - a.x) * (c.z - a.z) - (c.x - a.x) * (b.z - a.z)) * 0.5f;
      }
      return area;
    }

    static void AnalyzeSpawns(List<Transform> spawns, ref Result result){
      result.numSpawns = spawns.Count;
      int red=0, blue=0, green=0, neutral=0;
      foreach(var t in spawns){
        var lower = t.name.ToLowerInvariant();
        if (lower.Contains("red")) red++; else if (lower.Contains("blue")) blue++; else if (lower.Contains("green")) green++; else neutral++;
      }
      result.redSpawns = red;
      result.blueSpawns = blue;
      result.greenSpawns = green;
      float total = Mathf.Max(1, red + blue + green + neutral);
      float maxTeam = Mathf.Max(red, Mathf.Max(blue, green));
      float minTeam = Mathf.Min(red, Mathf.Min(blue, green));
      result.spawnBalanceScore = 1f - ((maxTeam - minTeam) / total);
    }

    static (float avg, float max, int lanes) SampleSightLines(Bounds bounds, int samples){
      if (samples <= 0) return (0f,0f,0);
      UnityEngine.Random.InitState(42);
      float totalLong = 0f;
      float longest = 0f;
      int lanes = 0;
      for(int i=0;i<samples;i++){
        var start = new Vector3(
          UnityEngine.Random.Range(bounds.min.x, bounds.max.x),
          bounds.center.y + 1.5f,
          UnityEngine.Random.Range(bounds.min.z, bounds.max.z));
        var dir = Quaternion.Euler(0f, UnityEngine.Random.Range(0f,360f), 0f) * Vector3.forward;
        if(Physics.Raycast(start, dir, out var hit, 200f)){
          longest = Mathf.Max(longest, hit.distance);
          if(hit.distance > 20f){ lanes++; totalLong += hit.distance; }
        }
      }
      float avgLong = lanes > 0 ? totalLong / lanes : 0f;
      return (avgLong, longest, lanes);
    }

    static int EstimateChokepoints(Bounds bounds, int samples){
      int chokepoints = 0;
      var path = new NavMeshPath();
      UnityEngine.Random.InitState(84);
      for(int i=0;i<samples;i++){
        var start = RandomPoint(bounds);
        var end = RandomPoint(bounds);
        if (NavMesh.CalculatePath(start, end, NavMesh.AllAreas, path) && path.corners.Length > 2){
          for(int j=1;j<path.corners.Length-1;j++){
            var prev = path.corners[j-1];
            var curr = path.corners[j];
            var next = path.corners[j+1];
            float angle = Vector3.Angle((curr - prev).normalized, (next - curr).normalized);
            if(angle > 60f) chokepoints++;
          }
        }
      }
      return chokepoints;
    }

    static Vector3 RandomPoint(Bounds bounds){
      return new Vector3(
        UnityEngine.Random.Range(bounds.min.x, bounds.max.x),
        bounds.center.y + 0.5f,
        UnityEngine.Random.Range(bounds.min.z, bounds.max.z));
    }
  }
}
#endif
