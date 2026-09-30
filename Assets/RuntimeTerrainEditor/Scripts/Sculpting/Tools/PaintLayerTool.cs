using System;
using UnityEngine;

namespace RuntimeTerrainEditor.Sculpting
{
    /// <summary>
    /// Moves one terrain layer's weight towards <see cref="TargetOpacity"/> by at most Strength scaled by the brush weight,
    /// then rescales the other layers so every cell still sums to 1.
    /// </summary>
    public sealed class PaintLayerTool : ITerrainTool
    {
        /// <summary>Index of the terrain layer to paint.</summary>
        public int Layer { get; set; }

        /// <summary>Weight [0,1] the layer is painted towards; below 1 blends it with what is already there.</summary>
        public float TargetOpacity { get; set; } = 1f;

        /// <summary>
        /// Takes the leftover weight when the painted layer is reduced on a cell where no other layer is present.
        /// </summary>
        public int FallbackLayer { get; set; }

        /// <summary>Edits the painted layer weights.</summary>
        public TerrainChannel Channel => TerrainChannel.Splat;

        /// <summary>Paints the layer on every cell under the brush.</summary>
        public void Apply(in TerrainToolContext context)
        {
            SplatField splat = context.Splat;
            if (splat == null || Layer < 0 || Layer >= splat.LayerCount)
                return;

            int layers = splat.LayerCount;
            int fallback = Mathf.Clamp(FallbackLayer, 0, layers - 1);
            float target = Mathf.Clamp01(TargetOpacity);
            float strength = Mathf.Abs(context.Strength);
            RectInt cells = context.Footprint.Cells;
            for (int z = cells.yMin; z < cells.yMax; z++)
            {
                Span<float> row = splat.Row(z);
                ReadOnlySpan<float> weights = context.Footprint.Row(z);
                for (int i = 0, x = cells.xMin; i < weights.Length; i++, x++)
                {
                    float step = strength * weights[i];
                    if (step <= 0f)
                        continue;

                    Span<float> cell = row.Slice(x * layers, layers);
                    float weight = Mathf.MoveTowards(cell[Layer], target, step);
                    if (weight == cell[Layer])
                        continue;

                    cell[Layer] = weight;
                    Rebalance(cell, Layer, fallback);
                }
            }
        }

        /// <summary>
        /// Rescales the other layers of a cell so all weights sum to 1 again after the painted layer changed.
        /// </summary>
        private static void Rebalance(Span<float> cell, int painted, int fallback)
        {
            float others = 0f;
            for (int layer = 0; layer < cell.Length; layer++)
            {
                if (layer != painted)
                    others += cell[layer];
            }

            float remaining = 1f - cell[painted];
            if (others > 1e-5f)
            {
                float scale = remaining / others;
                for (int layer = 0; layer < cell.Length; layer++)
                {
                    if (layer != painted)
                        cell[layer] *= scale;
                }
            }
            else if (painted != fallback)
            {
                cell[fallback] = remaining;
            }
            else
            {
                // Nothing else can take the weight, so the only layer stays opaque
                cell[painted] = 1f;
            }
        }
    }
}
