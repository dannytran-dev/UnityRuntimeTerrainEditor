using System.Collections.Generic;
using RuntimeTerrainEditor.Sculpting;
using UnityEngine;
using UnityEngine.Rendering;

namespace RuntimeTerrainEditor
{
    /// <summary>
    /// Builds walls under the outer edges of the terrain so the map reads as a solid block. With several tiles only the
    /// sides without a neighbouring tile get a wall. The top of each wall sits exactly on the terrain's edge heightmap
    /// samples, and the walls reach <see cref="Depth"/> meters below the terrain base.
    /// </summary>
    public class TerrainBaseBoard : MonoBehaviour
    {
        // Sides of a tile, in this order: front (z = 0), back (z = max), left (x = 0), right (x = max)
        private static readonly Vector3[] WallNormals = { Vector3.back, Vector3.forward, Vector3.left, Vector3.right };
        private static readonly Vector2Int[] NeighborSteps = { Vector2Int.down, Vector2Int.up, Vector2Int.left, Vector2Int.right };

        [Tooltip("How far the walls reach below the terrain base (the lowest height the terrain can have), in meters")]
        [SerializeField, Min(0f)] private float depth = 100f;
        [Tooltip("Wall color when no material is set")]
        [SerializeField] private Color wallColor = new Color(0.608f, 0.463f, 0.325f, 1f);
        [Tooltip("Meters covered by one texture tile on the walls")]
        [SerializeField, Min(0.01f)] private float tileSize = 8.0f;
        [Tooltip("Optional. Wall texture when no material is set")]
        [SerializeField] private Texture wallTexture;
        [Tooltip("Optional. When empty a URP Lit material is created at runtime.")]
        [SerializeField] private Material material;

        private readonly List<TileWalls> _walls = new List<TileWalls>();
        private TerrainSurface _surface;
        private Material _wallMaterial;
        private Material _runtimeMaterial;

        /// <summary>How far the walls reach below the terrain base, in meters. Setting it rebuilds the walls.</summary>
        public float Depth
        {
            get => depth;
            set
            {
                depth = Mathf.Max(0f, value);
                UpdateTerrainBase();
            }
        }

        /// <summary>Destroys the walls and everything made for them at runtime.</summary>
        private void OnDestroy()
        {
            foreach (TileWalls walls in _walls)
                walls.Destroy();
            _walls.Clear();
            if (_runtimeMaterial != null)
                Destroy(_runtimeMaterial);
        }

        /// <summary>Creates walls for every outer side of the surface's tiles, as children of the tiles.</summary>
        public void Init(TerrainSurface surface)
        {
            _surface = surface;
            Rebuild();
        }

        /// <summary>Builds the walls again for the surface's current tiles, e.g. after tiles were added or removed.</summary>
        public void Rebuild()
        {
            if (_surface == null)
                return;
            foreach (TileWalls walls in _walls)
                walls.Destroy();
            _walls.Clear();

            if (_wallMaterial == null)
                _wallMaterial = material != null ? material : CreateMaterial();
            foreach (TerrainTile tile in _surface.Tiles)
            {
                List<int> openSides = new List<int>(4);
                for (int side = 0; side < 4; side++)
                {
                    if (_surface.GetNeighbor(tile, NeighborSteps[side].x, NeighborSteps[side].y) == null)
                        openSides.Add(side);
                }
                if (openSides.Count > 0)
                    _walls.Add(new TileWalls(tile, openSides.ToArray(), _wallMaterial));
            }
            UpdateTerrainBase();
        }

        /// <summary>
        /// Rebuilds the wall tops from the current terrain edge heights.
        /// </summary>
        public void UpdateTerrainBase()
        {
            foreach (TileWalls walls in _walls)
                walls.Update(depth, tileSize);
        }

        /// <summary>
        /// Rebuilds the wall tops of the tiles overlapping a world-space area (ignoring height), e.g. where a stroke
        /// changed the heights.
        /// </summary>
        public void UpdateTerrainBase(Bounds area)
        {
            foreach (TileWalls walls in _walls)
            {
                if (walls.Overlaps(area))
                    walls.Update(depth, tileSize);
            }
        }

        /// <summary>Creates a URP Lit material (Standard in the Built-in pipeline) with the wall color and texture.</summary>
        private Material CreateMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");
            _runtimeMaterial = new Material(shader) { name = "BaseBoard (Runtime)", color = wallColor };
            if (wallTexture != null)
                _runtimeMaterial.mainTexture = wallTexture;
            return _runtimeMaterial;
        }

        /// <summary>The walls along the outer sides of one terrain tile, as one mesh.</summary>
        private sealed class TileWalls
        {
            private readonly TerrainTile _tile;
            private readonly int[] _sides;
            private readonly GameObject _object;
            private readonly Mesh _mesh;
            // Heightmap resolution the mesh topology was built for
            private int _resolution;
            private Vector3[] _vertices;
            private Vector2[] _uvs;
            // Reused buffer for one side's edge heights
            private float[] _edge = System.Array.Empty<float>();

            /// <summary>Creates the wall object as a child of the tile's terrain.</summary>
            /// <param name="sides">The sides that get a wall (see WallNormals for the order).</param>
            public TileWalls(TerrainTile tile, int[] sides, Material material)
            {
                _tile = tile;
                _sides = sides;
                Terrain terrain = tile.Terrain;

                _object = new GameObject("BaseBoard") { layer = terrain.gameObject.layer };
                _object.transform.SetParent(terrain.transform, false);

                MeshRenderer meshRenderer = _object.AddComponent<MeshRenderer>();
                meshRenderer.sharedMaterial = material;
                meshRenderer.shadowCastingMode = ShadowCastingMode.Off;

                _mesh = new Mesh { name = "BaseBoard" };
                _mesh.MarkDynamic();
                _object.AddComponent<MeshFilter>().sharedMesh = _mesh;
            }

            /// <summary>Destroys the wall object and mesh.</summary>
            public void Destroy()
            {
                if (_mesh != null)
                    Object.Destroy(_mesh);
                if (_object != null)
                    Object.Destroy(_object);
            }

            /// <summary>True when the tile overlaps a world-space area (ignoring height).</summary>
            public bool Overlaps(Bounds area)
            {
                Bounds bounds = _tile.WorldBounds;
                return bounds.min.x <= area.max.x && bounds.max.x >= area.min.x && bounds.min.z <= area.max.z && bounds.max.z >= area.min.z;
            }

            /// <summary>Moves the wall tops onto the tile's current edge heights (read from its CPU copy).</summary>
            public void Update(float depth, float tileSize)
            {
                if (_tile.Terrain == null)
                    return;

                int resolution = _tile.HeightmapResolution;
                if (resolution != _resolution)
                    BuildTopology(resolution);

                int last = resolution - 1;
                Vector3 size = _tile.Size;
                Vector3 position = _tile.Position;
                for (int wall = 0; wall < _sides.Length; wall++)
                {
                    int side = _sides[wall];
                    float[] heights = ReadEdge(side, resolution);
                    // One wall column per heightmap sample, so the wall top matches the terrain edge vertex for vertex
                    for (int i = 0; i < resolution; i++)
                    {
                        float fraction = (float)i / last;
                        float height = heights[i] * size.y;
                        bool alongX = side <= 1;
                        Vector3 top = alongX
                            ? new Vector3(fraction * size.x, height, side == 0 ? 0f : size.z)
                            : new Vector3(side == 2 ? 0f : size.x, height, fraction * size.z);
                        // World distance along the wall, so the texture continues from one tile's wall to the next
                        float distance = alongX ? position.x + top.x : position.z + top.z;
                        SetColumn(wall, i, top, distance, depth, tileSize);
                    }
                }

                _mesh.SetVertices(_vertices);
                _mesh.SetUVs(0, _uvs);
                _mesh.RecalculateBounds();
            }

            /// <summary>The normalized heights along one side of the tile, in a reused buffer.</summary>
            private float[] ReadEdge(int side, int resolution)
            {
                int last = resolution - 1;
                RectInt edge = side <= 1
                    ? new RectInt(0, side == 0 ? 0 : last, resolution, 1)
                    : new RectInt(side == 2 ? 0 : last, 0, 1, resolution);
                if (_edge.Length < resolution)
                    _edge = new float[resolution];
                _tile.ReadHeightRegion(edge, _edge);
                return _edge;
            }

            /// <summary>
            /// Places one column of a wall: its top on the terrain edge and its bottom <paramref name="depth"/> below the base.
            /// </summary>
            /// <param name="wall">Index of the wall in this mesh.</param>
            /// <param name="i">Column index along the wall.</param>
            /// <param name="top">Top of the column in the terrain's local space.</param>
            /// <param name="distance">Distance along the wall in meters, for the texture coordinates.</param>
            private void SetColumn(int wall, int i, Vector3 top, float distance, float depth, float tileSize)
            {
                int index = (wall * _resolution + i) * 2;
                float u = distance / tileSize;
                _vertices[index] = top;
                _vertices[index + 1] = new Vector3(top.x, -depth, top.z);
                // World scale UVs so tall walls don't stretch the texture
                _uvs[index] = new Vector2(u, top.y / tileSize);
                _uvs[index + 1] = new Vector2(u, -depth / tileSize);
            }

            /// <summary>
            /// Builds the triangles and normals. They only change with the heightmap resolution; updates just move the vertices.
            /// </summary>
            private void BuildTopology(int resolution)
            {
                _resolution = resolution;
                int columns = resolution * _sides.Length;
                _vertices = new Vector3[columns * 2];
                _uvs = new Vector2[columns * 2];
                Vector3[] normals = new Vector3[columns * 2];
                int[] triangles = new int[_sides.Length * (resolution - 1) * 6];

                for (int wall = 0, index = 0; wall < _sides.Length; wall++)
                {
                    int side = _sides[wall];
                    int first = wall * resolution * 2;
                    for (int i = 0; i < resolution * 2; i++)
                        normals[first + i] = WallNormals[side];

                    // Front and right walls face outward with this winding, back and left need it flipped
                    bool flip = side == 1 || side == 2;
                    for (int i = 0; i < resolution - 1; i++)
                    {
                        int top0 = first + i * 2;
                        int bottom0 = top0 + 1;
                        int top1 = top0 + 2;
                        int bottom1 = top0 + 3;
                        if (!flip)
                        {
                            AddTriangle(triangles, ref index, top0, top1, bottom0);
                            AddTriangle(triangles, ref index, bottom0, top1, bottom1);
                        }
                        else
                        {
                            AddTriangle(triangles, ref index, top0, bottom0, top1);
                            AddTriangle(triangles, ref index, bottom0, bottom1, top1);
                        }
                    }
                }

                _mesh.Clear();
                _mesh.indexFormat = _vertices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                _mesh.SetVertices(_vertices);
                _mesh.SetNormals(normals);
                _mesh.SetUVs(0, _uvs);
                _mesh.SetTriangles(triangles, 0);
            }

            /// <summary>Writes one triangle's vertex indices at <paramref name="index"/> and moves it past them.</summary>
            private static void AddTriangle(int[] triangles, ref int index, int a, int b, int c)
            {
                triangles[index++] = a;
                triangles[index++] = b;
                triangles[index++] = c;
            }
        }
    }
}
