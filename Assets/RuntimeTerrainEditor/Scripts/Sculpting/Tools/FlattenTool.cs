using System;
using UnityEngine;

namespace RuntimeTerrainEditor.Sculpting
{
    /// <summary>
    /// Moves heights towards <see cref="TargetHeight"/> by at most Strength (normalized height) scaled by the brush weight.
    /// </summary>
    public sealed class FlattenTool : ITerrainTool
    {
        /// <summary>Normalized height [0,1] of the terrain height.</summary>
        public float TargetHeight { get; set; } = 0.5f;

        /// <summary>Edits the heightmap.</summary>
        public TerrainChannel Channel => TerrainChannel.Heights;

        /// <summary>Moves every height under the brush towards the target height.</summary>
        public void Apply(in TerrainToolContext context)
        {
            RectInt cells = context.Footprint.Cells;
            float target = Mathf.Clamp01(TargetHeight);
            float strength = Mathf.Abs(context.Strength);
            for (int z = cells.yMin; z < cells.yMax; z++)
            {
                Span<float> heights = context.Heights.Row(z).Slice(cells.xMin, cells.width);
                ReadOnlySpan<float> weights = context.Footprint.Row(z);
                for (int i = 0; i < heights.Length; i++)
                {
                    heights[i] = Mathf.MoveTowards(heights[i], target, strength * weights[i]);
                }
            }
        }
    }
}
