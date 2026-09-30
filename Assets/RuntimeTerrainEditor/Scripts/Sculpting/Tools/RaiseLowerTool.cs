using System;
using UnityEngine;

namespace RuntimeTerrainEditor.Sculpting
{
    /// <summary>
    /// Adds Strength (normalized height, negative lowers) scaled by the brush weight.
    /// </summary>
    public sealed class RaiseLowerTool : ITerrainTool
    {
        /// <summary>Edits the heightmap.</summary>
        public TerrainChannel Channel => TerrainChannel.Heights;

        /// <summary>Adds the strength to every height under the brush, scaled by the brush weight.</summary>
        public void Apply(in TerrainToolContext context)
        {
            RectInt cells = context.Footprint.Cells;
            float strength = context.Strength;
            for (int z = cells.yMin; z < cells.yMax; z++)
            {
                Span<float> heights = context.Heights.Row(z).Slice(cells.xMin, cells.width);
                ReadOnlySpan<float> weights = context.Footprint.Row(z);
                for (int i = 0; i < heights.Length; i++)
                {
                    heights[i] = Mathf.Clamp01(heights[i] + strength * weights[i]);
                }
            }
        }
    }
}
