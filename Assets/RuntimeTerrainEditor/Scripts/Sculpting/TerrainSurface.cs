using System;
using System.Collections.Generic;
using UnityEngine;

namespace RuntimeTerrainEditor.Sculpting
{
    /// <summary>
    /// The editable ground: one Terrain, or several Terrain tiles edited as one surface. Runs <see cref="ITerrainTool"/>s
    /// on every tile a brush dab touches, keeps the heights of neighbouring tiles equal along their shared edges, and
    /// collects a stroke over all tiles into one undo step. Each tile keeps its own CPU copies (see <see cref="TerrainTile"/>).
    /// Tiles can be added to free slots of the grid and removed again while the game runs.
    /// All edits should go through here so the copies, the TerrainData and the undo history stay consistent;
    /// call <see cref="Reload"/> if something else changes the TerrainData or moves a terrain.
    /// </summary>
    public sealed class TerrainSurface
    {
        private const int RaycastRefineSteps = 16;
        // Fraction of a tile two slot positions may differ by and still count as the same slot
        private const float SlotTolerance = 0.001f;

        // The four sides of a tile as grid steps: left, right, front, back
        private static readonly Vector2Int[] SideSteps = { Vector2Int.left, Vector2Int.right, Vector2Int.down, Vector2Int.up };

        private readonly List<TerrainTile> _tiles = new List<TerrainTile>();
        private readonly Dictionary<Vector2Int, TerrainTile> _grid = new Dictionary<Vector2Int, TerrainTile>();
        private bool _strokeActive;
        // Corner of grid cell (0, 0) and the number of cells along X and Z
        private Vector3 _gridOrigin;
        private Vector2Int _gridSize = Vector2Int.one;
        private Bounds _worldBounds;
        // Reused seam buffers (see CloseSeam)
        private float[] _seamFirst = Array.Empty<float>();
        private float[] _seamSecond = Array.Empty<float>();

        /// <summary>The tiles, sorted by position (rows along Z, then X).</summary>
        public IReadOnlyList<TerrainTile> Tiles => _tiles;

        /// <summary>
        /// True when the tiles are the same size and resolution and sit on a regular grid, so their seams are kept closed.
        /// Always true for a single terrain.
        /// </summary>
        public bool IsGrid { get; private set; }

        /// <summary>True between <see cref="BeginStroke"/> and <see cref="EndStroke"/>.</summary>
        public bool IsStrokeActive => _strokeActive;

        /// <summary>Goes up whenever tiles are added or removed.</summary>
        public int LayoutVersion { get; private set; }

        /// <summary>Goes up whenever heights change, so visuals that follow the ground know when to rebuild.</summary>
        public int HeightsVersion => TerrainTile.HeightsVersion;

        /// <summary>World-space box around all tiles, spanning their full height.</summary>
        public Bounds WorldBounds => _worldBounds;

        /// <summary>Size of the whole surface in meters (x and z across all tiles, y the terrain height).</summary>
        public Vector3 Size
        {
            get
            {
                Vector3 size = _worldBounds.size;
                return new Vector3(size.x, _tiles[0].Size.y, size.z);
            }
        }

        /// <summary>Distance between heightmap samples in meters (x along terrain X, y along terrain Z).</summary>
        public Vector2 HeightCellSize => _tiles[0].HeightCellSize;

        /// <summary>Size of one tile in meters (tiles on a grid share their size).</summary>
        public Vector3 TileSize => _tiles[0].Size;

        /// <summary>Number of terrain layers (tiles share their layers).</summary>
        public int LayerCount => _tiles[0].LayerCount;

        /// <summary>Number of tree types (tiles share their tree types).</summary>
        public int TreeTypeCount => _tiles[0].TreeTypeCount;

        /// <summary>Number of grass (detail) types that can be painted (tiles share their grass types).</summary>
        public int DetailTypeCount => _tiles[0].DetailTypeCount;

        /// <summary>Number of trees on all tiles.</summary>
        public int TreeCount
        {
            get
            {
                int count = 0;
                foreach (TerrainTile tile in _tiles)
                    count += tile.TreeCount;
                return count;
            }
        }

        /// <summary>
        /// Seconds between terrain collider and LOD refreshes while a stroke is running; 0 refreshes every
        /// <see cref="FlushUploads"/>, infinity only when the stroke ends.
        /// </summary>
        public float HeightSyncInterval
        {
            get => _tiles[0].HeightSyncInterval;
            set
            {
                foreach (TerrainTile tile in _tiles)
                    tile.HeightSyncInterval = value;
            }
        }

        /// <summary>Takes CPU copies of one terrain.</summary>
        public TerrainSurface(Terrain terrain) : this(new[] { terrain })
        {
        }

        /// <summary>
        /// Takes CPU copies of several terrain tiles. Tiles of the same size and resolution placed side by side on a grid
        /// (like Unity's Create Neighbor Terrains makes them) get their seams kept closed.
        /// </summary>
        public TerrainSurface(IEnumerable<Terrain> terrains)
        {
            foreach (Terrain terrain in terrains)
            {
                if (terrain != null)
                    _tiles.Add(new TerrainTile(terrain));
            }
            if (_tiles.Count == 0)
                throw new ArgumentException("A terrain surface needs at least one terrain.", nameof(terrains));

            _tiles.Sort(CompareByPosition);
            UpdateLayout();
            if (!IsGrid)
                Debug.LogWarning("TerrainSurface: the terrains aren't tiles of the same size and resolution on a grid, so the seams between them aren't kept closed.");
        }

        /// <summary>Re-reads everything from every tile's TerrainData, and where the tiles are. Cancels a running stroke.</summary>
        public void Reload()
        {
            _strokeActive = false;
            foreach (TerrainTile tile in _tiles)
                tile.Reload();
            UpdateLayout();
        }

        /// <summary>
        /// Sets every height to a normalized value, fills one layer and closes all holes. Cancels a running stroke.
        /// </summary>
        public void Fill(float normalizedHeight, int layer = 0)
        {
            _strokeActive = false;
            foreach (TerrainTile tile in _tiles)
                tile.Fill(normalizedHeight, layer);
        }

        /// <summary>Removes all grass and trees. Cancels a running stroke.</summary>
        public void ClearVegetation()
        {
            _strokeActive = false;
            foreach (TerrainTile tile in _tiles)
                tile.ClearVegetation();
        }

        /// <summary>Starts collecting changes on all tiles into one undo step.</summary>
        public void BeginStroke()
        {
            _strokeActive = true;
            foreach (TerrainTile tile in _tiles)
                tile.BeginStroke();
        }

        /// <summary>
        /// Applies one brush dab to every tile it touches. Outside a stroke the change is uploaded immediately and isn't
        /// recorded for undo. During a stroke the CPU copies change right away (so sampling and raycasts see it), and the
        /// terrains get the change on <see cref="FlushUploads"/> or <see cref="EndStroke"/>.
        /// </summary>
        /// <returns>False when nothing was touched (brush off the terrain, nothing to paint with, ...).</returns>
        public bool Apply(ITerrainTool tool, in BrushDab dab)
        {
            if (tool == null || dab.Shape == null)
                return false;

            bool applied = false;
            foreach (TerrainTile tile in _tiles)
            {
                if (tile.Apply(tool, dab))
                    applied = true;
            }

            // Tools can compute different edge heights on each side of a seam (smoothing sees only its own tile)
            if (applied && tool.Channel == TerrainChannel.Heights && IsGrid && _tiles.Count > 1)
                CloseSeams(dab);
            return applied;
        }

        /// <summary>
        /// Uploads the changes the running stroke made since the last call to the terrains. Call it once per frame while a
        /// stroke runs, after the frame's dabs; colliders and trees follow at their sync intervals.
        /// </summary>
        public void FlushUploads()
        {
            foreach (TerrainTile tile in _tiles)
                tile.FlushUploads(false);
        }

        /// <summary>Finishes the stroke and returns its undo step, or null when the stroke changed nothing.</summary>
        public TerrainStrokeRecord EndStroke()
        {
            if (!_strokeActive)
                return null;
            _strokeActive = false;

            List<TerrainStrokeRecord.TileChanges> changes = new List<TerrainStrokeRecord.TileChanges>();
            foreach (TerrainTile tile in _tiles)
            {
                TerrainStrokeRecord.TileChanges tileChanges = tile.EndStroke();
                if (tileChanges != null)
                    changes.Add(tileChanges);
            }
            return changes.Count > 0 ? new TerrainStrokeRecord(changes.ToArray()) : null;
        }

        /// <summary>
        /// Adds a terrain as a new tile. The tiles have to form a grid, and the terrain has to fit into it: the same size
        /// and heightmap resolution, a whole number of tiles away from the others and not on top of one.
        /// Not allowed during a stroke.
        /// </summary>
        /// <returns>The new tile.</returns>
        public TerrainTile AddTile(Terrain terrain)
        {
            if (terrain == null)
                throw new ArgumentNullException(nameof(terrain));
            TerrainTile tile = new TerrainTile(terrain);
            AddTile(tile);
            return tile;
        }

        /// <summary>
        /// Puts a tile taken off with <see cref="RemoveTile"/> back (it has to fit the grid, like <see cref="AddTile(Terrain)"/>).
        /// </summary>
        public void AddTile(TerrainTile tile)
        {
            if (tile == null)
                throw new ArgumentNullException(nameof(tile));
            if (_strokeActive)
                throw new InvalidOperationException("Tiles can't be added during a stroke.");
            if (_tiles.Contains(tile))
                return;
            if (!IsGrid)
                throw new InvalidOperationException("Tiles can only be added when the terrains form a grid.");
            if (!FitsGrid(tile))
                throw new ArgumentException("The terrain doesn't line up with the grid of the other tiles, or overlaps one.", nameof(tile));

            tile.HeightSyncInterval = HeightSyncInterval;
            _tiles.Add(tile);
            _tiles.Sort(CompareByPosition);
            UpdateLayout();
        }

        /// <summary>
        /// Takes a tile off the surface. Its terrain and TerrainData are left as they are, so it can be put back with
        /// <see cref="AddTile(TerrainTile)"/>. Not allowed during a stroke.
        /// </summary>
        /// <returns>False when the tile isn't part of the surface or is its last tile.</returns>
        public bool RemoveTile(TerrainTile tile)
        {
            if (_strokeActive)
                throw new InvalidOperationException("Tiles can't be removed during a stroke.");
            if (_tiles.Count <= 1 || !_tiles.Remove(tile))
                return false;
            UpdateLayout();
            return true;
        }

        /// <summary>
        /// Makes the heights along a tile's sides equal to its neighbours' (both sides of a seam get their average), e.g.
        /// after adding a tile. Inside a stroke the change is recorded for undo.
        /// </summary>
        public void CloseSeams(TerrainTile tile)
        {
            if (!IsGrid || _tiles.Count < 2 || !_tiles.Contains(tile))
                return;
            Vector3 size = tile.Size;
            // A dab as big as the tile reaches all its seams, and the corners it shares with diagonal tiles
            CloseSeams(new BrushDab(tile.WorldBounds.center, Mathf.Max(size.x, size.z), BrushShape.Solid, 1f));
        }

        /// <summary>
        /// The corner (smallest X and Z) of the grid slot a world position lies in, lined up with the tiles. A tile added
        /// there has this position.
        /// </summary>
        public Vector3 GetSlotPosition(Vector3 worldPosition)
        {
            Vector3 anchor = _tiles[0].Position;
            Vector3 size = TileSize;
            float x = anchor.x + Mathf.Floor((worldPosition.x - anchor.x) / size.x) * size.x;
            float z = anchor.z + Mathf.Floor((worldPosition.z - anchor.z) / size.z) * size.z;
            return new Vector3(x, anchor.y, z);
        }

        /// <summary>The tile whose corner is at a slot position (see <see cref="GetSlotPosition"/>), or null.</summary>
        public TerrainTile GetTileAtSlot(Vector3 slotPosition)
        {
            if (IsGrid)
            {
                Vector3 size = TileSize;
                Vector2Int cell = new Vector2Int(Mathf.RoundToInt((slotPosition.x - _gridOrigin.x) / size.x),
                                                 Mathf.RoundToInt((slotPosition.z - _gridOrigin.z) / size.z));
                return _grid.TryGetValue(cell, out TerrainTile tile) && IsSameSlot(tile.Position, slotPosition) ? tile : null;
            }

            foreach (TerrainTile tile in _tiles)
            {
                if (IsSameSlot(tile.Position, slotPosition))
                    return tile;
            }
            return null;
        }

        /// <summary>
        /// True when a tile can be added at a slot: the tiles form a grid, nothing is there yet and the slot shares a side
        /// with a tile.
        /// </summary>
        public bool IsFreeSlot(Vector3 slotPosition)
        {
            if (!IsGrid || GetTileAtSlot(slotPosition) != null)
                return false;
            foreach (Vector2Int step in SideSteps)
            {
                if (GetTileAtSlot(GetSideSlot(slotPosition, step)) != null)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Fills a list with the corner of every slot a tile can be added to (see <see cref="IsFreeSlot"/>). Empty when the
        /// tiles don't form a grid.
        /// </summary>
        public void GetFreeSlots(List<Vector3> slots)
        {
            slots.Clear();
            if (!IsGrid)
                return;
            foreach (TerrainTile tile in _tiles)
            {
                foreach (Vector2Int step in SideSteps)
                {
                    Vector3 slot = GetSideSlot(tile.Position, step);
                    if (GetTileAtSlot(slot) == null && !ContainsSlot(slots, slot))
                        slots.Add(slot);
                }
            }
        }

        /// <summary>The tile under a world position (ignoring height), or null when there is none.</summary>
        public TerrainTile GetTile(Vector3 worldPosition)
        {
            if (IsGrid)
            {
                // A position on a shared edge belongs to either tile; check the cells it touches
                Vector2Int cell = GetGridCell(worldPosition);
                for (int dz = 0; dz >= -1; dz--)
                {
                    for (int dx = 0; dx >= -1; dx--)
                    {
                        if (_grid.TryGetValue(new Vector2Int(cell.x + dx, cell.y + dz), out TerrainTile tile) && tile.DistanceTo(worldPosition) <= 0f)
                            return tile;
                    }
                }
                return null;
            }

            foreach (TerrainTile tile in _tiles)
            {
                if (tile.DistanceTo(worldPosition) <= 0f)
                    return tile;
            }
            return null;
        }

        /// <summary>
        /// The tile next to another one in the grid (step -1 or 1 along X and/or Z), or null when there is none or the
        /// tiles don't form a grid.
        /// </summary>
        public TerrainTile GetNeighbor(TerrainTile tile, int stepX, int stepZ)
        {
            if (!IsGrid)
                return null;
            _grid.TryGetValue(tile.GridPosition + new Vector2Int(stepX, stepZ), out TerrainTile neighbor);
            return neighbor;
        }

        /// <summary>
        /// World height of the ground under a position; outside the tiles, of the nearest tile's edge. Read from the CPU
        /// copies, so it includes edits of a running stroke that weren't uploaded yet.
        /// </summary>
        public float SampleHeight(Vector3 worldPosition)
        {
            return GetNearestTile(worldPosition).SampleHeight(worldPosition);
        }

        /// <summary>World height of the ground under a position, or false when no tile is under it. Holes are ignored.</summary>
        public bool TrySampleHeight(Vector3 worldPosition, out float height)
        {
            TerrainTile tile = GetTile(worldPosition);
            height = tile != null ? tile.SampleHeight(worldPosition) : 0f;
            return tile != null;
        }

        /// <summary>Surface normal of the ground under a position (of the nearest tile's edge outside the tiles).</summary>
        public Vector3 SampleNormal(Vector3 worldPosition)
        {
            TerrainTile tile = GetNearestTile(worldPosition);
            Vector3 local = worldPosition - tile.Position;
            Vector3 size = tile.Size;
            return tile.Data.GetInterpolatedNormal(Mathf.Clamp01(local.x / size.x), Mathf.Clamp01(local.z / size.z));
        }

        /// <summary>
        /// Finds where a ray first meets the ground by stepping along it over the heightmaps. Unlike a collider raycast
        /// it sees the heights as soon as they are edited, and holes don't let it through, so holes can be filled again.
        /// </summary>
        public bool Raycast(Ray ray, float maxDistance, out Vector3 point)
        {
            point = default;
            if (!IntersectBounds(_worldBounds, ray, out float enter, out float exit))
                return false;
            float end = Mathf.Min(exit, maxDistance);
            float start = Mathf.Max(enter, 0f);
            if (start > end)
                return false;

            Vector2 cell = HeightCellSize;
            float step = Mathf.Max(0.01f, Mathf.Min(cell.x, cell.y));
            bool wasAbove = IsAboveGround(ray.GetPoint(start));
            float previous = start;
            for (float distance = start + step; previous < end; distance += step)
            {
                distance = Mathf.Min(distance, end);
                bool above = IsAboveGround(ray.GetPoint(distance));
                if (wasAbove && !above)
                {
                    point = ray.GetPoint(RefineCrossing(ray, previous, distance));
                    return true;
                }
                wasAbove = above;
                previous = distance;
            }
            return false;
        }

        /// <summary>
        /// The heights of all tiles joined into one grid [z, x], normalized to the terrain height. Missing grid tiles are 0.
        /// </summary>
        public float[,] GetHeights()
        {
            if (_tiles.Count == 1)
                return _tiles[0].Data.GetHeights(0, 0, _tiles[0].HeightmapResolution, _tiles[0].HeightmapResolution);
            if (!IsGrid)
                throw new InvalidOperationException("The terrains don't form a grid, so their heights can't be joined.");

            int steps = _tiles[0].HeightmapResolution - 1;
            Vector2Int gridSize = GetGridSize();
            float[,] heights = new float[gridSize.y * steps + 1, gridSize.x * steps + 1];
            foreach (TerrainTile tile in _tiles)
            {
                float[,] tileHeights = tile.Data.GetHeights(0, 0, steps + 1, steps + 1);
                int offsetX = tile.GridPosition.x * steps;
                int offsetZ = tile.GridPosition.y * steps;
                for (int z = 0; z <= steps; z++)
                    for (int x = 0; x <= steps; x++)
                        heights[offsetZ + z, offsetX + x] = tileHeights[z, x];
            }
            return heights;
        }

        /// <summary>Tiles along X (x) and Z (y) in the grid.</summary>
        public Vector2Int GetGridSize()
        {
            return _gridSize;
        }

        /// <summary>
        /// Makes the shared edge heights of neighbouring tiles equal around a dab. Vertical seams are closed before
        /// horizontal ones, so a corner shared by four tiles ends up with one height.
        /// </summary>
        private void CloseSeams(in BrushDab dab)
        {
            float reach = dab.Diameter * 0.5f * 1.42f + HeightCellSize.x; // rotated square corners reach a bit further
            foreach (TerrainTile tile in _tiles)
            {
                TerrainTile right = GetNeighbor(tile, 1, 0);
                if (right != null && Mathf.Abs(right.Position.x - dab.Center.x) <= reach)
                    CloseSeam(tile, right, true, dab.Center.z, reach);
            }
            foreach (TerrainTile tile in _tiles)
            {
                TerrainTile top = GetNeighbor(tile, 0, 1);
                if (top != null && Mathf.Abs(top.Position.z - dab.Center.z) <= reach)
                    CloseSeam(tile, top, false, dab.Center.x, reach);
            }
        }

        /// <summary>
        /// Sets both sides of a seam to their average height, for the samples within <paramref name="reach"/> of a position
        /// along the seam.
        /// </summary>
        /// <param name="first">The tile with the smaller X (vertical seam) or Z (horizontal seam).</param>
        /// <param name="vertical">True for a seam running along Z (between tiles side by side in X).</param>
        /// <param name="along">World position along the seam (Z for vertical seams, X for horizontal ones).</param>
        private void CloseSeam(TerrainTile first, TerrainTile second, bool vertical, float along, float reach)
        {
            int last = first.HeightmapResolution - 1;
            float origin = vertical ? first.Position.z : first.Position.x;
            float cell = vertical ? first.HeightCellSize.y : first.HeightCellSize.x;
            int from = Mathf.Clamp(Mathf.FloorToInt((along - reach - origin) / cell), 0, last);
            int to = Mathf.Clamp(Mathf.CeilToInt((along + reach - origin) / cell), 0, last);
            int count = to - from + 1;
            if (count <= 0)
                return;

            RectInt firstEdge = vertical ? new RectInt(last, from, 1, count) : new RectInt(from, last, count, 1);
            RectInt secondEdge = vertical ? new RectInt(0, from, 1, count) : new RectInt(from, 0, count, 1);
            if (_seamFirst.Length < count)
            {
                _seamFirst = new float[count];
                _seamSecond = new float[count];
            }
            first.ReadHeightRegion(firstEdge, _seamFirst);
            second.ReadHeightRegion(secondEdge, _seamSecond);

            bool differs = false;
            for (int i = 0; i < count; i++)
            {
                if (_seamFirst[i] == _seamSecond[i])
                    continue;
                float average = (_seamFirst[i] + _seamSecond[i]) * 0.5f;
                _seamFirst[i] = average;
                _seamSecond[i] = average;
                differs = true;
            }
            if (!differs)
                return;

            first.SetHeightRegion(firstEdge, _seamFirst);
            second.SetHeightRegion(secondEdge, _seamSecond);
        }

        /// <summary>Rebuilds the grid, the tiles' surface areas and the cached bounds after tiles changed.</summary>
        private void UpdateLayout()
        {
            IsGrid = BuildGrid();
            _worldBounds = _tiles[0].WorldBounds;
            for (int i = 1; i < _tiles.Count; i++)
                _worldBounds.Encapsulate(_tiles[i].WorldBounds);
            SetSurfaceAreas();
            LayoutVersion++;
            TerrainTile.NotifyHeightsChanged();
        }

        /// <summary>
        /// Gives each tile a grid position when all tiles are the same size and resolution and line up on a grid.
        /// </summary>
        /// <returns>False when they don't, so seams can't be matched.</returns>
        private bool BuildGrid()
        {
            _grid.Clear();
            _gridSize = Vector2Int.one;
            TerrainTile first = _tiles[0];
            Vector3 size = first.Size;
            Vector3 min = first.Position;
            foreach (TerrainTile tile in _tiles)
                min = Vector3.Min(min, tile.Position);
            _gridOrigin = min;

            foreach (TerrainTile tile in _tiles)
            {
                Vector3 position = tile.Position;
                float gridX = (position.x - min.x) / size.x;
                float gridZ = (position.z - min.z) / size.z;
                Vector2Int cell = new Vector2Int(Mathf.RoundToInt(gridX), Mathf.RoundToInt(gridZ));
                bool sameShape = Mathf.Approximately(tile.Size.x, size.x) && Mathf.Approximately(tile.Size.z, size.z)
                                 && tile.HeightmapResolution == first.HeightmapResolution;
                bool onGrid = Mathf.Abs(gridX - cell.x) < 0.001f && Mathf.Abs(gridZ - cell.y) < 0.001f
                              && Mathf.Abs(position.y - first.Position.y) < 0.001f;
                if (!sameShape || !onGrid || _grid.ContainsKey(cell))
                {
                    _grid.Clear();
                    _gridSize = Vector2Int.one;
                    return false;
                }
                _grid[cell] = tile;
                tile.GridPosition = cell;
                _gridSize = Vector2Int.Max(_gridSize, cell + Vector2Int.one);
            }
            return true;
        }

        /// <summary>Tells every tile which part of the whole surface it covers.</summary>
        private void SetSurfaceAreas()
        {
            Bounds bounds = _worldBounds;
            Vector3 size = bounds.size;
            foreach (TerrainTile tile in _tiles)
            {
                Vector3 position = tile.Position;
                tile.SurfaceArea = new Rect((position.x - bounds.min.x) / size.x, (position.z - bounds.min.z) / size.z,
                                            tile.Size.x / size.x, tile.Size.z / size.z);
            }
        }

        /// <summary>
        /// True when a tile can join the grid: the same size and heightmap resolution as the others, lined up with them
        /// and on a slot that is still free.
        /// </summary>
        private bool FitsGrid(TerrainTile tile)
        {
            TerrainTile first = _tiles[0];
            Vector3 size = first.Size;
            Vector3 position = tile.Position;
            bool sameShape = Mathf.Approximately(tile.Size.x, size.x) && Mathf.Approximately(tile.Size.z, size.z)
                             && tile.HeightmapResolution == first.HeightmapResolution;
            bool onGrid = IsSameSlot(GetSlotPosition(position + size * 0.5f), position)
                          && Mathf.Abs(position.y - first.Position.y) < 0.001f;
            return sameShape && onGrid && GetTileAtSlot(position) == null;
        }

        /// <summary>The grid cell a world position lies in (it may hold no tile, or be outside the grid).</summary>
        private Vector2Int GetGridCell(Vector3 worldPosition)
        {
            Vector3 size = TileSize;
            return new Vector2Int(Mathf.FloorToInt((worldPosition.x - _gridOrigin.x) / size.x),
                                  Mathf.FloorToInt((worldPosition.z - _gridOrigin.z) / size.z));
        }

        /// <summary>The slot next to another one, one tile away along a grid step.</summary>
        private Vector3 GetSideSlot(Vector3 slotPosition, Vector2Int step)
        {
            Vector3 size = TileSize;
            return slotPosition + new Vector3(step.x * size.x, 0f, step.y * size.z);
        }

        /// <summary>True when two slot positions are the same spot of the grid (ignoring height).</summary>
        private bool IsSameSlot(Vector3 first, Vector3 second)
        {
            Vector3 size = TileSize;
            return Mathf.Abs(first.x - second.x) <= size.x * SlotTolerance && Mathf.Abs(first.z - second.z) <= size.z * SlotTolerance;
        }

        /// <summary>True when a list already holds a slot position.</summary>
        private bool ContainsSlot(List<Vector3> slots, Vector3 slot)
        {
            foreach (Vector3 other in slots)
            {
                if (IsSameSlot(other, slot))
                    return true;
            }
            return false;
        }

        /// <summary>The tile under a position, or the closest one when there is none.</summary>
        private TerrainTile GetNearestTile(Vector3 worldPosition)
        {
            if (IsGrid)
            {
                // The grid cell nearest to the position is the one its clamped cell index points at
                Vector2Int cell = Vector2Int.Min(Vector2Int.Max(GetGridCell(worldPosition), Vector2Int.zero), _gridSize - Vector2Int.one);
                if (_grid.TryGetValue(cell, out TerrainTile tile))
                    return tile;
            }

            TerrainTile nearest = _tiles[0];
            float nearestDistance = float.MaxValue;
            foreach (TerrainTile tile in _tiles)
            {
                float distance = tile.DistanceTo(worldPosition);
                if (distance < nearestDistance)
                {
                    nearest = tile;
                    nearestDistance = distance;
                }
            }
            return nearest;
        }

        /// <summary>True when a point is above the ground, or not over any tile.</summary>
        private bool IsAboveGround(Vector3 point)
        {
            return !TrySampleHeight(point, out float height) || point.y > height;
        }

        /// <summary>Narrows down where the ray crosses the ground between a distance above it and one below it.</summary>
        private float RefineCrossing(Ray ray, float above, float below)
        {
            for (int i = 0; i < RaycastRefineSteps; i++)
            {
                float middle = (above + below) * 0.5f;
                if (IsAboveGround(ray.GetPoint(middle)))
                    above = middle;
                else
                    below = middle;
            }
            return below;
        }

        /// <summary>Distances along a ray where it enters and leaves a box, or false when it misses it.</summary>
        private static bool IntersectBounds(Bounds bounds, Ray ray, out float enter, out float exit)
        {
            enter = float.NegativeInfinity;
            exit = float.PositiveInfinity;
            for (int axis = 0; axis < 3; axis++)
            {
                float origin = ray.origin[axis];
                float direction = ray.direction[axis];
                float min = bounds.min[axis];
                float max = bounds.max[axis];
                if (Mathf.Abs(direction) < 1e-8f)
                {
                    if (origin < min || origin > max)
                        return false;
                    continue;
                }
                float near = (min - origin) / direction;
                float far = (max - origin) / direction;
                if (near > far)
                    (near, far) = (far, near);
                enter = Mathf.Max(enter, near);
                exit = Mathf.Min(exit, far);
            }
            return enter <= exit && exit >= 0f;
        }

        /// <summary>Orders tiles in rows along Z, then along X.</summary>
        private static int CompareByPosition(TerrainTile a, TerrainTile b)
        {
            Vector3 positionA = a.Position;
            Vector3 positionB = b.Position;
            int byZ = positionA.z.CompareTo(positionB.z);
            return byZ != 0 ? byZ : positionA.x.CompareTo(positionB.x);
        }
    }
}
