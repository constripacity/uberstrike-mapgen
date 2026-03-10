using System.Collections.Generic;
using System.IO;
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using System.Text; // NEW: Added for Encoding.UTF8

// --- BlueprintQC: Metrics Window (Existing Code) ---
public class BlueprintQC : EditorWindow
{
    // --- Legend (must match generator) ---
    private static readonly Color32 COL_EMPTY = Hex("#000000");
    private static readonly Color32 COL_FLOOR = Hex("#B8B8B8");
    private static readonly Color32 COL_WALL = Hex("#444444");
    private static readonly Color32 COL_GLASS = Hex("#00FFFF");
    private static readonly Color32 COL_WATER = Hex("#0044FF");
    private static readonly Color32 COL_JUMP = Hex("#00FF00");
    private static readonly Color32 COL_TELE = Hex("#FF00FF");
    private static readonly Color32 COL_SPAWN = Hex("#FFFF00");
    private static readonly Color32 COL_HEALTH = Hex("#FF0000");
    private static readonly Color32 COL_ARMOR = Hex("#FF7F00");
    private static readonly Color32 COL_AMMO = Hex("#00AEEF");
    private static readonly Color32 COL_RN = Hex("#9B59B6");
    private static readonly Color32 COL_RS = Hex("#8E44AD");
    private static readonly Color32 COL_RE = Hex("#3498DB");
    private static readonly Color32 COL_RW = Hex("#1ABC9C");

    // UI state
    private Texture2D blueprintTex;             // from Assets
    private string loadedPathOutsideAssets;    // if loaded from disk
    private Texture2D runtimeLoaded;            // temp Texture2D if loaded from disk
    private float metersPerPixel = 0.20f;      // default scale
    private string lastSummary = "";
    private Metrics last;

    [MenuItem("Tools/UnityAI/Blueprint QC (Metrics)")]
    public static void Open() => GetWindow<BlueprintQC>("Blueprint QC");

    private void OnGUI()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Blueprint QC", EditorStyles.boldLabel);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            blueprintTex = (Texture2D)EditorGUILayout.ObjectField(
                new GUIContent("Blueprint PNG", "Drag a PNG from Project view here."),
                blueprintTex, typeof(Texture2D), false);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Load From File…", GUILayout.Width(140)))
                {
                    var p = EditorUtility.OpenFilePanel("Select blueprint PNG", "", "png");
                    if (!string.IsNullOrEmpty(p) && File.Exists(p))
                    {
                        loadedPathOutsideAssets = p;
                        var bytes = File.ReadAllBytes(p);
                        runtimeLoaded = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
                        runtimeLoaded.filterMode = FilterMode.Point;
                        runtimeLoaded.LoadImage(bytes, markNonReadable: false);
                        blueprintTex = runtimeLoaded;
                    }
                }
                if (GUILayout.Button("Clear", GUILayout.Width(80)))
                {
                    blueprintTex = null; loadedPathOutsideAssets = null;
                    if (runtimeLoaded != null) DestroyImmediate(runtimeLoaded);
                }
                GUILayout.FlexibleSpace();
            }

            metersPerPixel = EditorGUILayout.Slider(new GUIContent("Meters/Pixel"), metersPerPixel, 0.05f, 1.0f);

            if (GUILayout.Button("Analyze", GUILayout.Height(28)))
            {
                AnalyzeCurrent();
            }
        }

        EditorGUILayout.Space(6);

        if (!string.IsNullOrEmpty(lastSummary))
        {
            EditorGUILayout.LabelField("Results", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(lastSummary, MessageType.None);
            DrawBadges(last);
        }
        else
        {
            EditorGUILayout.HelpBox("Pick a PNG (from Assets or from disk), set scale if needed, then click Analyze.", MessageType.Info);
        }
    }

    private void AnalyzeCurrent()
    {
        if (blueprintTex == null)
        {
            EditorUtility.DisplayDialog("Blueprint QC", "Please assign a PNG (Texture2D) or Load From File.", "OK");
            return;
        }

        // Ensure we can read pixels
        if (!blueprintTex.isReadable)
        {
            // Try to flip importer flags if this is an asset inside Assets/
            var path = AssetDatabase.GetAssetPath(blueprintTex);
            if (!string.IsNullOrEmpty(path))
            {
                var imp = (TextureImporter)TextureImporter.GetAtPath(path);
                if (imp != null)
                {
                    imp.textureType = TextureImporterType.Default;
                    imp.isReadable = true;
                    imp.sRGBTexture = true;
                    imp.mipmapEnabled = false;
                    imp.filterMode = FilterMode.Point;
                    imp.textureCompression = TextureImporterCompression.Uncompressed;
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                }
            }
        }

        var tex = blueprintTex;
        var w = tex.width; var h = tex.height;
        var px = tex.GetPixels32();
        if (px == null || px.Length != w * h)
        {
            EditorUtility.DisplayDialog("Blueprint QC", "Could not read PNG pixels (is it readable?).", "OK");
            return;
        }

        // Build fast color→mask lookups
        bool Is(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b;

        bool[] floor = new bool[w * h];
        bool[] walk = new bool[w * h];
        bool[] wall = new bool[w * h];
        int spawns = 0, teles = 0, jumps = 0, health = 0, armor = 0, ammo = 0;

        for (int i = 0; i < px.Length; i++)
        {
            var c = px[i];
            bool isFloor = Is(c, COL_FLOOR) || Is(c, COL_GLASS) || Is(c, COL_RN) || Is(c, COL_RS) || Is(c, COL_RE) || Is(c, COL_RW);
            floor[i] = Is(c, COL_FLOOR);
            wall[i] = Is(c, COL_WALL);
            walk[i] = isFloor;

            if (Is(c, COL_SPAWN)) spawns++;
            else if (Is(c, COL_TELE)) teles++;
            else if (Is(c, COL_JUMP)) jumps++;
            else if (Is(c, COL_HEALTH)) health++;
            else if (Is(c, COL_ARMOR)) armor++;
            else if (Is(c, COL_AMMO)) ammo++;
        }

        // Connected components over "walk"
        int comps = CountComponents(walk, w, h);

        // Loop estimate via grid graph cyclomatic number: loops ≈ E - N + C
        // N=walkable pixels, E=adjacent 4-neighbour edges between walkable, C=components
        long N = walk.LongCount(v => v);
        long E = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (!walk[i]) continue;
                if (x + 1 < w && walk[i + 1]) E++;
                if (y + 1 < h && walk[i + w]) E++;
            }
        long loops = System.Math.Max(0, E - N + comps);

        // Areas and size
        float worldW = w * metersPerPixel;
        float worldH = h * metersPerPixel;
        float areaWalk_m2 = (float)N * metersPerPixel * metersPerPixel;

        last = new Metrics
        {
            widthPx = w,
            heightPx = h,
            worldW = worldW,
            worldH = worldH,
            walkArea = areaWalk_m2,
            comps = comps,
            loops = (int)loops,
            spawns = spawns,
            teles = teles,
            jumps = jumps,
            health = health,
            armor = armor,
            ammo = ammo
        };

        lastSummary = BuildSummary(last);
        Repaint();
    }

    private static int CountComponents(bool[] walk, int w, int h)
    {
        int comps = 0;
        var visited = new bool[walk.Length];
        var q = new Queue<int>();
        int[] dx = { 1, -1, 0, 0 };
        int[] dy = { 0, 0, 1, -1 };

        for (int i = 0; i < walk.Length; i++)
        {
            if (!walk[i] || visited[i]) continue;
            comps++;
            visited[i] = true;
            q.Clear();
            q.Enqueue(i);

            while (q.Count > 0)
            {
                int v = q.Dequeue();
                int x = v % w; int y = v / w;
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + dx[k], ny = y + dy[k];
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    int ni = ny * w + nx;
                    if (walk[ni] && !visited[ni]) { visited[ni] = true; q.Enqueue(ni); }
                }
            }
        }
        return comps;
    }

    private static string BuildSummary(Metrics m)
    {
        // Heuristics/targets for "medium arena"
        bool sizeOk = m.worldW >= 180 && m.worldW <= 220 && m.worldH >= 120 && m.worldH <= 160;
        bool compsOk = m.comps == 1;
        bool loopsOk = m.loops >= 2;
        bool spawnsOk = m.spawns >= 8 && m.spawns <= 16;
        bool teleOk = (m.teles % 2) == 0;
        bool jumpsOk = m.jumps >= 4;

        string Line(string label, string val, bool ok, string extra = "")
        {
            string mark = ok ? "✔" : "✖";
            return $"{mark} {label}: {val} {extra}";
        }

        var s = "";
        s += Line("Pixels", $"{m.widthPx}×{m.heightPx}", true) + "\n";
        s += Line("World Size (m)", $"{m.worldW:F1} × {m.worldH:F1}", sizeOk, "(target ≈ 200×140)") + "\n";
        s += Line("Walkable Area (m²)", $"{m.walkArea:F0}", true) + "\n";
        s += Line("Walkable Components", $"{m.comps}", compsOk, "(must be 1)") + "\n";
        s += Line("Loop Estimate", $"{m.loops}", loopsOk, "(≥2)") + "\n";
        s += Line("Spawns", $"{m.spawns}", spawnsOk, "(8–16)") + "\n";
        s += Line("Teleporters", $"{m.teles}", teleOk, "(even count)") + "\n";
        s += Line("Jump Pads", $"{m.jumps}", jumpsOk, "(≥4)") + "\n";
        s += $"Items — Health:{m.health}  Armor:{m.armor}  Ammo:{m.ammo}";
        return s;
    }

    private void DrawBadges(Metrics m)
    {
        GUILayout.Space(6);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            DrawBadge("Size", m.worldW >= 180 && m.worldW <= 220 && m.worldH >= 120 && m.worldH <= 160);
            DrawBadge("Connectivity", m.comps == 1);
            DrawBadge("Loops ≥ 2", m.loops >= 2);
            DrawBadge("Spawns 8–16", m.spawns >= 8 && m.spawns <= 16);
            DrawBadge("Teleporters even", (m.teles % 2) == 0);
            DrawBadge("Jumps ≥ 4", m.jumps >= 4);
        }
    }

    private static void DrawBadge(string label, bool pass)
    {
        var c = GUI.color;
        GUI.color = pass ? new Color(0.6f, 0.9f, 0.6f) : new Color(0.95f, 0.6f, 0.6f);
        GUILayout.Label((pass ? "✔ " : "✖ ") + label, EditorStyles.boldLabel);
        GUI.color = c;
    }

    private static Color32 Hex(string hex)
    {
        if (hex.StartsWith("#")) hex = hex.Substring(1);
        byte r = byte.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
        byte g = byte.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
        byte b = byte.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);
        return new Color32(r, g, b, 255);
    }

    private struct Metrics
    {
        public int widthPx, heightPx;
        public float worldW, worldH, walkArea;
        public int comps, loops;
        public int spawns, teles, jumps;
        public int health, armor, ammo;
    }
}
