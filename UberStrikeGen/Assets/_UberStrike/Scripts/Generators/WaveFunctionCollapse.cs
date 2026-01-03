#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UnityAI
{
    /// <summary>
    /// Simple tile based Wave Function Collapse generator that can learn
    /// adjacency rules from existing layout images.
    /// </summary>
    public class WFCGenerator
    {
        private static readonly Vector2Int[] Directions =
        {
            new Vector2Int(1, 0),
            new Vector2Int(-1, 0),
            new Vector2Int(0, 1),
            new Vector2Int(0, -1)
        };

        public Texture2D GenerateLayout(WFCRuleset rules, int size = 256)
        {
            if (rules == null)
                throw new ArgumentNullException(nameof(rules));

            int width = Mathf.Max(16, size);
            int height = Mathf.Max(16, size);

            var grid = new Cell[width, height];
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    grid[x, y] = new Cell(rules.AllTiles);
                }
            }

            var rand = new System.Random();
            while (true)
            {
                Vector2Int? pos = FindLowestEntropyCell(grid, rand);
                if (!pos.HasValue)
                    break; // collapsed

                var cell = grid[pos.Value.x, pos.Value.y];
                Tile choice = cell.Collapse(rand);
                grid[pos.Value.x, pos.Value.y] = cell;
                Propagate(grid, pos.Value, rules);
            }

            return RenderTexture(grid, rules);
        }

        public WFCRuleset LearnRulesFromExisting(string imagePath)
        {
            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
                throw new FileNotFoundException("Layout image not found", imagePath);

            byte[] data = File.ReadAllBytes(imagePath);
            var tex = new Texture2D(2, 2);
            tex.LoadImage(data);

            var tiles = new HashSet<Tile>();
            var adjacency = new Dictionary<Tile, TileNeighbours>();

            for (int x = 0; x < tex.width; x++)
            {
                for (int y = 0; y < tex.height; y++)
                {
                    Tile tile = Classify(tex.GetPixel(x, y));
                    tiles.Add(tile);

                    if (!adjacency.TryGetValue(tile, out var neighbours))
                    {
                        neighbours = new TileNeighbours();
                        adjacency[tile] = neighbours;
                    }

                    foreach (var dir in Directions)
                    {
                        int nx = x + dir.x;
                        int ny = y + dir.y;
                        if (nx < 0 || nx >= tex.width || ny < 0 || ny >= tex.height)
                            continue;

                        Tile neighbour = Classify(tex.GetPixel(nx, ny));
                        neighbours.Add(dir, neighbour);
                    }
                }
            }

            return new WFCRuleset(tiles, adjacency);
        }

        private static void Propagate(Cell[,] grid, Vector2Int position, WFCRuleset rules)
        {
            var queue = new Queue<Vector2Int>();
            queue.Enqueue(position);

            while (queue.Count > 0)
            {
                Vector2Int current = queue.Dequeue();
                var cell = grid[current.x, current.y];

                foreach (var dir in Directions)
                {
                    Vector2Int neighbourPos = current + dir;
                    if (neighbourPos.x < 0 || neighbourPos.x >= grid.GetLength(0) || neighbourPos.y < 0 || neighbourPos.y >= grid.GetLength(1))
                        continue;

                    if (grid[neighbourPos.x, neighbourPos.y].Constrain(rules, dir, cell))
                    {
                        queue.Enqueue(neighbourPos);
                    }
                }
            }
        }

        private static Vector2Int? FindLowestEntropyCell(Cell[,] grid, System.Random rand)
        {
            float lowestEntropy = float.MaxValue;
            Vector2Int? lowest = null;

            for (int x = 0; x < grid.GetLength(0); x++)
            {
                for (int y = 0; y < grid.GetLength(1); y++)
                {
                    var cell = grid[x, y];
                    if (cell.IsCollapsed)
                        continue;

                    float entropy = cell.Entropy;
                    // tiebreaker randomness to avoid bias
                    entropy += (float)rand.NextDouble() * 0.001f;

                    if (entropy < lowestEntropy)
                    {
                        lowestEntropy = entropy;
                        lowest = new Vector2Int(x, y);
                    }
                }
            }

            return lowest;
        }

        private static Texture2D RenderTexture(Cell[,] grid, WFCRuleset rules)
        {
            int width = grid.GetLength(0);
            int height = grid.GetLength(1);
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point
            };

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    Tile tile = grid[x, y].ResolvedTile;
                    tex.SetPixel(x, y, rules.GetColor(tile));
                }
            }

            tex.Apply();
            return tex;
        }

        private static Tile Classify(Color pixel)
        {
            if (pixel.r < 0.2f && pixel.g < 0.2f && pixel.b < 0.2f)
                return Tile.Wall;
            if (pixel.r > 0.8f && pixel.g > 0.8f && pixel.b < 0.3f)
                return Tile.Spawn;
            if (pixel.b > 0.8f && pixel.r < 0.3f && pixel.g < 0.3f)
                return Tile.Water;
            if (pixel.b > 0.7f && pixel.g > 0.7f)
                return Tile.Border;
            if (pixel.r > 0.5f && pixel.b > 0.5f)
                return Tile.Bridge;
            return Tile.Floor;
        }

        private class Cell
        {
            private readonly List<Tile> _options;

            public Cell(IEnumerable<Tile> tiles)
            {
                _options = new List<Tile>(tiles);
            }

            public bool IsCollapsed => _options.Count == 1;
            public float Entropy => Mathf.Log(Mathf.Max(_options.Count, 1));

            public Tile ResolvedTile => _options.Count > 0 ? _options[0] : Tile.Floor;

            public Tile Collapse(System.Random random)
            {
                if (_options.Count == 0)
                {
                    _options.Add(Tile.Floor);
                }

                int index = random.Next(_options.Count);
                Tile choice = _options[index];
                _options.Clear();
                _options.Add(choice);
                return choice;
            }

            public bool Constrain(WFCRuleset rules, Vector2Int direction, Cell source)
            {
                if (IsCollapsed)
                    return false;

                bool removed = false;
                var allowed = new HashSet<Tile>();
                foreach (var tile in source._options)
                {
                    foreach (var candidate in rules.GetAllowed(tile, direction))
                    {
                        allowed.Add(candidate);
                    }
                }

                for (int i = _options.Count - 1; i >= 0; i--)
                {
                    if (!allowed.Contains(_options[i]))
                    {
                        _options.RemoveAt(i);
                        removed = true;
                    }
                }

                if (_options.Count == 0)
                {
                    _options.Add(Tile.Floor);
                }

                return removed;
            }
        }

        public enum Tile
        {
            Floor,
            Wall,
            Spawn,
            Border,
            Bridge,
            Water
        }

        public class WFCRuleset
        {
            private readonly Dictionary<Tile, TileNeighbours> _neighbours;
            private readonly Dictionary<Tile, Color> _colors;

            public WFCRuleset(IEnumerable<Tile> tiles, Dictionary<Tile, TileNeighbours> neighbours)
            {
                _neighbours = neighbours;
                _colors = new Dictionary<Tile, Color>
                {
                    [Tile.Floor] = Color.gray,
                    [Tile.Wall] = Color.black,
                    [Tile.Spawn] = Color.yellow,
                    [Tile.Border] = Color.cyan,
                    [Tile.Bridge] = new Color(0.5f, 0f, 0.5f),
                    [Tile.Water] = Color.blue
                };

                var tileSet = new HashSet<Tile>(tiles);
                foreach (Tile tile in Enum.GetValues(typeof(Tile)))
                {
                    tileSet.Add(tile);
                }

                AllTiles = new List<Tile>(tileSet);

                foreach (var tile in AllTiles)
                {
                    if (!_neighbours.ContainsKey(tile))
                    {
                        _neighbours[tile] = new TileNeighbours();
                    }
                }
            }

            public List<Tile> AllTiles { get; }

            public IEnumerable<Tile> GetAllowed(Tile tile, Vector2Int direction)
            {
                if (_neighbours.TryGetValue(tile, out var neighbours))
                {
                    return neighbours.Get(direction);
                }

                return AllTiles;
            }

            public Color GetColor(Tile tile)
            {
                if (_colors.TryGetValue(tile, out Color color))
                    return color;
                return Color.white;
            }
        }

        public class TileNeighbours
        {
            private readonly Dictionary<Vector2Int, HashSet<Tile>> _map = new Dictionary<Vector2Int, HashSet<Tile>>();

            public void Add(Vector2Int direction, Tile tile)
            {
                if (!_map.TryGetValue(direction, out var set))
                {
                    set = new HashSet<Tile>();
                    _map[direction] = set;
                }

                set.Add(tile);
            }

            public IEnumerable<Tile> Get(Vector2Int direction)
            {
                if (_map.TryGetValue(direction, out var set) && set.Count > 0)
                    return set;
                return Enum.GetValues(typeof(Tile)) as Tile[];
            }
        }
    }

    public static class WFCGeneratorMenu
    {
        [MenuItem("Tools/UberStrike/Generate Layout/WFC From Sample", priority = 230)]
        private static void GenerateFromSample()
        {
            string imagePath = EditorUtility.OpenFilePanel("Select layout image", Application.dataPath, "png");
            if (string.IsNullOrEmpty(imagePath))
                return;

            var generator = new WFCGenerator();
            var rules = generator.LearnRulesFromExisting(imagePath);
            var layout = generator.GenerateLayout(rules, 256);

            string folder = EditorUtility.SaveFolderPanel("Save generated layout", Application.dataPath, "");
            if (string.IsNullOrEmpty(folder))
                return;

            byte[] png = layout.EncodeToPNG();
            string output = Path.Combine(folder, "wfc_layout.png");
            File.WriteAllBytes(output, png);
            AssetDatabase.Refresh();

            Debug.Log($"[WFCGenerator] Generated layout saved to {output}");
        }
    }
}
#endif
