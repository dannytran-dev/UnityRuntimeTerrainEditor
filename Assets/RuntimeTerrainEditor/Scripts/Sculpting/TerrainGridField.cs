using System;
using UnityEngine;

namespace RuntimeTerrainEditor.Sculpting
{
    /// <summary>The kinds of terrain data a tool can edit.</summary>
    public enum TerrainChannel
    {
        /// <summary>The heightmap.</summary>
        Heights,
        /// <summary>The painted terrain layer weights (alphamaps).</summary>
        Splat,
        /// <summary>The grass and other detail densities.</summary>
        Details,
        /// <summary>The tree instances.</summary>
        Trees,
        /// <summary>The holes (cells where the terrain surface is cut away).</summary>
        Holes,
    }

    /// <summary>
    /// A square grid of values copied from a TerrainData, stored row by row along terrain Z,
    /// with <see cref="Channels"/> values per cell.
    /// </summary>
    public abstract class TerrainGridField
    {
        /// <summary>All values, row by row: [(z * Resolution + x) * Channels + channel].</summary>
        internal readonly float[] Values;

        // -0.5 for texel-centered grids, so a normalized coordinate maps onto the texel centers
        private readonly float _cellOffset;

        /// <summary>Cells per side.</summary>
        public int Resolution { get; }

        /// <summary>Values stored per cell (1 for heights, the layer count for splat weights).</summary>
        public int Channels { get; }

        /// <summary>How many cell steps span one full side of the terrain.</summary>
        public float CellsPerUnit { get; }

        /// <summary>Creates an empty grid.</summary>
        /// <param name="texelCentered">
        /// False for heightmaps, whose samples sit on the terrain corners (i / (n - 1)); true for alphamaps and detail
        /// maps, whose texels are centered ((i + 0.5) / n).
        /// </param>
        protected TerrainGridField(int resolution, int channels, bool texelCentered)
        {
            Resolution = resolution;
            Channels = channels;
            Values = new float[resolution * resolution * channels];
            CellsPerUnit = texelCentered ? resolution : resolution - 1;
            _cellOffset = texelCentered ? -0.5f : 0f;
        }

        /// <summary>Converts a normalized terrain coordinate [0,1] into a fractional cell coordinate.</summary>
        public float ToCell(float normalized)
        {
            return normalized * CellsPerUnit + _cellOffset;
        }

        /// <summary>All values of row z, <see cref="Channels"/> values per cell.</summary>
        public Span<float> Row(int z)
        {
            int stride = Resolution * Channels;
            return new Span<float>(Values, z * stride, stride);
        }

        /// <summary>Number of values in a rectangle of cells.</summary>
        internal int RegionLength(RectInt rect)
        {
            return rect.width * rect.height * Channels;
        }

        /// <summary>Copies a rectangle of cells from a buffer laid out like this field into a packed array.</summary>
        internal void ReadRegion(float[] source, RectInt rect, Array destination, int destinationOffset)
        {
            int rowLength = rect.width * Channels;
            for (int row = 0; row < rect.height; row++)
            {
                int sourceIndex = ((rect.yMin + row) * Resolution + rect.xMin) * Channels;
                Buffer.BlockCopy(source, sourceIndex * sizeof(float), destination, (destinationOffset + row * rowLength) * sizeof(float), rowLength * sizeof(float));
            }
        }

        /// <summary>Writes a packed rectangle of cells back into this field.</summary>
        internal void WriteRegion(RectInt rect, float[] source, int sourceOffset)
        {
            int rowLength = rect.width * Channels;
            for (int row = 0; row < rect.height; row++)
            {
                int destinationIndex = ((rect.yMin + row) * Resolution + rect.xMin) * Channels;
                Array.Copy(source, sourceOffset + row * rowLength, Values, destinationIndex, rowLength);
            }
        }

        /// <summary>Exchanges a rectangle of cells with packed values (laid out like <see cref="ReadRegion"/> writes them).</summary>
        internal void SwapRegion(RectInt rect, float[] other, int otherOffset)
        {
            int rowLength = rect.width * Channels;
            for (int row = 0; row < rect.height; row++)
            {
                int index = ((rect.yMin + row) * Resolution + rect.xMin) * Channels;
                int otherIndex = otherOffset + row * rowLength;
                for (int i = 0; i < rowLength; i++)
                {
                    float value = Values[index + i];
                    Values[index + i] = other[otherIndex + i];
                    other[otherIndex + i] = value;
                }
            }
        }
    }

    /// <summary>
    /// CPU copy of a terrain heightmap. Heights are normalized to [0,1] of the terrain height.
    /// </summary>
    public sealed class HeightField : TerrainGridField
    {
        /// <summary>Normalized height of the sample at column x, row z.</summary>
        public float this[int x, int z]
        {
            get => Values[z * Resolution + x];
            set => Values[z * Resolution + x] = value;
        }

        /// <summary>Creates an empty heightmap copy.</summary>
        internal HeightField(int resolution) : base(resolution, 1, false)
        {
        }

        /// <summary>Normalized height at a normalized terrain position (u along X, v along Z), bilinear between samples.</summary>
        public float Sample(float u, float v)
        {
            int last = Resolution - 1;
            float x = Mathf.Clamp01(u) * last;
            float z = Mathf.Clamp01(v) * last;
            int x0 = Mathf.Min((int)x, last - 1);
            int z0 = Mathf.Min((int)z, last - 1);
            float fx = x - x0;
            float fz = z - z0;

            int index = z0 * Resolution + x0;
            float bottom = Values[index] + (Values[index + 1] - Values[index]) * fx;
            index += Resolution;
            float top = Values[index] + (Values[index + 1] - Values[index]) * fx;
            return bottom + (top - bottom) * fz;
        }
    }

    /// <summary>
    /// CPU copy of the terrain splat (alphamap) weights, one weight per terrain layer per cell.
    /// </summary>
    public sealed class SplatField : TerrainGridField
    {
        /// <summary>Number of terrain layers (weights per cell).</summary>
        public int LayerCount => Channels;

        /// <summary>Creates an empty alphamap copy.</summary>
        internal SplatField(int resolution, int layerCount) : base(resolution, layerCount, true)
        {
        }

        /// <summary>The layer weights of one cell.</summary>
        public Span<float> Cell(int x, int z)
        {
            return new Span<float>(Values, (z * Resolution + x) * Channels, Channels);
        }
    }

    /// <summary>
    /// CPU copy of the terrain detail (grass) maps: one density value per detail type per cell.
    /// Values use the terrain's own units, 0 to <see cref="MaxDensity"/>.
    /// </summary>
    public sealed class DetailField : TerrainGridField
    {
        /// <summary>Number of detail types (values per cell).</summary>
        public int TypeCount => Channels;

        /// <summary>Highest value a cell can hold: 255 in coverage scatter mode, 16 in instance count mode.</summary>
        public float MaxDensity { get; }

        /// <summary>The detail types marked with <see cref="MarkChanged"/> during the current dab.</summary>
        internal readonly bool[] ChangedTypes;

        /// <summary>True when any type was marked during the current dab.</summary>
        internal bool AnyChanged { get; private set; }

        /// <summary>Creates an empty detail map copy.</summary>
        internal DetailField(int resolution, int typeCount, float maxDensity) : base(resolution, typeCount, true)
        {
            MaxDensity = maxDensity;
            ChangedTypes = new bool[typeCount];
        }

        /// <summary>
        /// Tells the terrain which detail type a tool changed, so only changed types are uploaded. A tool that marks no
        /// type is treated as having changed all of them.
        /// </summary>
        public void MarkChanged(int type)
        {
            ChangedTypes[type] = true;
            AnyChanged = true;
        }

        /// <summary>Forgets the marked types before the next dab.</summary>
        internal void ClearChanged()
        {
            Array.Clear(ChangedTypes, 0, ChangedTypes.Length);
            AnyChanged = false;
        }
    }

    /// <summary>
    /// CPU copy of the terrain holes: one value per heightmap quad, 1 where the surface is there and 0 where it is cut away.
    /// </summary>
    public sealed class HoleField : TerrainGridField
    {
        /// <summary>Value of a cell with surface.</summary>
        public const float Surface = 1f;

        /// <summary>Value of a cell that is a hole.</summary>
        public const float Hole = 0f;

        /// <summary>Creates an empty hole map copy.</summary>
        internal HoleField(int resolution) : base(resolution, 1, true)
        {
        }

        /// <summary>True when the cell at column x, row z has surface (isn't a hole).</summary>
        public bool IsSurface(int x, int z)
        {
            return Values[z * Resolution + x] >= 0.5f;
        }
    }
}
