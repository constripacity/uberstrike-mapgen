#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Full Wave Function Collapse solver for UberStrike MapGen.
/// Ported from Python (DesktopAgent/agent/tools/wave_function_collapse.py)
/// and C# reference (UberStrikeGen WaveFunctionCollapseGenerator.cs) with
/// restart-on-contradiction backtracking and connectivity guarantee.
///
/// 10 base tile types x 4 rotations = ~30 variants.
/// Socket-based adjacency (N/E/S/W string matching).
/// Shannon entropy cell selection with weighted random collapse.
/// Constraint propagation via queue.
/// Restart-on-contradiction backtracking (configurable max restarts).
/// BFS connectivity verification (restarts if disconnected).
/// </summary>
public class WFCCore
{
    // ------------------------------------------------------------------ Tile
    private class Tile
    {
        public readonly WFCTileType Type;
        public readonly string Id;
        public readonly string[] Sockets; // N, E, S, W
        public readonly float Weight;
        public readonly int Rotation;

        public Tile(WFCTileType type, string id, string[] sockets, float weight = 1f, int rotation = 0)
        {
            Type = type;
            Id = id;
            Sockets = sockets;
            Weight = weight;
            Rotation = rotation;
        }

        public Tile Rotate(int times)
        {
            times = ((times % 4) + 4) % 4;
            if (times == 0) return this;
            string[] s = (string[])Sockets.Clone();
            for (int i = 0; i < times; i++)
                s = new[] { s[3], s[0], s[1], s[2] };
            int newRot = (Rotation + times * 90) % 360;
            return new Tile(Type, $"{Id}_r{newRot}", s, Weight, newRot);
        }
    }

    // ---------------------------------------------------------- Base tileset
    // Floor-biased weights produce open arena layouts. WallInterior bridges
    // the floor<->wall socket gap that the original 10-tile vocab couldn't
    // span (no socket bridged "floor" to "wall" without a Door, so floor
    // regions could never abut walls and the solver always contradicted).
    // Validated in Tools/MapGen/wfc_harness.py — wall_interior_tuned variant.
    private static readonly Tile[] BaseTiles =
    {
        new Tile(WFCTileType.Void,         "void",          new[] {"void",  "void",  "void",  "void"},  0.02f),
        new Tile(WFCTileType.Floor,        "floor",         new[] {"floor", "floor", "floor", "floor"}, 18.0f),
        new Tile(WFCTileType.Wall,         "wall",          new[] {"wall",  "void",  "wall",  "void"},  0.6f),
        new Tile(WFCTileType.WallCorner,   "wall_corner",   new[] {"void",  "void",  "wall",  "wall"},  0.4f),
        new Tile(WFCTileType.WallT,        "wall_t",        new[] {"void",  "wall",  "wall",  "wall"},  0.25f),
        new Tile(WFCTileType.WallEnd,      "wall_end",      new[] {"void",  "void",  "wall",  "void"},  0.2f),
        new Tile(WFCTileType.Door,         "door",          new[] {"floor", "wall",  "floor", "wall"},  0.35f),
        new Tile(WFCTileType.Water,        "water",         new[] {"water", "water", "water", "water"}, 0.1f),
        new Tile(WFCTileType.Bridge,       "bridge",        new[] {"floor", "water", "floor", "water"}, 0.1f),
        // Spawn weight is intentionally tiny: the arena generator places
        // spawns via explicit constraints, and we don't want the WFC to
        // sprinkle organic Spawn cells across the map (would flood the
        // result with 15-20+ markers and tank Spawn Balance).
        new Tile(WFCTileType.Spawn,        "spawn",         new[] {"floor", "floor", "floor", "floor"}, 0.001f),
        new Tile(WFCTileType.WallInterior, "wall_interior", new[] {"wall",  "floor", "wall",  "void"},  0.6f),
    };

    // Direction offsets: N, E, S, W  (dx, dy, direction char, opposite index for socket matching)
    private static readonly (int dx, int dy, int dir)[] Dirs =
    {
        ( 0, -1, 0), // N: my socket[0] must match neighbor socket[2]
        ( 1,  0, 1), // E: my socket[1] must match neighbor socket[3]
        ( 0,  1, 2), // S: my socket[2] must match neighbor socket[0]
        (-1,  0, 3), // W: my socket[3] must match neighbor socket[1]
    };
    private static readonly int[] OppositeSocket = { 2, 3, 0, 1 };

    // ----------------------------------------------------------- State
    private readonly int _width;
    private readonly int _height;
    private readonly int _baseSeed;
    private System.Random _rng;
    private List<Tile> _tiles;
    private HashSet<int>[][] _allowedByDir; // _allowedByDir[tileIdx * 4 + dir] = set of compatible neighbor indices
    private HashSet<int>[][] _wave; // [y][x] = set of remaining tile indices
    private int[,] _grid;          // [y,x] = collapsed tile index, -1 = uncollapsed

    // Saved constraints for re-application on restart
    private Dictionary<Vector2Int, WFCTileType> _savedConstraints;

    // ----------------------------------------------------------- Config
    /// <summary>Maximum restart attempts on contradiction or disconnected layout.</summary>
    public int MaxRestarts { get; set; } = 5;

    // Stats from last Collapse call
    public int LastRestartCount { get; private set; }
    public float LastElapsedSeconds { get; private set; }

    // ----------------------------------------------------------- Constructor

    public WFCCore(int width, int height, int seed)
    {
        _width = width;
        _height = height;
        _baseSeed = seed;
        _tiles = BuildTileset();
        BuildAdjacencyLookup();
        InitWave(seed);
    }

    // ----------------------------------------------------------- Tileset

    private static List<Tile> BuildTileset()
    {
        var list = new List<Tile>();
        foreach (var b in BaseTiles)
        {
            list.Add(b);
            // Asymmetric tiles get rotation variants
            if (b.Type == WFCTileType.Wall || b.Type == WFCTileType.WallCorner ||
                b.Type == WFCTileType.WallT || b.Type == WFCTileType.WallEnd ||
                b.Type == WFCTileType.Door || b.Type == WFCTileType.Bridge ||
                b.Type == WFCTileType.WallInterior)
            {
                list.Add(b.Rotate(1));
                list.Add(b.Rotate(2));
                list.Add(b.Rotate(3));
            }
        }
        return list;
    }

    // ----------------------------------------------------------- Adjacency

    private static bool SocketsMatch(string a, string b)
    {
        if (a == b) return true;
        // Ordered pair check for cross-type compatibility
        if ((a == "floor" && b == "door") || (a == "door" && b == "floor")) return true;
        if ((a == "wall"  && b == "door") || (a == "door" && b == "wall"))  return true;
        if ((a == "water" && b == "bridge") || (a == "bridge" && b == "water")) return true;
        if ((a == "floor" && b == "bridge") || (a == "bridge" && b == "floor")) return true;
        return false;
    }

    /// <summary>
    /// Pre-compute per-tile, per-direction sets of compatible neighbor tile indices.
    /// Stored as _allowedByDir[tileIdx][dir] = HashSet of compatible tile indices.
    /// </summary>
    private void BuildAdjacencyLookup()
    {
        int n = _tiles.Count;
        _allowedByDir = new HashSet<int>[n][];
        for (int i = 0; i < n; i++)
        {
            _allowedByDir[i] = new HashSet<int>[4];
            for (int d = 0; d < 4; d++)
                _allowedByDir[i][d] = new HashSet<int>();

            for (int j = 0; j < n; j++)
            {
                // N: my[0] vs their[2]
                if (SocketsMatch(_tiles[i].Sockets[0], _tiles[j].Sockets[2]))
                    _allowedByDir[i][0].Add(j);
                // E: my[1] vs their[3]
                if (SocketsMatch(_tiles[i].Sockets[1], _tiles[j].Sockets[3]))
                    _allowedByDir[i][1].Add(j);
                // S: my[2] vs their[0]
                if (SocketsMatch(_tiles[i].Sockets[2], _tiles[j].Sockets[0]))
                    _allowedByDir[i][2].Add(j);
                // W: my[3] vs their[1]
                if (SocketsMatch(_tiles[i].Sockets[3], _tiles[j].Sockets[1]))
                    _allowedByDir[i][3].Add(j);
            }
        }
    }

    // ----------------------------------------------------------- Wave init

    private void InitWave(int seed)
    {
        _rng = new System.Random(seed);
        int n = _tiles.Count;
        _wave = new HashSet<int>[_height][];
        _grid = new int[_height, _width];
        for (int y = 0; y < _height; y++)
        {
            _wave[y] = new HashSet<int>[_width];
            for (int x = 0; x < _width; x++)
            {
                _wave[y][x] = new HashSet<int>(Enumerable.Range(0, n));
                _grid[y, x] = -1;
            }
        }
    }

    private void Reset(int seed)
    {
        InitWave(seed);
        if (_savedConstraints != null)
            ApplyConstraintsInternal(_savedConstraints);
    }

    // ----------------------------------------------------------- Constraints

    public void ApplyConstraints(Dictionary<Vector2Int, WFCTileType> constraints)
    {
        _savedConstraints = new Dictionary<Vector2Int, WFCTileType>(constraints);
        ApplyConstraintsInternal(constraints);
    }

    private void ApplyConstraintsInternal(Dictionary<Vector2Int, WFCTileType> constraints)
    {
        foreach (var kvp in constraints)
        {
            int x = kvp.Key.x, y = kvp.Key.y;
            if (x < 0 || x >= _width || y < 0 || y >= _height) continue;

            var allowed = new HashSet<int>();
            for (int i = 0; i < _tiles.Count; i++)
            {
                if (_tiles[i].Type == kvp.Value)
                    allowed.Add(i);
            }
            if (allowed.Count > 0)
                _wave[y][x] = allowed;
        }
    }

    // ----------------------------------------------------------- Entropy

    private float Entropy(int x, int y)
    {
        var options = _wave[y][x];
        if (options.Count <= 1) return float.PositiveInfinity;

        float total = 0f;
        foreach (int i in options)
            total += _tiles[i].Weight;
        if (total <= 0f) return float.PositiveInfinity;

        float entropy = 0f;
        foreach (int i in options)
        {
            float p = _tiles[i].Weight / total;
            if (p > 0f) entropy -= p * Mathf.Log(p);
        }
        // Small random noise for tiebreaking
        return entropy + (float)_rng.NextDouble() * 0.0001f;
    }

    // ----------------------------------------------------------- Observe

    /// <summary>
    /// Find the cell with lowest entropy, collapse it to a single tile
    /// chosen by weighted random selection.
    /// Returns true if observation succeeded (or grid is fully collapsed).
    /// Returns false on contradiction (empty candidate set).
    /// </summary>
    private bool Observe(out Vector2Int collapsed)
    {
        collapsed = default;
        float minEntropy = float.PositiveInfinity;
        var candidates = new List<Vector2Int>();

        for (int y = 0; y < _height; y++)
        {
            for (int x = 0; x < _width; x++)
            {
                if (_grid[y, x] != -1) continue;
                float e = Entropy(x, y);
                if (e < minEntropy)
                {
                    minEntropy = e;
                    candidates.Clear();
                    candidates.Add(new Vector2Int(x, y));
                }
                else if (Math.Abs(e - minEntropy) < 0.0001f)
                {
                    candidates.Add(new Vector2Int(x, y));
                }
            }
        }

        if (candidates.Count == 0)
            return true; // fully collapsed

        var chosen = candidates[_rng.Next(candidates.Count)];
        var options = _wave[chosen.y][chosen.x];

        if (options.Count == 0)
            return false; // contradiction

        // Weighted random selection
        float total = 0f;
        foreach (int i in options)
            total += _tiles[i].Weight;
        if (total <= 0f) return false;

        float r = (float)_rng.NextDouble() * total;
        int choice = -1;
        foreach (int i in options)
        {
            r -= _tiles[i].Weight;
            if (r <= 0f) { choice = i; break; }
        }
        if (choice < 0) choice = options.First();

        _wave[chosen.y][chosen.x] = new HashSet<int> { choice };
        _grid[chosen.y, chosen.x] = choice;
        collapsed = chosen;
        return true;
    }

    // ----------------------------------------------------------- Propagate

    /// <summary>
    /// Propagate constraints outward from a specific cell.
    /// Returns false if any cell's candidate set becomes empty (contradiction).
    /// </summary>
    private bool Propagate(Vector2Int start)
    {
        var queue = new Queue<Vector2Int>();
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            var cell = queue.Dequeue();
            var current = _wave[cell.y][cell.x];
            if (current.Count == 0) return false;

            for (int d = 0; d < 4; d++)
            {
                int nx = cell.x + Dirs[d].dx;
                int ny = cell.y + Dirs[d].dy;
                if (nx < 0 || ny < 0 || nx >= _width || ny >= _height) continue;

                var neighbor = _wave[ny][nx];
                if (neighbor.Count <= 1 && _grid[ny, nx] != -1) continue; // already collapsed

                // Compute union of allowed neighbors from all current options
                var allowed = new HashSet<int>();
                foreach (int tile in current)
                    allowed.UnionWith(_allowedByDir[tile][d]);

                // Intersect with what neighbor currently allows
                int beforeCount = neighbor.Count;
                neighbor.IntersectWith(allowed);

                if (neighbor.Count == 0) return false; // contradiction

                if (neighbor.Count < beforeCount)
                {
                    // Neighbor was reduced, propagate further
                    // If reduced to 1, auto-collapse
                    if (neighbor.Count == 1)
                    {
                        int only = neighbor.First();
                        _grid[ny, nx] = only;
                    }
                    queue.Enqueue(new Vector2Int(nx, ny));
                }
            }
        }
        return true;
    }

    // ----------------------------------------------------------- Collapse

    /// <summary>
    /// Run the full WFC collapse with restart-on-contradiction backtracking.
    /// Also verifies connectivity and restarts if the layout is disconnected.
    /// </summary>
    /// <param name="maxSteps">Max observe+propagate iterations per attempt.</param>
    /// <param name="maxRestarts">Max restart attempts on contradiction or disconnected layout. -1 uses MaxRestarts property.</param>
    /// <returns>True if a valid, connected layout was produced.</returns>
    public bool Collapse(int maxSteps = 50000, int maxRestarts = -1)
    {
        if (maxRestarts < 0) maxRestarts = MaxRestarts;

        var sw = Stopwatch.StartNew();
        LastRestartCount = 0;

        for (int attempt = 0; attempt <= maxRestarts; attempt++)
        {
            if (attempt > 0)
            {
                LastRestartCount = attempt;
                Reset(_baseSeed + attempt * 7919); // prime offset for diversity
            }

            bool success = CollapseInternal(maxSteps);

            if (!success)
            {
                Debug.Log($"[WFC] Contradiction on attempt {attempt + 1}/{maxRestarts + 1}, restarting...");
                continue;
            }

            if (!CheckConnectivity())
            {
                Debug.Log($"[WFC] Layout disconnected on attempt {attempt + 1}/{maxRestarts + 1}, restarting...");
                continue;
            }

            sw.Stop();
            LastElapsedSeconds = (float)sw.Elapsed.TotalSeconds;
            Debug.Log($"[WFC] Generated {_width}x{_height} layout in {LastElapsedSeconds:F1}s ({LastRestartCount} restarts)");
            return true;
        }

        // All attempts failed
        sw.Stop();
        LastElapsedSeconds = (float)sw.Elapsed.TotalSeconds;
        Debug.LogWarning($"[WFC] All {maxRestarts + 1} attempts failed for {_width}x{_height}. Generating fallback.");
        GenerateFallback();
        return false;
    }

    /// <summary>Single attempt at WFC collapse (no restarts).</summary>
    private bool CollapseInternal(int maxSteps)
    {
        // Initial propagation pass to reduce wave from constraints
        // Push all constrained cells
        for (int y = 0; y < _height; y++)
        {
            for (int x = 0; x < _width; x++)
            {
                if (_wave[y][x].Count < _tiles.Count)
                {
                    if (!Propagate(new Vector2Int(x, y)))
                        return false;
                }
            }
        }
        SyncSingletons();

        int steps = 0;
        while (steps < maxSteps)
        {
            if (!HasUncollapsed())
                return true; // fully collapsed

            steps++;
            if (!Observe(out var collapsed))
                return false; // contradiction during observe

            if (!Propagate(collapsed))
                return false; // contradiction during propagate

            SyncSingletons();
        }

        // Ran out of steps -- treat as failure if not fully collapsed
        return !HasUncollapsed();
    }

    private bool HasUncollapsed()
    {
        for (int y = 0; y < _height; y++)
            for (int x = 0; x < _width; x++)
                if (_grid[y, x] == -1) return true;
        return false;
    }

    // Constraint application leaves cells with wave={one_idx} but grid=-1.
    // Observe's Entropy() returns +Inf for count<=1 so those cells are never
    // selected, and the solver spins until max_steps. Sync them so the loop
    // can terminate.
    private void SyncSingletons()
    {
        for (int y = 0; y < _height; y++)
        {
            for (int x = 0; x < _width; x++)
            {
                if (_grid[y, x] == -1 && _wave[y][x].Count == 1)
                    _grid[y, x] = _wave[y][x].First();
            }
        }
    }

    // ----------------------------------------------------------- Connectivity

    /// <summary>
    /// BFS flood-fill from any walkable tile. Returns true if all walkable
    /// tiles are reachable (the layout is fully connected).
    /// </summary>
    public bool EnsureConnectivity()
    {
        return CheckConnectivity();
    }

    private bool CheckConnectivity()
    {
        var walkable = new HashSet<Vector2Int>();
        for (int y = 0; y < _height; y++)
        {
            for (int x = 0; x < _width; x++)
            {
                int idx = _grid[y, x];
                if (idx < 0) continue;
                if (IsWalkable(_tiles[idx].Type))
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

    private static bool IsWalkable(WFCTileType t)
    {
        return t == WFCTileType.Floor || t == WFCTileType.Door ||
               t == WFCTileType.Spawn || t == WFCTileType.Bridge;
    }

    // ----------------------------------------------------------- Fallback

    /// <summary>
    /// Generate a simple bordered floor layout as fallback when all WFC
    /// attempts fail. Produces a valid, boring but playable arena.
    /// </summary>
    private void GenerateFallback()
    {
        // Find tile indices for Wall and Floor (use first match)
        int wallIdx = -1, floorIdx = -1;
        for (int i = 0; i < _tiles.Count; i++)
        {
            if (wallIdx < 0 && _tiles[i].Type == WFCTileType.Wall) wallIdx = i;
            if (floorIdx < 0 && _tiles[i].Type == WFCTileType.Floor) floorIdx = i;
        }
        if (wallIdx < 0 || floorIdx < 0) return;

        for (int y = 0; y < _height; y++)
        {
            for (int x = 0; x < _width; x++)
            {
                bool border = (x == 0 || y == 0 || x == _width - 1 || y == _height - 1);
                _grid[y, x] = border ? wallIdx : floorIdx;
                int idx = _grid[y, x];
                _wave[y][x] = new HashSet<int> { idx };
            }
        }

        // Place saved spawn constraints if any
        if (_savedConstraints != null)
        {
            foreach (var kvp in _savedConstraints)
            {
                if (kvp.Value != WFCTileType.Spawn) continue;
                int x = kvp.Key.x, y = kvp.Key.y;
                if (x <= 0 || x >= _width - 1 || y <= 0 || y >= _height - 1) continue;
                int spawnIdx = -1;
                for (int i = 0; i < _tiles.Count; i++)
                {
                    if (_tiles[i].Type == WFCTileType.Spawn) { spawnIdx = i; break; }
                }
                if (spawnIdx >= 0)
                {
                    _grid[y, x] = spawnIdx;
                    _wave[y][x] = new HashSet<int> { spawnIdx };
                }
            }
        }
    }

    // ----------------------------------------------------------- Output

    /// <summary>
    /// Convert collapsed grid to Color array matching BuildFromBlueprint's
    /// ClassifyPixel expectations:
    ///   Wall types     -> (0, 0, 0)     black
    ///   Floor/Door     -> (128, 128, 128) gray
    ///   Water          -> (0, 0, 255)    blue
    ///   Spawn          -> (255, 255, 0)  yellow
    ///   Bridge         -> (128, 0, 128)  purple (matches COL_PURPLE in BuildFromBlueprint)
    ///   Void           -> (0, 0, 0)      black (treated as wall/empty)
    ///   Door           -> (128, 128, 128) gray  (walkable, same as floor)
    /// </summary>
    public Color[] ToBlueprintColors()
    {
        var colors = new Color[_width * _height];
        for (int y = 0; y < _height; y++)
        {
            for (int x = 0; x < _width; x++)
            {
                int idx = _grid[y, x];
                Color32 c;
                if (idx < 0)
                {
                    c = new Color32(0, 0, 0, 255);
                }
                else
                {
                    c = _tiles[idx].Type switch
                    {
                        WFCTileType.Wall or WFCTileType.WallCorner or
                        WFCTileType.WallT or WFCTileType.WallEnd or
                        WFCTileType.WallInterior                 => new Color32(0, 0, 0, 255),
                        WFCTileType.Floor or WFCTileType.Door    => new Color32(128, 128, 128, 255),
                        WFCTileType.Water                        => new Color32(0, 0, 255, 255),
                        WFCTileType.Spawn                        => new Color32(255, 255, 0, 255),
                        WFCTileType.Bridge                       => new Color32(128, 0, 128, 255),
                        _                                        => new Color32(0, 0, 0, 255) // Void
                    };
                }
                colors[y * _width + x] = c;
            }
        }
        return colors;
    }

    // ----------------------------------------------------------- Standalone generation

    /// <summary>
    /// High-level method for standalone layout generation (not just preprocessing).
    /// Sets up border walls and spawn constraints, then collapses.
    /// </summary>
    /// <param name="spawnCount">Number of spawns to place (1-8). Distributed across quadrants.</param>
    /// <param name="maxRestarts">Max restart attempts.</param>
    /// <returns>True if a valid connected layout was generated.</returns>
    public bool GenerateArenaLayout(int spawnCount = 4, int maxRestarts = -1)
    {
        var constraints = new Dictionary<Vector2Int, WFCTileType>();

        // Border edges and corners. Constraining corners to type Wall is structurally
        // unsolvable: the plain Wall tile has wall sockets only on N/S (or E/W after
        // rotation), but a corner cell needs wall sockets on two adjacent edges to
        // mate with both border runs. Corners must use WallCorner; edges use Wall.
        for (int x = 1; x < _width - 1; x++)
        {
            constraints[new Vector2Int(x, 0)] = WFCTileType.Wall;
            constraints[new Vector2Int(x, _height - 1)] = WFCTileType.Wall;
        }
        for (int y = 1; y < _height - 1; y++)
        {
            constraints[new Vector2Int(0, y)] = WFCTileType.Wall;
            constraints[new Vector2Int(_width - 1, y)] = WFCTileType.Wall;
        }
        constraints[new Vector2Int(0, 0)] = WFCTileType.WallCorner;
        constraints[new Vector2Int(_width - 1, 0)] = WFCTileType.WallCorner;
        constraints[new Vector2Int(0, _height - 1)] = WFCTileType.WallCorner;
        constraints[new Vector2Int(_width - 1, _height - 1)] = WFCTileType.WallCorner;

        // Distribute spawns across quadrants (up to 8 positions)
        var spawnPositions = new[]
        {
            new Vector2Int(_width / 4, _height / 4),
            new Vector2Int(3 * _width / 4, 3 * _height / 4),
            new Vector2Int(_width / 4, 3 * _height / 4),
            new Vector2Int(3 * _width / 4, _height / 4),
            new Vector2Int(_width / 2, _height / 4),
            new Vector2Int(_width / 2, 3 * _height / 4),
            new Vector2Int(_width / 4, _height / 2),
            new Vector2Int(3 * _width / 4, _height / 2),
        };

        int count = Mathf.Clamp(spawnCount, 0, spawnPositions.Length);
        for (int i = 0; i < count; i++)
        {
            var pos = spawnPositions[i];
            // Ensure spawn is not on the border
            pos.x = Mathf.Clamp(pos.x, 2, _width - 3);
            pos.y = Mathf.Clamp(pos.y, 2, _height - 3);
            constraints[pos] = WFCTileType.Spawn;
        }

        ApplyConstraints(constraints);
        return Collapse(maxSteps: _width * _height * 3, maxRestarts: maxRestarts);
    }
}
#endif
