#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace MapGen {
  [Serializable] class InstanceDTO { public string prefab; public float[] pos; public float[] rot; public float[] scale; }
  [Serializable] class BoundsDTO { public float[] min; public float[] max; }
  [Serializable] class LegendHexDTO { public string Floor, Wall, Glass, Water, Spawn, JumpPad, Teleporter, Health, Armor; }
  [Serializable] class QCResultDTO {
    public bool navmesh_ok; public float navmesh_coverage_pct;
    public int num_spawns, num_jump_pads, num_teleporters, num_health, num_armor;
    public float spawn_balance; public float avg_long_los_m; public float longest_los_m;
    public int num_sniper_lanes; public int chokepoint_count;
    public float triangle_count; public int draw_calls; public float perf_cost;
    public float map_area_m2, map_height_m;
  }
  [Serializable] class MapMetaDTO {
    public string map_name, export_date;
    public int[] size_px; public float cell_size_m, height_scale_m_per_255;
    public BoundsDTO bounds; public LegendHexDTO legend_hex;
    public List<InstanceDTO> instances; public QCResultDTO qc;
  }

  public static class MapDatasetExporter {
    [MenuItem("Tools/UberStrike/MapGen/Export Dataset (Current Scene)")]
    static void ExportCurrent() {
      var scene = EditorSceneManager.GetActiveScene();
      if (!scene.IsValid()) { Debug.LogError("No active scene"); return; }
      ExportSceneToDataset(scene);
    }

    [MenuItem("Tools/UberStrike/MapGen/Export All Maps")]
    static void ExportAll() {
      var paths = AssetDatabase.FindAssets("t:Scene", new[]{ "Assets" })
        .Select(AssetDatabase.GUIDToAssetPath)
        .Where(p => p.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)).ToArray();
      foreach (var p in paths) { EditorSceneManager.OpenScene(p); ExportSceneToDataset(EditorSceneManager.GetActiveScene()); }
      Debug.Log($"Exported {paths.Length} scenes.");
    }

    static void ExportSceneToDataset(UnityEngine.SceneManagement.Scene scene) {
      var b = CalcSceneBounds();
      int W = Mathf.Clamp(Mathf.CeilToInt(b.size.x / Legend.CellSizeMeters), 64, 512);
      int H = Mathf.Clamp(Mathf.CeilToInt(b.size.z / Legend.CellSizeMeters), 64, 512);

      var legend = new Texture2D(W,H,TextureFormat.RGBA32,false,true);
      var height = new Texture2D(W,H,TextureFormat.R8,false,true);
      var px = new Color32[W*H]; var hh = new byte[W*H];

      var gos = GameObject.FindObjectsOfType<GameObject>().Where(g=>g.activeInHierarchy).ToArray();

      foreach (var go in gos) {
        if (Has(go,"floor") || Has(go,"ground") || go.layer==LayerMask.NameToLayer("Floor")) SampleMesh(go, b, px, hh, W,H, Legend.Floor);
        else if (Has(go,"wall") || go.layer==LayerMask.NameToLayer("Wall")) SampleMesh(go, b, px, hh, W,H, Legend.Wall);
        else if (Has(go,"water")) SampleMesh(go, b, px, hh, W,H, Legend.Water);
        else if (Has(go,"glass") || Has(go,"bridge")) SampleMesh(go, b, px, hh, W,H, Legend.GlassBridge);
      }

      var instances = new List<InstanceDTO>();
      foreach (var go in gos) {
        var pos = go.transform.position; var cell = WorldToLegend(pos,b,W,H);
        if (cell.x<0||cell.x>=W||cell.y<0||cell.y>=H) continue;
        int i = cell.y*W+cell.x; Color32 color = default; string prefab=null;

        if (Has(go,"spawn") || go.CompareTag("SpawnPoint")) { color=Legend.Spawn; prefab="SpawnPoint"; }
        else if (Has(go,"jump") || Has(go,"pad")) { color=Legend.JumpPad; prefab="JumpPad"; }
        else if (Has(go,"teleport")) { color=Legend.Teleporter; prefab="Teleporter"; }
        else if (Has(go,"health") && !Has(go,"armor")) { color=Legend.Health; prefab="Pickup_Health"; }
        else if (Has(go,"armor")) { color=Legend.Armor; prefab="Pickup_Armor"; }

        if (prefab!=null) {
          px[i]=color;
          instances.Add(new InstanceDTO {
            prefab=prefab,
            pos=new[]{pos.x,pos.y,pos.z},
            rot=new[]{0f, go.transform.eulerAngles.y, 0f},
            scale=new[]{1f,1f,1f}
          });
        }
      }

      legend.SetPixels32(px); legend.Apply();
      height.LoadRawTextureData(hh); height.Apply();

      var outDir = $"Assets/_Generated/Maps/{scene.name}";
      Directory.CreateDirectory(outDir);
      File.WriteAllBytes(Path.Combine(outDir,"legend.png"), legend.EncodeToPNG());
      File.WriteAllBytes(Path.Combine(outDir,"height.png"), height.EncodeToPNG());

      var qc = AnalyzeQC(scene, gos, b);

      var meta = new MapMetaDTO {
        map_name = scene.name,
        export_date = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
        size_px = new[]{W,H},
        cell_size_m = Legend.CellSizeMeters,
        height_scale_m_per_255 = 4f,
        bounds = new BoundsDTO { min = new[]{ b.min.x,b.min.y,b.min.z }, max = new[]{ b.max.x,b.max.y,b.max.z } },
        legend_hex = new LegendHexDTO {
          Floor=Hex(Legend.Floor), Wall=Hex(Legend.Wall), Glass=Hex(Legend.GlassBridge), Water=Hex(Legend.Water),
          Spawn=Hex(Legend.Spawn), JumpPad=Hex(Legend.JumpPad), Teleporter=Hex(Legend.Teleporter), Health=Hex(Legend.Health), Armor=Hex(Legend.Armor)
        },
        instances = instances,
        qc = qc
      };

      File.WriteAllText(Path.Combine(outDir,"map.json"), JsonUtility.ToJson(meta,true));

      UnityEngine.Object.DestroyImmediate(legend);
      UnityEngine.Object.DestroyImmediate(height);
      AssetDatabase.Refresh();
      Debug.Log($"Dataset exported: {outDir}");
    }

    static Bounds CalcSceneBounds(){
      var rends = GameObject.FindObjectsOfType<Renderer>().Where(r=>r.gameObject.activeInHierarchy).ToArray();
      if (rends.Length==0) return new Bounds(Vector3.zero, Vector3.one*100);
      var b = rends[0].bounds; foreach (var r in rends) b.Encapsulate(r.bounds); return b;
    }
    static void SampleMesh(GameObject go, Bounds scene, Color32[] px, byte[] hh, int W,int H, Color32 col){
      var mf=go.GetComponent<MeshFilter>(); if(!mf || !mf.sharedMesh) return;
      var m=mf.sharedMesh; foreach(var v in m.vertices){
        var wp = go.transform.TransformPoint(v); var p=WorldToLegend(wp,scene,W,H);
        if (p.x>=0&&p.x<W&&p.y>=0&&p.y<H){ int i=p.y*W+p.x; px[i]=col;
          float nh = Mathf.InverseLerp(scene.min.y, scene.max.y, wp.y); hh[i]=(byte)(nh*255); }
      }
    }
    static Vector2Int WorldToLegend(Vector3 w, Bounds b, int W,int H){
      float nx=Mathf.InverseLerp(b.min.x,b.max.x,w.x), nz=Mathf.InverseLerp(b.min.z,b.max.z,w.z);
      return new(Mathf.FloorToInt(nx*W), Mathf.FloorToInt(nz*H));
    }
    static bool Has(GameObject go, string token)=> go.name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
    static string Hex(Color32 c)=> $"#{c.r:X2}{c.g:X2}{c.b:X2}";

    static QCResultDTO AnalyzeQC(Scene scene, GameObject[] objs, Bounds b){
      int Count(Func<GameObject,bool> pred)=> objs.Count(pred);
      var qc = MapQC.Run(scene);
      return new QCResultDTO {
        navmesh_ok = qc.navmeshOk,
        navmesh_coverage_pct = qc.navmeshCoveragePercent,
        num_spawns = qc.numSpawns,
        num_jump_pads = Count(g=>Has(g,"jump")||Has(g,"pad")),
        num_teleporters = Count(g=>Has(g,"teleport")),
        num_health = Count(g=>Has(g,"health")&&!Has(g,"armor")),
        num_armor = Count(g=>Has(g,"armor")),
        spawn_balance = qc.spawnBalanceScore,
        avg_long_los_m = qc.avgLongLoS,
        longest_los_m = qc.longestLoS,
        num_sniper_lanes = qc.estimatedSniperLanes,
        chokepoint_count = qc.chokepointCount,
        triangle_count = qc.triangleCount,
        draw_calls = qc.drawCalls,
        perf_cost = qc.performanceCost,
        map_area_m2 = b.size.x * b.size.z,
        map_height_m = b.size.y
      };
    }
  }
}
#endif
