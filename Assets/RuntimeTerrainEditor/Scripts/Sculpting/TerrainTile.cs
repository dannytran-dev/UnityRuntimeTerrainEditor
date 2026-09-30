using System;
using System.Collections.Generic;
using UnityEngine;

namespace RuntimeTerrainEditor.Sculpting
{
    /// <summary>
    /// One Terrain of a <see cref="TerrainSurface"/>. It keeps CPU copies of the terrain's heightmap, splat weights,
    /// detail (grass) maps, trees and holes, runs <see cref="ITerrainTool"/>s on them and uploads only what changed.
    /// During a stroke the changes are collected and uploaded once per <see cref="FlushUploads"/>, not once per dab.
    /// Edit through <see cref="TerrainSurface"/>, which also keeps the seams between tiles closed.
    /// </summary>
    public sealed class TerrainTile
    {
        // Upload rectangles are grown to multiples of this many cells, so the upload arrays come in few sizes and get reused
        private const int UploadAlignment = 16;
        // Trees per tile after which uploads during a stroke are spaced out further (each upload rebuilds the whole list)
        private const float TreesPerSyncInterval = 10000f;
        private const int ChannelCount = 5;

        // Upload arrays shared by all tiles, by size
        private static readonly UploadBuffers<float[,]> HeightUploads = new UploadBuffers<float[,]>((width, height, _) => new float[height, width]);
        private static readonly UploadBuffers<float[,,]> SplatUploads = new UploadBuffers<float[,,]>((width, height, layers) => new float[height, width, layers]);
        private static readonly UploadBuffers<int[,]> DetailUploads = new UploadBuffers<int[,]>((width, height, _) => new int[height, width]);
        private static readonly UploadBuffers<bool[,]> HoleUploads = new UploadBuffers<bool[,]>((width, height, _) => new bool[height, width]);

        // Goes up whenever any tile's heights change, so height-following visuals know when to rebuild
        private static int _heightsVersion;

        private readonly Terrain _terrain;
        private readonly TerrainData _data;
        private HeightField _heights;
        private SplatField _splat;
        private DetailField _details;
        private HoleField _holes;
        private readonly TreeField _trees = new TreeField();
        // Cached terrain position and size, refreshed by Reload and at the start of every edit
        private Vector3 _position;
        private Vector3 _size;

        // Values from before the running stroke, copied one 32x32 block at a time the first time the stroke changes it
        private readonly StrokeBackup _heightBackup = new StrokeBackup();
        private readonly StrokeBackup _splatBackup = new StrokeBackup();
        private readonly StrokeBackup _detailBackup = new StrokeBackup();
        private readonly StrokeBackup _holeBackup = new StrokeBackup();
        private bool _strokeActive;
        private Rect _strokeBounds; // normalized terrain coordinates
        private bool _strokeHasBounds;

        // Changed cells not uploaded yet, per TerrainChannel, and the changed grass types
        private readonly RectInt[] _dirtyRects = new RectInt[ChannelCount];
        private readonly bool[] _dirty = new bool[ChannelCount];
        private bool[] _dirtyDetailTypes = Array.Empty<bool>();

        private bool _heightSyncPending;
        private float _lastHeightSyncTime;
        private float _lastTreeUploadTime;

        // Reused so dabs don't allocate
        private float[] _footprintBuffer;

        /// <summary>The terrain this tile edits.</summary>
        public Terrain Terrain => _terrain;

        /// <summary>The TerrainData this tile edits.</summary>
        public TerrainData Data => _data;

        /// <summary>Tile size in meters.</summary>
        public Vector3 Size => _size;

        /// <summary>World position of the tile's corner with the smallest X and Z.</summary>
        public Vector3 Position => _position;

        /// <summary>Heightmap samples per side.</summary>
        public int HeightmapResolution => _heights.Resolution;

        /// <summary>Number of terrain layers.</summary>
        public int LayerCount => _data.alphamapLayers;

        /// <summary>Number of grass (detail) types that can be painted (0 without a detail resolution).</summary>
        public int DetailTypeCount => _details != null ? _details.TypeCount : 0;

        /// <summary>Number of tree types.</summary>
        public int TreeTypeCount => _data.treePrototypes.Length;

        /// <summary>Number of trees on the tile.</summary>
        public int TreeCount => _trees.Count;

        /// <summary>True between <see cref="BeginStroke"/> and <see cref="EndStroke"/>.</summary>
        public bool IsStrokeActive => _strokeActive;

        /// <summary>World-space box around the whole tile.</summary>
        public Bounds WorldBounds => ToWorldBounds(new Rect(0f, 0f, 1f, 1f));

        /// <summary>Distance between heightmap samples in meters (x along terrain X, y along terrain Z).</summary>
        public Vector2 HeightCellSize
        {
            get
            {
                float steps = _heights.Resolution - 1;
                return new Vector2(_size.x / steps, _size.z / steps);
            }
        }

        /// <summary>Approximate memory of the CPU copies (the TerrainData holds about as much again).</summary>
        public long SizeInBytes
        {
            get
            {
                long values = _heights.Values.Length + _holes.Values.Length;
                if (_splat != null)
                    values += _splat.Values.Length;
                if (_details != null)
                    values += _details.Values.Length;
                return values * sizeof(float) + _trees.Count * TerrainStrokeRecord.TreeSizeInBytes;
            }
        }

        /// <summary>Position of the tile in the surface's grid (0, 0 for the tile with the smallest X and Z).</summary>
        public Vector2Int GridPosition { get; internal set; }

        /// <summary>The tile's part of the surface, in normalized surface coordinates (the whole surface is 0-1).</summary>
        public Rect SurfaceArea { get; internal set; } = new Rect(0f, 0f, 1f, 1f);

        /// <summary>
        /// Seconds between terrain collider and LOD refreshes while a stroke is running; 0 refreshes every flush, infinity
        /// only when the stroke ends. Heights render immediately either way, the collider lags by up to this long.
        /// </summary>
        public float HeightSyncInterval { get; set; } = 0.1f;

        /// <summary>
        /// Seconds between tree uploads while a stroke is running (rebuilding the tree list gets slow with many trees).
        /// Tiles with more than 10000 trees wait proportionally longer.
        /// </summary>
        public float TreeSyncInterval { get; set; } = 0.05f;

        /// <summary>Goes up whenever the heights of any tile change.</summary>
        internal static int HeightsVersion => _heightsVersion;

        /// <summary>Takes CPU copies of everything the terrain has.</summary>
        internal TerrainTile(Terrain terrain)
        {
            _terrain = terrain ? terrain : throw new ArgumentNullException(nameof(terrain));
            _data = terrain.terrainData;
            Reload();
        }

        /// <summary>Re-reads everything from the TerrainData, and the terrain's position and size. Cancels a running stroke.</summary>
        public void Reload()
        {
            CancelStroke();
            RefreshTransform();
            LoadHeights();
            LoadSplat();
            LoadDetails();
            LoadHoles();
            LoadTrees();
            Array.Clear(_dirty, 0, _dirty.Length);
        }

        /// <summary>
        /// Sets every height to a normalized value, fills one layer and closes all holes. Cancels a running stroke.
        /// </summary>
        public void Fill(float normalizedHeight, int layer = 0)
        {
            CancelStroke();
            EnsureInSync();

            Array.Fill(_heights.Values, Mathf.Clamp01(normalizedHeight));
            MarkDirty(TerrainChannel.Heights, FullRect(_heights));

            if (_splat != null)
            {
                int layers = _splat.LayerCount;
                layer = Mathf.Clamp(layer, 0, layers - 1);
                float[] weights = _splat.Values;
                Array.Clear(weights, 0, weights.Length);
                for (int i = layer; i < weights.Length; i += layers)
                {
                    weights[i] = 1f;
                }
                MarkDirty(TerrainChannel.Splat, FullRect(_splat));
            }

            Array.Fill(_holes.Values, HoleField.Surface);
            MarkDirty(TerrainChannel.Holes, FullRect(_holes));
            SnapTreesToGround(FullRect(_heights));
            FlushUploads(true);
        }

        /// <summary>Removes all grass and trees. Cancels a running stroke.</summary>
        public void ClearVegetation()
        {
            CancelStroke();
            EnsureInSync();

            if (_details != null)
            {
                Array.Clear(_details.Values, 0, _details.Values.Length);
                MarkAllDetailTypesDirty();
                MarkDirty(TerrainChannel.Details, FullRect(_details));
            }
            _trees.Set(Array.Empty<TreeInstance>());
            FlushUploads(true);
        }

        /// <summary>Starts collecting changes into one undo step.</summary>
        public void BeginStroke()
        {
            EnsureInSync();
            CancelStroke();
            _strokeActive = true;
            _trees.BeginTracking();
        }

        /// <summary>
        /// Applies one brush dab to this tile. Outside a stroke the change is uploaded immediately and isn't recorded for
        /// undo; during a stroke it is uploaded by the next <see cref="FlushUploads"/>.
        /// </summary>
        /// <returns>False when nothing was touched (brush off the tile, nothing to paint with, ...).</returns>
        public bool Apply(ITerrainTool tool, in BrushDab dab)
        {
            if (tool == null || dab.Shape == null)
                return false;
            if (!_strokeActive)
                EnsureInSync();

            TerrainChannel channel = tool.Channel;
            TerrainGridField field = channel == TerrainChannel.Trees ? _heights : GetField(channel);
            if (field == null || (channel == TerrainChannel.Trees && _trees.TypeCount == 0))
                return false;

            // Most tiles of a big grid are nowhere near the dab; skip them before rasterizing
            Vector3 local = dab.Center - _position;
            float reach = dab.Diameter * 0.5f * 1.42f + Mathf.Max(_size.x, _size.z) / field.CellsPerUnit; // rotated square corners reach a bit further
            if (local.x + reach < 0f || local.z + reach < 0f || local.x - reach > _size.x || local.z - reach > _size.z)
                return false;

            Vector2 center = new Vector2(local.x / _size.x, local.z / _size.z);
            BrushArea area = new BrushArea(center, dab.Diameter * 0.5f, dab.Rotation, dab.Shape, _size);
            BrushFootprint footprint = BrushFootprint.Rasterize(field, area, ref _footprintBuffer);
            if (footprint.IsEmpty)
                return false;

            if (_strokeActive)
            {
                if (channel != TerrainChannel.Trees)
                    GetBackup(channel).Capture(field, footprint.Cells);
                IncludeInStroke(area);
            }

            if (channel == TerrainChannel.Details)
                _details.ClearChanged();

            Vector2 cellSize = new Vector2(_size.x / field.CellsPerUnit, _size.z / field.CellsPerUnit);
            tool.Apply(new TerrainToolContext(footprint, area, _heights, _splat, _details, _trees, _holes, dab.Strength, cellSize, _size, SurfaceArea));

            if (channel == TerrainChannel.Details)
                CollectChangedDetailTypes();
            if (channel != TerrainChannel.Trees)
                MarkDirty(channel, footprint.Cells);
            if (!_strokeActive)
                FlushUploads(true);
            return true;
        }

        /// <summary>
        /// Uploads the changes collected since the last flush to the TerrainData. The collider and trees are refreshed when
        /// <paramref name="syncNow"/> is set or their sync interval has passed.
        /// </summary>
        internal void FlushUploads(bool syncNow)
        {
            if (TakeDirty(TerrainChannel.Heights, _heights.Resolution, out RectInt heightRect))
            {
                float[,] upload = HeightUploads.Get(heightRect.width, heightRect.height, 1);
                _heights.ReadRegion(_heights.Values, heightRect, upload, 0);
                // Delaying the LOD / collider rebuild is what keeps sculpting cheap; SyncHeights catches up
                _data.SetHeightsDelayLOD(heightRect.xMin, heightRect.yMin, upload);
                _heightSyncPending = true;
            }
            if (_heightSyncPending && (syncNow || Time.unscaledTime - _lastHeightSyncTime >= HeightSyncInterval))
                SyncHeights();

            if (_splat != null && TakeDirty(TerrainChannel.Splat, _splat.Resolution, out RectInt splatRect))
            {
                float[,,] upload = SplatUploads.Get(splatRect.width, splatRect.height, _splat.LayerCount);
                _splat.ReadRegion(_splat.Values, splatRect, upload, 0);
                _data.SetAlphamaps(splatRect.xMin, splatRect.yMin, upload);
            }

            if (_details != null && TakeDirty(TerrainChannel.Details, _details.Resolution, out RectInt detailRect))
                UploadDetails(detailRect);

            if (TakeDirty(TerrainChannel.Holes, _holes.Resolution, out RectInt holeRect))
                UploadHoles(holeRect);

            float treeInterval = TreeSyncInterval * Mathf.Max(1f, _trees.Count / TreesPerSyncInterval);
            if (_trees.Changed && (syncNow || Time.unscaledTime - _lastTreeUploadTime >= treeInterval))
                UploadTrees();
        }

        /// <summary>Finishes the stroke and returns what it changed on this tile, or null when it changed nothing.</summary>
        internal TerrainStrokeRecord.TileChanges EndStroke()
        {
            if (!_strokeActive)
                return null;
            _strokeActive = false;

            bool heightsChanged = _heightBackup.HasChanges;
            RectInt heightBounds = _heightBackup.Bounds;
            List<TerrainStrokeRecord.Delta> deltas = new List<TerrainStrokeRecord.Delta>(4);
            AddDelta(deltas, TerrainChannel.Heights, _heights, _heightBackup);
            AddDelta(deltas, TerrainChannel.Splat, _splat, _splatBackup);
            AddDelta(deltas, TerrainChannel.Details, _details, _detailBackup);
            AddDelta(deltas, TerrainChannel.Holes, _holes, _holeBackup);

            TerrainStrokeRecord.TreeDelta treeDelta = null;
            if (_trees.EndTracking(out TreeInstance[] added, out TreeInstance[] removed))
                treeDelta = new TerrainStrokeRecord.TreeDelta(added, removed);

            if (heightsChanged)
                SnapTreesToGround(heightBounds);
            FlushUploads(true);

            if (deltas.Count == 0 && treeDelta == null)
                return null;
            Bounds bounds = _strokeHasBounds ? ToWorldBounds(_strokeBounds) : WorldBounds;
            return new TerrainStrokeRecord.TileChanges(this, deltas.ToArray(), treeDelta, bounds);
        }

        /// <summary>Copies the heights of a rectangle of heightmap samples, packed row by row.</summary>
        internal float[] GetHeightRegion(RectInt rect)
        {
            float[] values = new float[rect.width * rect.height];
            ReadHeightRegion(rect, values);
            return values;
        }

        /// <summary>Copies the heights of a rectangle of heightmap samples into a buffer, packed row by row.</summary>
        internal void ReadHeightRegion(RectInt rect, float[] destination)
        {
            _heights.ReadRegion(_heights.Values, rect, destination, 0);
        }

        /// <summary>
        /// Writes the heights of a rectangle of heightmap samples (packed row by row), recorded for undo during a stroke.
        /// Used to keep the seams between tiles closed.
        /// </summary>
        internal void SetHeightRegion(RectInt rect, float[] values)
        {
            if (_strokeActive)
                _heightBackup.Capture(_heights, rect);
            _heights.WriteRegion(rect, values, 0);
            MarkDirty(TerrainChannel.Heights, rect);
            if (!_strokeActive)
                FlushUploads(true);
        }

        /// <summary>World height of the ground under a world position (clamped to the tile's edge), from the CPU copy.</summary>
        public float SampleHeight(Vector3 worldPosition)
        {
            float u = (worldPosition.x - _position.x) / _size.x;
            float v = (worldPosition.z - _position.z) / _size.z;
            return _position.y + _heights.Sample(u, v) * _size.y;
        }

        /// <summary>Swaps recorded grid values with the terrain's (undo / redo).</summary>
        /// <param name="undo">True for undo (the values from before the stroke come back), false for redo.</param>
        internal void Restore(TerrainStrokeRecord.Delta delta, bool undo)
        {
            if (_strokeActive || delta.IsUndone == undo)
                return;
            EnsureInSync();

            TerrainGridField field = GetField(delta.Channel);
            if (field == null || field.Resolution != delta.Resolution || field.Channels != delta.Channels)
            {
                Debug.LogWarning("TerrainTile: the terrain's resolution or layer count changed since this edit, it can't be restored.");
                return;
            }

            int offset = 0;
            foreach (RectInt block in delta.Blocks)
            {
                field.SwapRegion(block, delta.Values, offset);
                offset += field.RegionLength(block);
            }
            delta.IsUndone = undo;

            if (delta.Channel == TerrainChannel.Details)
                MarkAllDetailTypesDirty();
            MarkDirty(delta.Channel, delta.Bounds);
            if (delta.Channel == TerrainChannel.Heights)
                SnapTreesToGround(delta.Bounds);
            FlushUploads(true);
        }

        /// <summary>Removes and adds back the trees a stroke added and removed (undo / redo).</summary>
        /// <param name="undo">True for undo (the trees from before the stroke come back), false for redo.</param>
        internal void Restore(TerrainStrokeRecord.TreeDelta delta, bool undo)
        {
            if (_strokeActive || delta.IsUndone == undo)
                return;
            EnsureInSync();

            _trees.RemoveMatching(undo ? delta.Added : delta.Removed);
            foreach (TreeInstance tree in undo ? delta.Removed : delta.Added)
            {
                // The ground may have changed since the tree was recorded
                TreeInstance placed = tree;
                placed.position.y = _heights.Sample(tree.position.x, tree.position.z);
                _trees.Add(placed);
            }
            delta.IsUndone = undo;
            FlushUploads(true);
        }

        /// <summary>World-space box for a rectangle in normalized terrain coordinates, spanning the terrain's full height.</summary>
        public Bounds ToWorldBounds(Rect normalized)
        {
            Vector3 min = _position + new Vector3(normalized.xMin * _size.x, 0f, normalized.yMin * _size.z);
            Vector3 max = _position + new Vector3(normalized.xMax * _size.x, _size.y, normalized.yMax * _size.z);
            Bounds bounds = new Bounds();
            bounds.SetMinMax(min, max);
            return bounds;
        }

        /// <summary>Distance on the ground plane from a world position to the tile (0 when the position is over it).</summary>
        public float DistanceTo(Vector3 worldPosition)
        {
            float dx = Mathf.Max(_position.x - worldPosition.x, 0f, worldPosition.x - (_position.x + _size.x));
            float dz = Mathf.Max(_position.z - worldPosition.z, 0f, worldPosition.z - (_position.z + _size.z));
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Tells height-following visuals that heights changed without going through a tile (tiles added or removed).</summary>
        internal static void NotifyHeightsChanged()
        {
            _heightsVersion++;
        }

        /// <summary>Ends a running stroke without recording it (its changes stay).</summary>
        private void CancelStroke()
        {
            _strokeActive = false;
            _strokeHasBounds = false;
            _heightBackup.Reset();
            _splatBackup.Reset();
            _detailBackup.Reset();
            _holeBackup.Reset();
            _trees.CancelTracking();
        }

        /// <summary>Reads the terrain's position and size again.</summary>
        private void RefreshTransform()
        {
            _position = _terrain.GetPosition();
            _size = _data.size;
        }

        /// <summary>Grows the running stroke's bounds (normalized terrain coordinates) to include a dab.</summary>
        private void IncludeInStroke(in BrushArea area)
        {
            float extent = area.Extent;
            Rect rect = Rect.MinMaxRect(
                Mathf.Clamp01(area.Center.x - extent / area.TerrainSize.x), Mathf.Clamp01(area.Center.y - extent / area.TerrainSize.z),
                Mathf.Clamp01(area.Center.x + extent / area.TerrainSize.x), Mathf.Clamp01(area.Center.y + extent / area.TerrainSize.z));
            _strokeBounds = _strokeHasBounds
                ? Rect.MinMaxRect(Mathf.Min(_strokeBounds.xMin, rect.xMin), Mathf.Min(_strokeBounds.yMin, rect.yMin),
                                  Mathf.Max(_strokeBounds.xMax, rect.xMax), Mathf.Max(_strokeBounds.yMax, rect.yMax))
                : rect;
            _strokeHasBounds = true;
        }

        /// <summary>Packs the values from before the stroke of every block it changed into an undo delta, and frees the backup.</summary>
        private static void AddDelta(List<TerrainStrokeRecord.Delta> deltas, TerrainChannel channel, TerrainGridField field, StrokeBackup backup)
        {
            if (field != null && backup.HasChanges && backup.Matches(field))
            {
                RectInt[] blocks = backup.GetSavedBlocks();
                int length = 0;
                foreach (RectInt block in blocks)
                    length += field.RegionLength(block);

                float[] before = new float[length];
                int offset = 0;
                for (int i = 0; i < blocks.Length; i++)
                {
                    int blockLength = field.RegionLength(blocks[i]);
                    backup.CopySavedBlock(i, before, offset, blockLength);
                    offset += blockLength;
                }
                deltas.Add(new TerrainStrokeRecord.Delta(channel, field.Resolution, field.Channels, blocks, backup.Bounds, before));
            }
            backup.Reset();
        }

        /// <summary>The CPU copy of a grid channel (null for trees, or when the terrain has no such data).</summary>
        private TerrainGridField GetField(TerrainChannel channel)
        {
            switch (channel)
            {
                case TerrainChannel.Heights: return _heights;
                case TerrainChannel.Splat: return _splat;
                case TerrainChannel.Details: return _details;
                case TerrainChannel.Holes: return _holes;
                default: return null;
            }
        }

        /// <summary>The stroke backup of a grid channel.</summary>
        private StrokeBackup GetBackup(TerrainChannel channel)
        {
            switch (channel)
            {
                case TerrainChannel.Heights: return _heightBackup;
                case TerrainChannel.Splat: return _splatBackup;
                case TerrainChannel.Holes: return _holeBackup;
                default: return _detailBackup;
            }
        }

        /// <summary>
        /// Picks up resolution, layer or prototype changes made to the TerrainData since the copies were taken, and the
        /// terrain's current position and size.
        /// </summary>
        private void EnsureInSync()
        {
            RefreshTransform();
            if (_heights.Resolution != _data.heightmapResolution)
                LoadHeights();

            int layers = _data.alphamapLayers;
            bool splatStale = _splat == null ? layers > 0 : _splat.LayerCount != layers || _splat.Resolution != _data.alphamapResolution;
            if (splatStale)
                LoadSplat();

            int detailTypes = _data.detailResolution > 0 ? _data.detailPrototypes.Length : 0;
            bool detailsStale = _details == null ? detailTypes > 0 : _details.TypeCount != detailTypes || _details.Resolution != _data.detailResolution;
            if (detailsStale)
                LoadDetails();

            if (_holes.Resolution != _data.holesResolution)
                LoadHoles();

            if (_trees.TypeCount != _data.treePrototypes.Length)
                LoadTrees();
        }

        /// <summary>Copies the heightmap from the TerrainData.</summary>
        private void LoadHeights()
        {
            int resolution = _data.heightmapResolution;
            _heights = new HeightField(resolution);
            float[,] heights = _data.GetHeights(0, 0, resolution, resolution);
            Buffer.BlockCopy(heights, 0, _heights.Values, 0, _heights.Values.Length * sizeof(float));
            _heightBackup.Reset();
            _dirty[(int)TerrainChannel.Heights] = false;
            _heightsVersion++;
        }

        /// <summary>Copies the layer weights from the TerrainData (none when it has no layers).</summary>
        private void LoadSplat()
        {
            _splatBackup.Reset();
            _dirty[(int)TerrainChannel.Splat] = false;
            int layers = _data.alphamapLayers;
            if (layers == 0)
            {
                _splat = null;
                return;
            }

            int resolution = _data.alphamapResolution;
            _splat = new SplatField(resolution, layers);
            float[,,] weights = _data.GetAlphamaps(0, 0, resolution, resolution);
            Buffer.BlockCopy(weights, 0, _splat.Values, 0, _splat.Values.Length * sizeof(float));
        }

        /// <summary>Copies the detail maps from the TerrainData (none without detail types or resolution).</summary>
        private void LoadDetails()
        {
            _detailBackup.Reset();
            _dirty[(int)TerrainChannel.Details] = false;
            int resolution = _data.detailResolution;
            int types = _data.detailPrototypes.Length;
            if (resolution == 0 || types == 0)
            {
                _details = null;
                _dirtyDetailTypes = Array.Empty<bool>();
                return;
            }

            float maxDensity = _data.detailScatterMode == DetailScatterMode.CoverageMode ? 255f : 16f;
            _details = new DetailField(resolution, types, maxDensity);
            _dirtyDetailTypes = new bool[types];
            float[] values = _details.Values;
            for (int type = 0; type < types; type++)
            {
                int[,] layer = _data.GetDetailLayer(0, 0, resolution, resolution, type);
                for (int z = 0; z < resolution; z++)
                {
                    int row = z * resolution;
                    for (int x = 0; x < resolution; x++)
                        values[(row + x) * types + type] = layer[z, x];
                }
            }
        }

        /// <summary>Copies the holes from the TerrainData.</summary>
        private void LoadHoles()
        {
            _holeBackup.Reset();
            _dirty[(int)TerrainChannel.Holes] = false;
            int resolution = _data.holesResolution;
            _holes = new HoleField(resolution);
            bool[,] surface = _data.GetHoles(0, 0, resolution, resolution);
            float[] values = _holes.Values;
            for (int z = 0; z < resolution; z++)
            {
                int row = z * resolution;
                for (int x = 0; x < resolution; x++)
                    values[row + x] = surface[z, x] ? HoleField.Surface : HoleField.Hole;
            }
        }

        /// <summary>Copies the trees from the TerrainData.</summary>
        private void LoadTrees()
        {
            _trees.CancelTracking();
            _trees.TypeCount = _data.treePrototypes.Length;
            _trees.Set(_data.treeInstances);
            _trees.Changed = false;
        }

        /// <summary>Remembers that cells of a channel changed and have to be uploaded.</summary>
        private void MarkDirty(TerrainChannel channel, RectInt rect)
        {
            int index = (int)channel;
            _dirtyRects[index] = _dirty[index] ? Union(_dirtyRects[index], rect) : rect;
            _dirty[index] = true;
            if (channel == TerrainChannel.Heights)
                _heightsVersion++;
        }

        /// <summary>
        /// Takes a channel's changed cells for uploading, grown to <see cref="UploadAlignment"/> so the upload arrays
        /// come in few sizes.
        /// </summary>
        /// <returns>False when nothing changed.</returns>
        private bool TakeDirty(TerrainChannel channel, int resolution, out RectInt rect)
        {
            int index = (int)channel;
            rect = _dirtyRects[index];
            if (!_dirty[index])
                return false;
            _dirty[index] = false;

            int xMin = rect.xMin / UploadAlignment * UploadAlignment;
            int zMin = rect.yMin / UploadAlignment * UploadAlignment;
            int xMax = Mathf.Min((rect.xMax + UploadAlignment - 1) / UploadAlignment * UploadAlignment, resolution);
            int zMax = Mathf.Min((rect.yMax + UploadAlignment - 1) / UploadAlignment * UploadAlignment, resolution);
            rect = new RectInt(xMin, zMin, xMax - xMin, zMax - zMin);
            return rect.width > 0 && rect.height > 0;
        }

        /// <summary>Adds the grass types the last dab's tool marked (all types when it marked none) to the ones to upload.</summary>
        private void CollectChangedDetailTypes()
        {
            if (!_details.AnyChanged)
            {
                MarkAllDetailTypesDirty();
                return;
            }
            bool[] changed = _details.ChangedTypes;
            for (int type = 0; type < _dirtyDetailTypes.Length; type++)
                _dirtyDetailTypes[type] |= changed[type];
        }

        /// <summary>Uploads every grass type with the next detail upload.</summary>
        private void MarkAllDetailTypesDirty()
        {
            Array.Fill(_dirtyDetailTypes, true);
        }

        /// <summary>Writes changed detail cells to the TerrainData, one changed detail type at a time.</summary>
        private void UploadDetails(RectInt rect)
        {
            int[,] upload = DetailUploads.Get(rect.width, rect.height, 1);
            int types = _details.TypeCount;
            float[] values = _details.Values;
            for (int type = 0; type < types; type++)
            {
                if (!_dirtyDetailTypes[type])
                    continue;
                _dirtyDetailTypes[type] = false;
                for (int row = 0; row < rect.height; row++)
                {
                    int start = ((rect.yMin + row) * _details.Resolution + rect.xMin) * types + type;
                    for (int column = 0; column < rect.width; column++)
                        upload[row, column] = Mathf.RoundToInt(values[start + column * types]);
                }
                _data.SetDetailLayer(rect.xMin, rect.yMin, type, upload);
            }
        }

        /// <summary>Writes changed hole cells to the TerrainData (which also updates the collider).</summary>
        private void UploadHoles(RectInt rect)
        {
            bool[,] upload = HoleUploads.Get(rect.width, rect.height, 1);
            for (int row = 0; row < rect.height; row++)
            {
                for (int column = 0; column < rect.width; column++)
                    upload[row, column] = _holes.IsSurface(rect.xMin + column, rect.yMin + row);
            }
            _data.SetHoles(rect.xMin, rect.yMin, upload);
        }

        /// <summary>
        /// Replaces the terrain's trees with the CPU list. The trees already stand on the ground (see
        /// <see cref="SnapTreesToGround"/>), so Unity doesn't have to snap them again.
        /// </summary>
        private void UploadTrees()
        {
            _data.SetTreeInstances(_trees.Instances.ToArray(), false);
            _trees.Changed = false;
            _lastTreeUploadTime = Time.unscaledTime;
        }

        /// <summary>Rebuilds the terrain's LODs and collider after delayed height uploads.</summary>
        private void SyncHeights()
        {
            _data.SyncHeightmap();
            _heightSyncPending = false;
            _lastHeightSyncTime = Time.unscaledTime;
        }

        /// <summary>
        /// Puts the trees standing on changed heightmap samples back on the ground; trees keep the height they were placed
        /// at otherwise. Trees elsewhere aren't touched, and nothing is uploaded when no tree moved.
        /// </summary>
        /// <param name="cells">The changed heightmap samples.</param>
        private void SnapTreesToGround(RectInt cells)
        {
            if (_trees.Count == 0)
                return;

            // A tree between two samples moves when either of them changed, so the area reaches one cell further
            float last = _heights.Resolution - 1;
            float xMin = (cells.xMin - 1) / last;
            float xMax = cells.xMax / last;
            float zMin = (cells.yMin - 1) / last;
            float zMax = cells.yMax / last;

            List<TreeInstance> trees = _trees.Instances;
            bool moved = false;
            for (int i = 0; i < trees.Count; i++)
            {
                TreeInstance tree = trees[i];
                Vector3 position = tree.position;
                if (position.x < xMin || position.x > xMax || position.z < zMin || position.z > zMax)
                    continue;
                float height = _heights.Sample(position.x, position.z);
                if (height == position.y)
                    continue;
                tree.position.y = height;
                trees[i] = tree;
                moved = true;
            }
            if (moved)
                _trees.Changed = true;
        }

        /// <summary>All cells of a grid.</summary>
        private static RectInt FullRect(TerrainGridField field)
        {
            return new RectInt(0, 0, field.Resolution, field.Resolution);
        }

        /// <summary>Smallest rectangle containing both.</summary>
        private static RectInt Union(RectInt a, RectInt b)
        {
            int xMin = Mathf.Min(a.xMin, b.xMin);
            int zMin = Mathf.Min(a.yMin, b.yMin);
            return new RectInt(xMin, zMin, Mathf.Max(a.xMax, b.xMax) - xMin, Mathf.Max(a.yMax, b.yMax) - zMin);
        }

        /// <summary>
        /// Copy-on-write backup for one stroke: a 32x32 block of cells is copied the first time the stroke is about to
        /// change it, so starting a stroke and recording undo cost scale with the edited area, not the terrain size.
        /// Only the saved blocks take memory, and their buffers are reused by later strokes.
        /// </summary>
        private sealed class StrokeBackup
        {
            private const int BlockSize = 32;
            // Free block buffers kept for the next stroke; more are left to the garbage collector
            private const int MaxPooledBlocks = 256;

            // Saved values of each block, packed row by row; null for blocks not saved this stroke
            private float[][] _blocks = Array.Empty<float[]>();
            private readonly List<int> _saved = new List<int>();
            private readonly Stack<float[]> _pool = new Stack<float[]>();
            private int _resolution;
            private int _channels;
            private int _blocksPerSide;

            /// <summary>True once the stroke captured any block.</summary>
            public bool HasChanges => _saved.Count > 0;

            /// <summary>Box around every captured rectangle, in cells.</summary>
            public RectInt Bounds { get; private set; }

            /// <summary>True when the backup was taken from a field of this resolution and layout.</summary>
            public bool Matches(TerrainGridField field)
            {
                return field.Resolution == _resolution && field.Channels == _channels;
            }

            /// <summary>Forgets the saved blocks, keeping their buffers for the next stroke.</summary>
            public void Reset()
            {
                foreach (int index in _saved)
                {
                    if (_pool.Count < MaxPooledBlocks)
                        _pool.Push(_blocks[index]);
                    _blocks[index] = null;
                }
                _saved.Clear();
            }

            /// <summary>Saves every block of a rectangle that wasn't saved yet this stroke.</summary>
            public void Capture(TerrainGridField field, RectInt rect)
            {
                if (!Matches(field))
                {
                    Reset();
                    _pool.Clear();
                    _resolution = field.Resolution;
                    _channels = field.Channels;
                    _blocksPerSide = (_resolution + BlockSize - 1) / BlockSize;
                    _blocks = new float[_blocksPerSide * _blocksPerSide][];
                }

                bool hadChanges = HasChanges;
                int blockXMax = (rect.xMax - 1) / BlockSize;
                int blockZMax = (rect.yMax - 1) / BlockSize;
                for (int blockZ = rect.yMin / BlockSize; blockZ <= blockZMax; blockZ++)
                {
                    for (int blockX = rect.xMin / BlockSize; blockX <= blockXMax; blockX++)
                    {
                        int index = blockZ * _blocksPerSide + blockX;
                        if (_blocks[index] != null)
                            continue;
                        float[] buffer = _pool.Count > 0 ? _pool.Pop() : new float[BlockSize * BlockSize * _channels];
                        field.ReadRegion(field.Values, GetBlockRect(index), buffer, 0);
                        _blocks[index] = buffer;
                        _saved.Add(index);
                    }
                }

                Bounds = hadChanges ? Union(Bounds, rect) : rect;
            }

            /// <summary>The rectangles of all saved blocks, in the order <see cref="CopySavedBlock"/> numbers them.</summary>
            public RectInt[] GetSavedBlocks()
            {
                RectInt[] blocks = new RectInt[_saved.Count];
                for (int i = 0; i < blocks.Length; i++)
                    blocks[i] = GetBlockRect(_saved[i]);
                return blocks;
            }

            /// <summary>Copies the packed values of the i-th saved block.</summary>
            public void CopySavedBlock(int i, float[] destination, int offset, int length)
            {
                Array.Copy(_blocks[_saved[i]], 0, destination, offset, length);
            }

            /// <summary>The cells of a block; blocks at the far edges can be smaller.</summary>
            private RectInt GetBlockRect(int index)
            {
                int x = index % _blocksPerSide * BlockSize;
                int z = index / _blocksPerSide * BlockSize;
                return new RectInt(x, z, Mathf.Min(BlockSize, _resolution - x), Mathf.Min(BlockSize, _resolution - z));
            }
        }

        /// <summary>
        /// Upload arrays by size, shared by all tiles (uploads copy the data, so one array serves every tile). Keeps a few
        /// sizes and starts over when more are needed.
        /// </summary>
        private sealed class UploadBuffers<T> where T : class
        {
            private const int MaxBuffers = 8;

            private readonly Dictionary<long, T> _buffers = new Dictionary<long, T>();
            private readonly Func<int, int, int, T> _create;

            public UploadBuffers(Func<int, int, int, T> create)
            {
                _create = create;
            }

            /// <summary>An array of the given size (width, height and values per cell).</summary>
            public T Get(int width, int height, int depth)
            {
                long key = ((long)width << 40) | ((long)height << 20) | (long)depth;
                if (_buffers.TryGetValue(key, out T buffer))
                    return buffer;
                if (_buffers.Count >= MaxBuffers)
                    _buffers.Clear();
                buffer = _create(width, height, depth);
                _buffers.Add(key, buffer);
                return buffer;
            }
        }
    }
}
