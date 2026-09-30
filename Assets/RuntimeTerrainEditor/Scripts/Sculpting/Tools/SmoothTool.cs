using System;
using UnityEngine;

namespace RuntimeTerrainEditor.Sculpting
{
    /// <summary>
    /// Blends heights towards the box average of their neighbourhood. Strength [0,1] is the blend amount at full brush weight.
    /// Averages come from a summed-area table, so the cost per cell stays the same for any radius.
    /// </summary>
    public sealed class SmoothTool : ITerrainTool
    {
        // Summed-area table of the area a dab reads, reused between dabs (see BuildTable)
        private double[] _table = Array.Empty<double>();

        /// <summary>Neighbourhood radius in meters.</summary>
        public float Radius { get; set; } = 2f;

        /// <summary>Edits the heightmap.</summary>
        public TerrainChannel Channel => TerrainChannel.Heights;

        /// <summary>Blends every height under the brush towards the average of its neighbourhood.</summary>
        public void Apply(in TerrainToolContext context)
        {
            HeightField field = context.Heights;
            RectInt cells = context.Footprint.Cells;
            int radius = Mathf.Max(1, Mathf.RoundToInt(Radius / Mathf.Max(context.CellSize.x, 1e-4f)));
            int last = field.Resolution - 1;

            // The averages read the brush area plus the radius around it
            int readXMin = Mathf.Max(cells.xMin - radius, 0);
            int readXMax = Mathf.Min(cells.xMax - 1 + radius, last);
            int readZMin = Mathf.Max(cells.yMin - radius, 0);
            int readZMax = Mathf.Min(cells.yMax - 1 + radius, last);
            int stride = readXMax - readXMin + 2;
            BuildTable(field, readXMin, readXMax, readZMin, readZMax, stride);

            // The table holds the heights from before this dab, so the result doesn't depend on the loop order
            float strength = Mathf.Clamp01(context.Strength);
            for (int z = cells.yMin; z < cells.yMax; z++)
            {
                Span<float> heights = field.Row(z);
                ReadOnlySpan<float> weights = context.Footprint.Row(z);
                int boxZMin = Mathf.Max(z - radius, readZMin) - readZMin;
                int boxZMax = Mathf.Min(z + radius, readZMax) - readZMin + 1;
                for (int i = 0, x = cells.xMin; i < weights.Length; i++, x++)
                {
                    float blend = strength * weights[i];
                    if (blend <= 0f)
                        continue;

                    int boxXMin = Mathf.Max(x - radius, readXMin) - readXMin;
                    int boxXMax = Mathf.Min(x + radius, readXMax) - readXMin + 1;
                    double sum = _table[boxZMax * stride + boxXMax] - _table[boxZMin * stride + boxXMax]
                               - _table[boxZMax * stride + boxXMin] + _table[boxZMin * stride + boxXMin];
                    float average = (float)(sum / ((boxXMax - boxXMin) * (boxZMax - boxZMin)));
                    heights[x] += (average - heights[x]) * blend;
                }
            }
        }

        /// <summary>
        /// Fills the summed-area table for a block of heights: _table[(z + 1) * stride + (x + 1)] is the sum of the heights
        /// in [xMin..x] x [zMin..z]; row 0 and column 0 are zero.
        /// </summary>
        private void BuildTable(HeightField field, int xMin, int xMax, int zMin, int zMax, int stride)
        {
            int rows = zMax - zMin + 2;
            if (_table.Length < stride * rows)
                _table = new double[Mathf.NextPowerOfTwo(stride * rows)];
            Array.Clear(_table, 0, stride);

            for (int z = zMin; z <= zMax; z++)
            {
                ReadOnlySpan<float> heights = field.Row(z);
                int row = (z - zMin + 1) * stride;
                int above = row - stride;
                double rowSum = 0;
                _table[row] = 0;
                for (int x = xMin; x <= xMax; x++)
                {
                    rowSum += heights[x];
                    int column = x - xMin + 1;
                    _table[row + column] = _table[above + column] + rowSum;
                }
            }
        }
    }
}
