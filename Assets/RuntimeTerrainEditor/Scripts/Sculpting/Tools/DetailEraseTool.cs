using System;
using UnityEngine;

namespace RuntimeTerrainEditor.Sculpting
{
    /// <summary>
    /// Removes all grass and other details under the brush at once, of one type or of all types.
    /// </summary>
    public sealed class DetailEraseTool : ITerrainTool
    {
        /// <summary>Only remove this detail type, or every type when negative.</summary>
        public int DetailType { get; set; } = -1;

        /// <summary>Brush weight a cell has to be under to be cleared. The default ignores the faint outer edge of soft brushes.</summary>
        public float MinWeight { get; set; } = 0.1f;

        /// <summary>Edits the grass (detail) densities.</summary>
        public TerrainChannel Channel => TerrainChannel.Details;

        /// <summary>Clears the detail densities on every cell under the brush.</summary>
        public void Apply(in TerrainToolContext context)
        {
            DetailField details = context.Details;
            if (details == null || DetailType >= details.TypeCount)
                return;

            int types = details.TypeCount;
            int firstType = DetailType < 0 ? 0 : DetailType;
            int lastType = DetailType < 0 ? types - 1 : DetailType;
            for (int type = firstType; type <= lastType; type++)
                details.MarkChanged(type);
            RectInt cells = context.Footprint.Cells;
            for (int z = cells.yMin; z < cells.yMax; z++)
            {
                Span<float> row = details.Row(z);
                ReadOnlySpan<float> weights = context.Footprint.Row(z);
                for (int i = 0, x = cells.xMin; i < weights.Length; i++, x++)
                {
                    float weight = weights[i];
                    if (weight <= 0f || weight < MinWeight)
                        continue;
                    int cell = x * types;
                    for (int type = firstType; type <= lastType; type++)
                        row[cell + type] = 0f;
                }
            }
        }
    }
}
