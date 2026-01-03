#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace MapGen {
  [Serializable]
  public class FlowMetrics {
    public List<Vector3> chokepoints = new();
    public List<Vector3> deadZones = new();
    public float[,] heatMap = new float[1,1];
    public float[,] sightlineMap = new float[1,1];
    public float spawnBalance;
    public List<List<Vector3>> circulationLoops = new();
    public float averageEngagementDistance;
    public float mapOpenness;
    public List<Vector3> strategicPositions = new();
    public List<Vector3> campingSpots = new();

    public string Summary() =>
      $"Spawn Balance {spawnBalance:F3} | Openness {mapOpenness:P0} | Engagement {averageEngagementDistance:F1}m | Chokepoints {chokepoints.Count}";
  }

  public static class FlowAnalysisCore {
    const int MaxWalkSamples = 800;
    const int WalkLength = 40;

    public static FlowMetrics Analyze(GameObject root){
      var metrics = new FlowMetrics();
      if(!root) return metrics;
      var bounds = CalcBounds(root.GetComponentsInChildren<Renderer>(true));
      var nav = NavMesh.CalculateTriangulation();
      var graph = BuildGraph(nav);
      if (graph.nodes.Count==0) return metrics;

      var spawns = root.GetComponentsInChildren<Transform>(true)
        .Where(t=>t.name.IndexOf("spawn", StringComparison.OrdinalIgnoreCase)>=0)
        .Select(t=>t.position).ToList();
      var items = CollectItems(root);

      metrics.chokepoints = FindChokepoints(graph);
      metrics.deadZones = FindDeadZones(graph);
      metrics.heatMap = SimulateHeat(graph, spawns, items);
      metrics.spawnBalance = SpawnBalance(graph, spawns, items);
      metrics.circulationLoops = FindLoops(graph);
      metrics.sightlineMap = SampleSightlines(bounds, 32);
      metrics.campingSpots = FindCampingSpots(metrics.sightlineMap, bounds, graph);
      metrics.averageEngagementDistance = EstimateEngagement(metrics.sightlineMap);
      metrics.mapOpenness = CalcOpenness(root, bounds);
      metrics.strategicPositions = FindStrategic(graph, items);
      return metrics;
    }

    class Graph {
      public List<Vector3> nodes = new();
      public Dictionary<int, List<int>> edges = new();
    }

    static Graph BuildGraph(NavMeshTriangulation nav){
      var g=new Graph();
      if(nav.vertices==null||nav.vertices.Length==0||nav.indices==null||nav.indices.Length==0) return g;
      g.nodes.AddRange(nav.vertices);
      for(int i=0;i<nav.indices.Length;i+=3){
        int a=nav.indices[i], b=nav.indices[i+1], c=nav.indices[i+2];
        AddEdge(g,a,b); AddEdge(g,b,c); AddEdge(g,c,a);
      }
      return g;
    }

    static void AddEdge(Graph g, int a, int b){
      if(!g.edges.TryGetValue(a, out var listA)){ listA=new List<int>(); g.edges[a]=listA; }
      if(!g.edges.TryGetValue(b, out var listB)){ listB=new List<int>(); g.edges[b]=listB; }
      if(!listA.Contains(b)) listA.Add(b);
      if(!listB.Contains(a)) listB.Add(a);
    }

    static List<Vector3> FindChokepoints(Graph g){
      var centrality=new float[g.nodes.Count];
      int sample=Mathf.Min(80,g.nodes.Count);
      var rnd=new System.Random(123);
      var ids=g.nodes.Select((v,i)=>i).OrderBy(_=>rnd.Next()).Take(sample).ToArray();
      var temp=new List<int>();
      foreach(var s in ids){
        var dist=Dijkstra(g,s,out var prev);
        foreach(var t in ids){ if(t==s||dist[t]==float.PositiveInfinity) continue;
          temp.Clear(); int cur=t; while(cur!=s && cur!=-1){ temp.Add(cur); cur=prev[cur]; }
          foreach(var v in temp) centrality[v]+=1f;
        }
      }
      float thresh = Percentile(centrality,0.9f);
      var result=new List<Vector3>();
      for(int i=0;i<centrality.Length;i++) if(centrality[i]>thresh) result.Add(g.nodes[i]);
      return result;
    }

    static List<Vector3> FindDeadZones(Graph g){
      var list=new List<Vector3>();
      foreach(var kv in g.edges) if(kv.Value.Count<=1) list.Add(g.nodes[kv.Key]);
      return list;
    }

    static float[,] SimulateHeat(Graph g, List<Vector3> spawns, Dictionary<string,List<Vector3>> items){
      int count=g.nodes.Count; if(count==0) return new float[1,1];
      var heat=new float[count];
      var itemNodes = items?.SelectMany(kv=>kv.Value.Select(p=>NearestNode(g,p))).Where(i=>i>=0).ToArray();
      var spawnNodes = spawns.Select(p=>NearestNode(g,p)).Where(i=>i>=0).ToArray();
      var rnd=new System.Random(321);
      for(int sim=0; sim<MaxWalkSamples; sim++){
        int cur = spawnNodes.Length>0? spawnNodes[rnd.Next(spawnNodes.Length)] : rnd.Next(count);
        for(int step=0; step<WalkLength; step++){
          heat[cur]+=1f;
          if(!g.edges.TryGetValue(cur,out var neigh)||neigh.Count==0) break;
          if(itemNodes!=null && itemNodes.Length>0 && rnd.NextDouble()<0.7){
            int target=itemNodes[rnd.Next(itemNodes.Length)];
            var path = ShortestPath(g, cur, target);
            cur = path.Count>1? path[1]: neigh[rnd.Next(neigh.Count)];
          } else cur = neigh[rnd.Next(neigh.Count)];
        }
      }
      float max=heat.Max()+0.001f;
      var map=new float[count];
      for(int i=0;i<count;i++) map[i]=heat[i]/max;
      return ProjectToGrid(g, map);
    }

    static float SpawnBalance(Graph g, List<Vector3> spawns, Dictionary<string,List<Vector3>> items){
      if(spawns.Count<2||g.nodes.Count==0) return 0f;
      var scores=new List<float>();
      foreach(var s in spawns){
        int n=NearestNode(g,s); if(n<0) continue;
        var dists=new List<float>();
        if(items!=null){
          foreach(var kv in items){ float w=ItemWeight(kv.Key);
            foreach(var p in kv.Value){ int t=NearestNode(g,p); if(t<0) continue; var dist=Dijkstra(g,n,t); if(float.IsInfinity(dist)) continue; dists.Add(dist/(w+1f)); }
          }
        }
        if(dists.Count>0) scores.Add(dists.Average());
      }
      return (scores.Count>1 && scores.Average()!=0f)? (float)(Std(scores)/scores.Average()) : 0f;
    }

    static List<List<Vector3>> FindLoops(Graph g){
      var loops=new List<List<Vector3>>();
      var visited=new HashSet<int>();
      foreach(var start in g.edges.Keys){ if(loops.Count>=10) break; visited.Clear();
        DFS(g,start,start,new List<int>(),visited,loops);
      }
      return loops;
    }

    static void DFS(Graph g,int current,int target,List<int> path,HashSet<int> visited,List<List<Vector3>> loops){
      visited.Add(current); path.Add(current);
      if(path.Count>2 && g.edges.TryGetValue(current,out var neigh)){
        foreach(var n in neigh){
          if(n==target && path.Count>=3 && path.Count<=20){ loops.Add(path.Select(i=>g.nodes[i]).ToList()); return; }
          if(!visited.Contains(n) && path.Count<20) DFS(g,n,target,new List<int>(path),new HashSet<int>(visited),loops);
          if(loops.Count>=10) return;
        }
      }
    }

    static float[,] SampleSightlines(Bounds bounds,int samples){
      int size=32; var map=new float[size,size];
      for(int i=0;i<samples;i++){
        var origin=new Vector3(UnityEngine.Random.Range(bounds.min.x,bounds.max.x), bounds.center.y+1.5f, UnityEngine.Random.Range(bounds.min.z,bounds.max.z));
        for(int r=0;r<16;r++){
          var dir=Quaternion.Euler(0,UnityEngine.Random.Range(0f,360f),0)*Vector3.forward;
          if(Physics.Raycast(origin,dir,out var hit,80f)){
            var p=Project(bounds,hit.point,size); map[p.y,p.x]+=1f;
          }
        }
      }
      return map;
    }

    static List<Vector3> FindCampingSpots(float[,] exposure, Bounds bounds, Graph g){
      var spots=new List<Vector3>();
      int w=exposure.GetLength(1), h=exposure.GetLength(0);
      for(int y=1;y<h-1;y++) for(int x=1;x<w-1;x++){
        float cover=AdjacentWalls(g, bounds, x,y,w,h);
        if(cover>=2 && cover<=4 && exposure[y,x]>0.3f) spots.Add(Unproject(bounds,x,y,w,h));
      }
      spots = spots.OrderByDescending(p=>exposure[Project(bounds,p,w).y, Project(bounds,p,w).x]).Take(20).ToList();
      return spots;
    }

    static float EstimateEngagement(float[,] exposure){
      var vals=new List<float>();
      for(int y=0;y<exposure.GetLength(0);y++) for(int x=0;x<exposure.GetLength(1);x++) if(exposure[y,x]>0) vals.Add(exposure[y,x]);
      return vals.Count>0? vals.Average()*50f : 20f;
    }

    static float CalcOpenness(GameObject root, Bounds b){
      var meshes=root.GetComponentsInChildren<MeshFilter>(true);
      float floor=0, wall=0;
      foreach(var mf in meshes){
        var n=mf.name.ToLowerInvariant();
        if(n.Contains("wall")) wall++;
        else floor++;
      }
      return wall==0?1f: Mathf.Min(1f, (floor/(wall+1f))/5f);
    }

    static List<Vector3> FindStrategic(Graph g, Dictionary<string,List<Vector3>> items){
      var list=new List<Vector3>();
      if(items!=null){
        var all = items.Values.SelectMany(v=>v).ToList();
        if(all.Count>0){
          var center = all.Aggregate(Vector3.zero,(a,b)=>a+b)/all.Count;
          foreach(var kv in g.edges) if(kv.Value.Count>=4 && Vector3.Distance(g.nodes[kv.Key],center)<30f) list.Add(g.nodes[kv.Key]);
        }
      }
      var degrees=g.edges.ToDictionary(kv=>kv.Key, kv=>kv.Value.Count);
      float thresh = Percentile(degrees.Values.Select(v=>(float)v).ToArray(),0.8f);
      foreach(var kv in degrees) if(kv.Value>=thresh && !list.Contains(g.nodes[kv.Key])) list.Add(g.nodes[kv.Key]);
      return list.Take(15).ToList();
    }

    // helpers
    static Bounds CalcBounds(IReadOnlyList<Renderer> rends){
      if(rends==null||rends.Count==0) return new Bounds(Vector3.zero, Vector3.one*10f);
      var b=rends[0].bounds; for(int i=1;i<rends.Count;i++) b.Encapsulate(rends[i].bounds); return b;
    }

    static Dictionary<string,List<Vector3>> CollectItems(GameObject root){
      var dict=new Dictionary<string,List<Vector3>>();
      foreach(var t in root.GetComponentsInChildren<Transform>(true)){
        var lower=t.name.ToLowerInvariant();
        string key=null;
        if(lower.Contains("sniper")) key="weapon_sniper";
        else if(lower.Contains("rocket")) key="weapon_rocket";
        else if(lower.Contains("shotgun")) key="weapon_shotgun";
        else if(lower.Contains("armor")) key="armor_light";
        else if(lower.Contains("health")) key="health_small";
        if(key!=null){ if(!dict.ContainsKey(key)) dict[key]=new List<Vector3>(); dict[key].Add(t.position);}        
      }
      return dict;
    }

    static int NearestNode(Graph g, Vector3 pos){
      int best=-1; float bestD=float.PositiveInfinity;
      for(int i=0;i<g.nodes.Count;i++){
        float d=(g.nodes[i]-pos).sqrMagnitude;
        if(d<bestD){ bestD=d; best=i; }
      }
      return best;
    }

    static List<int> ShortestPath(Graph g,int start,int target){
      var path=new List<int>();
      var dist=Dijkstra(g,start,out var prev);
      if(float.IsInfinity(dist[target])) return path;
      int cur=target; while(cur!=-1){ path.Insert(0,cur); cur=prev[cur]; }
      return path;
    }

    static float[] Dijkstra(Graph g,int source,out int[] prev){
      int n=g.nodes.Count; var dist=Enumerable.Repeat(float.PositiveInfinity,n).ToArray(); prev=Enumerable.Repeat(-1,n).ToArray();
      var visited=new bool[n]; dist[source]=0f;
      var pq=new SortedSet<(float d,int i)>(Comparer<(float,int)>.Create((a,b)=> a.d!=b.d? a.d.CompareTo(b.d) : a.i.CompareTo(b.i)));
      pq.Add((0f,source));
      while(pq.Count>0){
        var (d,i)=pq.Min; pq.Remove(pq.Min); if(visited[i]) continue; visited[i]=true;
        if(!g.edges.TryGetValue(i,out var neigh)) continue;
        foreach(var v in neigh){ float nd=d+Vector3.Distance(g.nodes[i],g.nodes[v]); if(nd<dist[v]){ dist[v]=nd; prev[v]=i; pq.Add((nd,v)); } }
      }
      return dist;
    }

    static float Percentile(IReadOnlyList<float> values,float p){
      if(values==null||values.Count==0) return 0f;
      var sorted=values.OrderBy(v=>v).ToArray();
      int idx=Mathf.Clamp(Mathf.RoundToInt((sorted.Length-1)*p),0,sorted.Length-1); return sorted[idx];
    }

    static float Std(List<float> values){
      if(values.Count==0) return 0f; float avg=values.Average(); float sum=values.Sum(v=> (v-avg)*(v-avg)); return Mathf.Sqrt(sum/values.Count);
    }

    static float[,] ProjectToGrid(Graph g, float[] values){
      var min=new Vector2(float.MaxValue,float.MaxValue); var max=new Vector2(float.MinValue,float.MinValue);
      foreach(var v in g.nodes){ min=Vector2.Min(min,new Vector2(v.x,v.z)); max=Vector2.Max(max,new Vector2(v.x,v.z)); }
      int w=Mathf.Max(1,Mathf.CeilToInt(max.x-min.x)); int h=Mathf.Max(1,Mathf.CeilToInt(max.y-min.y));
      var grid=new float[h,w];
      for(int i=0;i<g.nodes.Count;i++){
        int x=Mathf.Clamp(Mathf.RoundToInt(g.nodes[i].x-min.x),0,w-1);
        int y=Mathf.Clamp(Mathf.RoundToInt(g.nodes[i].z-min.y),0,h-1);
        grid[y,x]=Mathf.Max(grid[y,x], values[i]);
      }
      return grid;
    }

    static Vector2Int Project(Bounds b, Vector3 pos, int size){
      float nx=Mathf.InverseLerp(b.min.x,b.max.x,pos.x); float nz=Mathf.InverseLerp(b.min.z,b.max.z,pos.z);
      return new Vector2Int(Mathf.Clamp(Mathf.RoundToInt(nx*(size-1)),0,size-1), Mathf.Clamp(Mathf.RoundToInt(nz*(size-1)),0,size-1));
    }

    static Vector3 Unproject(Bounds b,int x,int y,int w,int h){
      float px=Mathf.Lerp(b.min.x,b.max.x,x/(float)(w-1));
      float pz=Mathf.Lerp(b.min.z,b.max.z,y/(float)(h-1));
      return new Vector3(px,b.center.y,pz);
    }

    static float AdjacentWalls(Graph g, Bounds b, int x,int y,int w,int h){
      float cover=0f; var p=Unproject(b,x,y,w,h);
      foreach(var node in g.nodes){ float d=(new Vector3(node.x,b.center.y,node.z)-p).sqrMagnitude; if(d<4f) cover++; }
      return cover;
    }
  }

  public class GraphFlowAnalyzerWindow : EditorWindow {
    GameObject mapRoot;
    FlowMetrics last;
    Texture2D heatPreview;
    bool autoRegenerate;

    [MenuItem("Tools/UberStrike/MapGen/Graph Flow Analyzer")]
    static void Open() => GetWindow<GraphFlowAnalyzerWindow>("Graph Flow Analyzer");

    void OnGUI(){
      mapRoot = (GameObject)EditorGUILayout.ObjectField("Map Root", mapRoot, typeof(GameObject), true);
      autoRegenerate = EditorGUILayout.Toggle("Auto-regenerate if poor", autoRegenerate);
      if(GUILayout.Button("Analyze")) Analyze();
      if(last!=null){
        EditorGUILayout.HelpBox(last.Summary(), MessageType.Info);
        if(heatPreview!=null){
          var r=GUILayoutUtility.GetRect(256,256); GUI.DrawTexture(r, heatPreview, ScaleMode.ScaleToFit);
        }
        if(GUILayout.Button("Visualize in Scene")) Visualize();
      }
    }

    void Analyze(){
      if(!mapRoot){ Debug.LogWarning("Select a map root"); return; }
      last = FlowAnalysisCore.Analyze(mapRoot);
      heatPreview = CreateHeatTexture(last.heatMap);
      Debug.Log("Flow analysis complete: "+last.Summary());
      if(autoRegenerate && last.spawnBalance>0.3f){
        Debug.LogWarning("Spawn balance poor; consider regenerating layout/theme or re-running placement.");
      }
    }

    void Visualize(){
      if(last==null) return;
      var old=GameObject.Find("FlowVisualization"); if(old) DestroyImmediate(old);
      var root=new GameObject("FlowVisualization");
      foreach(var c in last.chokepoints){ var s=GameObject.CreatePrimitive(PrimitiveType.Sphere); s.transform.position=c; s.transform.localScale=Vector3.one*2f; s.transform.SetParent(root.transform); s.GetComponent<Renderer>().sharedMaterial=new Material(Shader.Find("Standard")){color=Color.red}; }
      foreach(var s in last.strategicPositions){ var c=GameObject.CreatePrimitive(PrimitiveType.Cube); c.transform.position=s; c.transform.localScale=Vector3.one*1.5f; c.transform.SetParent(root.transform); c.GetComponent<Renderer>().sharedMaterial=new Material(Shader.Find("Standard")){color=Color.yellow}; }
      Debug.Log("Scene visualization created under FlowVisualization");
    }

    Texture2D CreateHeatTexture(float[,] map){
      int h=map.GetLength(0), w=map.GetLength(1); var tex=new Texture2D(w,h,TextureFormat.RGB24,false);
      float max=0f; foreach(var v in map) max=Mathf.Max(max,v);
      for(int y=0;y<h;y++) for(int x=0;x<w;x++){
        float v=max>0? map[y,x]/max : 0f;
        Color c = v<0.25f? Color.Lerp(Color.blue,Color.green,v*4f)
          : v<0.5f? Color.Lerp(Color.green,Color.yellow,(v-0.25f)*4f)
          : v<0.75f? Color.Lerp(Color.yellow,new Color(1f,0.5f,0f),(v-0.5f)*4f)
          : Color.Lerp(new Color(1f,0.5f,0f), Color.red, (v-0.75f)*4f);
        tex.SetPixel(x,y,c);
      }
      tex.Apply(); tex.filterMode=FilterMode.Bilinear; return tex;
    }
  }
}
#endif
