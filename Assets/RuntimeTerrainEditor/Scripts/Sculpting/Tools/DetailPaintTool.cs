using System;
using UnityEngine;

namespace RuntimeTerrainEditor.Sculpting
{
    /// <summary>
    /// Paints grass and other details: moves one detail type's density towards <see cref="TargetDensity"/> by at most
    /// Strength (fraction of the full density) scaled by the brush weight. A target of 0 thins it out gradually;
    /// <see cref="DetailEraseTool"/> clears at once.
    /// </summary>
    public sealed class DetailPaintTool : ITerrainTool
    {
        /// <summary>Index into the terrain's detail types (prototypes).</summary>
        public int DetailType { get; set; }

        /// <summary>Density to paint towards, 0 (none) to 1 (full).</summary>
        public float TargetDensity { get; set; } = 1f;

        /// <summary>Edits the grass (detail) densities.</summary>
        public TerrainChannel Channel => TerrainChannel.Details;

        /// <summary>Moves the detail type's density towards the target on every cell under the brush.</summary>
        public void Apply(in TerrainToolContext context)
        {
            DetailField details = context.Details;
            if (details == null || DetailType < 0 || DetailType >= details.TypeCount)
                return;

            int types = details.TypeCount;
            float max = details.MaxDensity;
            float target = Mathf.Clamp01(TargetDensity) * max;
            float step = Mathf.Abs(context.Strength) * max;
            details.MarkChanged(DetailType);
            RectInt cells = context.Footprint.Cells;
            for (int z = cells.yMin; z < cells.yMax; z++)
            {
                Span<float> row = details.Row(z);
                ReadOnlySpan<float> weights = context.Footprint.Row(z);
                for (int i = 0, x = cells.xMin; i < weights.Length; i++, x++)
                {
                    int index = x * types + DetailType;
                    row[index] = Mathf.MoveTowards(row[index], target, step * weights[i]);
                }
            }
        }
    }
}
