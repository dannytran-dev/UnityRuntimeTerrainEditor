using System;
using UnityEngine;

namespace RuntimeTerrainEditor.Sculpting
{
    /// <summary>
    /// Cuts holes in the terrain (e.g. for cave or tunnel entrances), or fills them again. Every cell under the brush whose
    /// weight reaches <see cref="MinWeight"/> is changed at once; a cell is one heightmap quad.
    /// </summary>
    public sealed class HoleTool : ITerrainTool
    {
        /// <summary>True cuts holes, false fills them.</summary>
        public bool Dig { get; set; } = true;

        /// <summary>Brush weight a cell has to reach to change. Lower values let the soft edge of a brush cut as well.</summary>
        public float MinWeight { get; set; } = 0.5f;

        /// <summary>Edits the holes.</summary>
        public TerrainChannel Channel => TerrainChannel.Holes;

        /// <summary>Cuts or fills every cell under the brush that reaches the minimum weight.</summary>
        public void Apply(in TerrainToolContext context)
        {
            HoleField holes = context.Holes;
            if (holes == null)
                return;

            float value = Dig ? HoleField.Hole : HoleField.Surface;
            float minWeight = Mathf.Max(MinWeight, 1e-4f);
            RectInt cells = context.Footprint.Cells;
            for (int z = cells.yMin; z < cells.yMax; z++)
            {
                Span<float> row = holes.Row(z).Slice(cells.xMin, cells.width);
                ReadOnlySpan<float> weights = context.Footprint.Row(z);
                for (int i = 0; i < row.Length; i++)
                {
                    if (weights[i] >= minWeight)
                        row[i] = value;
                }
            }
        }
    }
}
