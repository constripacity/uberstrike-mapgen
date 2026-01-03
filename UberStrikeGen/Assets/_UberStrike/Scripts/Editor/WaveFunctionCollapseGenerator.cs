#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor-side Wave Function Collapse generator to produce architecturally
/// valid blueprint textures. Includes a reusable WFCCore solver and an
/// EditorWindow for ad-hoc layout generation.
/// </summary>
public class WaveFunctionCollapseGenerator : EditorWindow
{
    private int _width = 64;
    private int _height = 64;
    private int _spawnCount = 2;
    private bool _ensureConnected = true;
    private int _seed = -1;
    private Texture2D _preview;
    private WFCCore _solver;

    [MenuItem("Tools/UberStrike/MapGen/Wave Function Collapse Generator")]
    public static void ShowWindow() => GetWindow<WaveFunctionCollapseGenerator>("WFC Generator");

    private void OnGUI()
    {
        GUILayout.Label("Wave Function Collapse", EditorStyles.boldLabel);

        _width = EditorGUILayout.IntSlider("Width", _width, 32, 256);
        _height = EditorGUILayout.IntSlider("Height", _height, 32, 256);
        _spawnCount = EditorGUILayout.IntSlider("Spawn Points", _spawnCount, 0, 8);
        _ensureConnected = EditorGUILayout.Toggle("Ensure Connected", _ensureConnected);
        _seed = EditorGUILayout.IntField("Seed (-1 random)", _seed);

        if (GUILayout.Button("Generate"))
        {
            Generate();
        }

        if (_preview)
        {
            GUILayout.Label("Preview", EditorStyles.boldLabel);
            Rect r = GUILayoutUtility.GetRect(256, 256, GUILayout.ExpandWidth(true));
            GUI.DrawTexture(r, _preview, ScaleMode.ScaleToFit);
        }

        if (_preview && GUILayout.Button("Save Blueprint PNG"))
        {
            SavePreview();
        }
    }

    private void Generate()
    {
        int seed = _seed >= 0 ? _seed : UnityEngine.Random.Range(0, int.MaxValue);
        _solver = new WFCCore(_width, _height, seed);

        var constraints = new Dictionary<Vector2Int, WFCTileType>();
        // Border walls to frame the layout
        for (int x = 0; x < _width; x++)
        {
            constraints[new Vector2Int(x, 0)] = WFCTileType.Wall;
            constraints[new Vector2Int(x, _height - 1)] = WFCTileType.Wall;
        }
        for (int y = 0; y < _height; y++)
        {
            constraints[new Vector2Int(0, y)] = WFCTileType.Wall;
            constraints[new Vector2Int(_width - 1, y)] = WFCTileType.Wall;
        }

        // Simple spawn hints on opposite quadrants
        if (_spawnCount > 0)
        {
            var spawnHints = new List<Vector2Int>
            {
                new Vector2Int(_width / 4, _height / 4),
                new Vector2Int(3 * _width / 4, 3 * _height / 4),
                new Vector2Int(_width / 4, 3 * _height / 4),
                new Vector2Int(3 * _width / 4, _height / 4)
            };
            for (int i = 0; i < Mathf.Min(_spawnCount, spawnHints.Count); i++)
            {
                constraints[spawnHints[i]] = WFCTileType.Spawn;
            }
        }

        _solver.ApplyConstraints(constraints);
        bool success = _solver.Collapse();
        if (success && (!_ensureConnected || _solver.EnsureConnectivity()))
        {
            var colors = _solver.ToBlueprintColors();
            _preview = new Texture2D(_width, _height, TextureFormat.RGB24, false);
            _preview.SetPixels(colors);
            _preview.filterMode = FilterMode.Point;
            _preview.Apply();
            Debug.Log("[WFC] Map generated successfully.");
        }
        else
        {
            Debug.LogError("[WFC] Failed to generate a valid map.");
            _preview = null;
        }
    }

    private void SavePreview()
    {
        string path = EditorUtility.SaveFilePanel("Save WFC Blueprint", "Assets/_UberStrike/Blueprints/MapLayouts", "wfc_map.png", "png");
        if (string.IsNullOrEmpty(path) || !_preview)
            return;

        byte[] bytes = _preview.EncodeToPNG();
        System.IO.File.WriteAllBytes(path, bytes);
        AssetDatabase.Refresh();
        Debug.Log($"[WFC] Saved blueprint to {path}");
    }
}

public enum WFCTileType
{
    Void,
    Floor,
    Wall,
    WallCorner,
    WallT,
    WallEnd,
    Door,
    Water,
    Bridge,
    Spawn
}

public class WFCTile
{
    public WFCTileType Type;
    public string Id;
    public string[] Sockets; // N,E,S,W
    public float Weight;
    public int Rotation;

    public WFCTile(WFCTileType type, string id, string[] sockets, float weight = 1f, int rotation = 0)
    {
        Type = type;
        Id = id;
        Sockets = sockets;
        Weight = weight;
        Rotation = rotation;
    }

    public WFCTile Rotate(int times)
    {
        times = times % 4;
        if (times == 0)
            return this;
        string[] sockets = Sockets.ToArray();
        for (int i = 0; i < times; i++)
        {
            sockets = new[] { sockets[3], sockets[0], sockets[1], sockets[2] };
        }
        return new WFCTile(Type, $"{Id}_r{(Rotation + times * 90) % 360}", sockets, Weight, (Rotation + times * 90) % 360);
    }
}

public class WFCCore
{
    private readonly int _width;
    private readonly int _height;
    private readonly System.Random _rng;
    private readonly List<WFCTile> _tiles;
    private readonly Dictionary<int, Dictionary<char, HashSet<int>>> _adjacency;
    private readonly List<List<HashSet<int>>> _wave;
    private readonly int[,] _grid; // -1 uncollapsed

    private static readonly WFCTile[] BaseTiles =
    {
        new WFCTile(WFCTileType.Void, "void", new[] {"void", "void", "void", "void"}, 0.05f),
        new WFCTile(WFCTileType.Floor, "floor", new[] {"floor", "floor", "floor", "floor"}, 5f),
        new WFCTile(WFCTileType.Wall, "wall", new[] {"wall", "void", "wall", "void"}, 3f),
        new WFCTile(WFCTileType.WallCorner, "wall_corner", new[] {"void", "void", "wall", "wall"}, 2f),
        new WFCTile(WFCTileType.WallT, "wall_t", new[] {"void", "wall", "wall", "wall"}, 1.5f),
        new WFCTile(WFCTileType.WallEnd, "wall_end", new[] {"void", "void", "wall", "void"}, 1f),
        new WFCTile(WFCTileType.Door, "door", new[] {"floor", "wall", "floor", "wall"}, 0.8f),
        new WFCTile(WFCTileType.Water, "water", new[] {"water", "water", "water", "water"}, 0.4f),
        new WFCTile(WFCTileType.Bridge, "bridge", new[] {"floor", "water", "floor", "water"}, 0.35f),
        new WFCTile(WFCTileType.Spawn, "spawn", new[] {"floor", "floor", "floor", "floor"}, 0.15f),
    };

    public WFCCore(int width, int height, int seed)
    {
        _width = width;
        _height = height;
        _rng = new System.Random(seed);
        _tiles = BuildTileset();
        _adjacency = BuildAdjacency();
        _wave = new List<List<HashSet<int>>>(_height);
        for (int y = 0; y < _height; y++)
        {
            var row = new List<HashSet<int>>(_width);
            for (int x = 0; x < _width; x++)
                row.Add(new HashSet<int>(Enumerable.Range(0, _tiles.Count)));
            _wave.Add(row);
        }
        _grid = new int[_height, _width];
        for (int y = 0; y < _height; y++)
            for (int x = 0; x < _width; x++)
                _grid[y, x] = -1;
    }

    private List<WFCTile> BuildTileset()
    {
        var list = new List<WFCTile>();
        foreach (var baseTile in BaseTiles)
        {
            list.Add(baseTile);
            if (baseTile.Type == WFCTileType.Wall || baseTile.Type == WFCTileType.WallCorner ||
                baseTile.Type == WFCTileType.WallT || baseTile.Type == WFCTileType.WallEnd ||
                baseTile.Type == WFCTileType.Door)
            {
                list.Add(baseTile.Rotate(1));
                list.Add(baseTile.Rotate(2));
                list.Add(baseTile.Rotate(3));
            }
        }
        return list;
    }

    private Dictionary<int, Dictionary<char, HashSet<int>>> BuildAdjacency()
    {
        var adj = new Dictionary<int, Dictionary<char, HashSet<int>>>();
        for (int i = 0; i < _tiles.Count; i++)
        {
            adj[i] = new Dictionary<char, HashSet<int>>
            {
                ['N'] = new HashSet<int>(),
                ['E'] = new HashSet<int>(),
                ['S'] = new HashSet<int>(),
                ['W'] = new HashSet<int>()
            };

            for (int j = 0; j < _tiles.Count; j++)
            {
                if (SocketsMatch(_tiles[i].Sockets[0], _tiles[j].Sockets[2])) adj[i]['N'].Add(j);
                if (SocketsMatch(_tiles[i].Sockets[1], _tiles[j].Sockets[3])) adj[i]['E'].Add(j);
                if (SocketsMatch(_tiles[i].Sockets[2], _tiles[j].Sockets[0])) adj[i]['S'].Add(j);
                if (SocketsMatch(_tiles[i].Sockets[3], _tiles[j].Sockets[1])) adj[i]['W'].Add(j);
            }
        }
        return adj;
    }

    private static bool SocketsMatch(string a, string b)
    {
        if (a == b) return true;
        return (a, b) switch
        {
            ("floor", "door") or ("door", "floor") => true,
            ("wall", "door") or ("door", "wall") => true,
            ("water", "bridge") or ("bridge", "water") => true,
            ("floor", "bridge") or ("bridge", "floor") => true,
            _ => false
        };
    }

    public void ApplyConstraints(Dictionary<Vector2Int, WFCTileType> constraints)
    {
        foreach (var kvp in constraints)
        {
            var allowed = Enumerable.Range(0, _tiles.Count).Where(i => _tiles[i].Type == kvp.Value).ToHashSet();
            if (allowed.Count == 0) continue;
            _wave[kvp.Key.y][kvp.Key.x] = allowed;
            _grid[kvp.Key.y, kvp.Key.x] = -1; // force recalc
        }
    }

    private float Entropy(int x, int y)
    {
        var options = _wave[y][x];
        if (options.Count <= 1) return float.PositiveInfinity;
        float total = options.Sum(i => _tiles[i].Weight);
        if (total <= 0f) return float.PositiveInfinity;
        float entropy = 0f;
        foreach (int i in options)
        {
            float p = _tiles[i].Weight / total;
            entropy -= p * Mathf.Log(p);
        }
        return entropy + (float)_rng.NextDouble() * 0.001f;
    }

    private bool Observe()
    {
        float min = float.PositiveInfinity;
        var cells = new List<Vector2Int>();
        for (int y = 0; y < _height; y++)
        {
            for (int x = 0; x < _width; x++)
            {
                if (_grid[y, x] != -1) continue;
                float e = Entropy(x, y);
                if (e < min)
                {
                    min = e;
                    cells.Clear();
                    cells.Add(new Vector2Int(x, y));
                }
                else if (Math.Abs(e - min) < 0.0001f)
                {
                    cells.Add(new Vector2Int(x, y));
                }
            }
        }

        if (cells.Count == 0)
            return true;

        var chosen = cells[_rng.Next(cells.Count)];
        var options = _wave[chosen.y][chosen.x].ToList();
        var weights = options.Select(i => _tiles[i].Weight).ToArray();
        float total = weights.Sum();
        if (total <= 0f) return false;
        float r = (float)_rng.NextDouble() * total;
        int idx = 0;
        for (int i = 0; i < weights.Length; i++)
        {
            r -= weights[i];
            if (r <= 0f)
            {
                idx = i; break;
            }
        }
        int choice = options[idx];
        _wave[chosen.y][chosen.x] = new HashSet<int> { choice };
        _grid[chosen.y, chosen.x] = choice;
        return true;
    }

    private bool Propagate()
    {
        var stack = new Stack<Vector2Int>();
        for (int y = 0; y < _height; y++)
            for (int x = 0; x < _width; x++)
                stack.Push(new Vector2Int(x, y));

        while (stack.Count > 0)
        {
            var cell = stack.Pop();
            var current = _wave[cell.y][cell.x];
            if (current.Count == 0) return false;

            foreach (var (dx, dy, dir) in new[] { (0, -1, 'N'), (1, 0, 'E'), (0, 1, 'S'), (-1, 0, 'W') })
            {
                int nx = cell.x + dx, ny = cell.y + dy;
                if (nx < 0 || ny < 0 || nx >= _width || ny >= _height) continue;

                var neighbor = _wave[ny][nx];
                var allowed = new HashSet<int>();
                foreach (int tile in current)
                    allowed.UnionWith(_adjacency[tile][dir]);

                var intersection = neighbor.Intersect(allowed).ToHashSet();
                if (intersection.Count == 0) return false;
                if (!neighbor.SetEquals(intersection))
                {
                    _wave[ny][nx] = intersection;
                    stack.Push(new Vector2Int(nx, ny));
                }
            }
        }
        return true;
    }

    public bool Collapse(int maxSteps = 10000)
    {
        int steps = 0;
        while (steps < maxSteps && HasUncollapsed())
        {
            steps++;
            if (!Observe()) return false;
            if (!Propagate()) return false;
        }
        return !HasUncollapsed();
    }

    private bool HasUncollapsed()
    {
        for (int y = 0; y < _height; y++)
            for (int x = 0; x < _width; x++)
                if (_grid[y, x] == -1) return true;
        return false;
    }

    public bool EnsureConnectivity()
    {
        var walkable = new HashSet<Vector2Int>();
        for (int y = 0; y < _height; y++)
        {
            for (int x = 0; x < _width; x++)
            {
                int idx = _grid[y, x];
                if (idx < 0) continue;
                var t = _tiles[idx].Type;
                if (t == WFCTileType.Floor || t == WFCTileType.Door || t == WFCTileType.Spawn || t == WFCTileType.Bridge)
                    walkable.Add(new Vector2Int(x, y));
            }
        }

        if (walkable.Count == 0) return false;

        var start = walkable.First();
        var stack = new Stack<Vector2Int>();
        var seen = new HashSet<Vector2Int>();
        stack.Push(start);
        seen.Add(start);
        while (stack.Count > 0)
        {
            var c = stack.Pop();
            foreach (var dir in new[] { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left })
            {
                var n = c + dir;
                if (walkable.Contains(n) && seen.Add(n))
                    stack.Push(n);
            }
        }
        return seen.Count == walkable.Count;
    }

    public Color[] ToBlueprintColors()
    {
        var colors = new Color[_width * _height];
        for (int y = 0; y < _height; y++)
        {
            for (int x = 0; x < _width; x++)
            {
                int idx = _grid[y, x];
                if (idx < 0)
                {
                    colors[y * _width + x] = Color.black;
                    continue;
                }
                colors[y * _width + x] = _tiles[idx].Type switch
                {
                    WFCTileType.Wall or WFCTileType.WallCorner or WFCTileType.WallEnd or WFCTileType.WallT => new Color32(0, 0, 0, 255),
                    WFCTileType.Water => new Color32(0, 0, 255, 255),
                    WFCTileType.Spawn => new Color32(255, 255, 0, 255),
                    _ => new Color32(128, 128, 128, 255)
                };
            }
        }
        return colors;
    }
}
#endif
