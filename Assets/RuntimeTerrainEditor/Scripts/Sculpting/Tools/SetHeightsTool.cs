using System;
using UnityEngine;

namespace RuntimeTerrainEditor.Sculpting
{
    /// <summary>
    /// Blends the terrain towards a heightmap that covers the whole surface, over all tiles (any resolution, stretched
    /// to fit). Strength 1 with a solid brush replaces the heights; used for heightmap import.
    /// </summary>
    public sealed class SetHeightsTool : ITerrainTool
    {
        /// <summary>Normalized heights [0,1] indexed [z, x], covering the whole surface.</summary>
        public float[,] Source { get; set; }

        /// <summary>Edits the heightmap.</summary>
        public TerrainChannel Channel => TerrainChannel.Heights;

        /// <summary>Blends every height under the brush towards the source heightmap.</summary>
        public void Apply(in TerrainToolContext context)
        {
            if (Source == null)
                return;

            HeightField heights = context.Heights;
            Rect area = context.TileArea;
            float last = heights.Resolution - 1;
            float strength = Mathf.Clamp01(context.Strength);
            RectInt cells = context.Footprint.Cells;
            for (int z = cells.yMin; z < cells.yMax; z++)
            {
                Span<float> row = heights.Row(z);
                ReadOnlySpan<float> weights = context.Footprint.Row(z);
                float v = area.y + z / last * area.height;
                for (int i = 0, x = cells.xMin; i < weights.Length; i++, x++)
                {
                    float u = area.x + x / last * area.width;
                    float target = SampleBilinear(Source, u, v);
                    row[x] += (target - row[x]) * strength * weights[i];
                }
            }
        }

        /// <summary>Bilinear sample of a [z, x] grid at normalized coordinates.</summary>
        public static float SampleBilinear(float[,] grid, float u, float v)
        {
            int height = grid.GetLength(0);
            int width = grid.GetLength(1);
            float x = Mathf.Clamp01(u) * (width - 1);
            float z = Mathf.Clamp01(v) * (height - 1);
            int x0 = (int)x;
            int z0 = (int)z;
            int x1 = Mathf.Min(x0 + 1, width - 1);
            int z1 = Mathf.Min(z0 + 1, height - 1);
            float fx = x - x0;
            float fz = z - z0;
            float bottom = Mathf.Lerp(grid[z0, x0], grid[z0, x1], fx);
            float top = Mathf.Lerp(grid[z1, x0], grid[z1, x1], fx);
            return Mathf.Lerp(bottom, top, fz);
        }
    }
}
