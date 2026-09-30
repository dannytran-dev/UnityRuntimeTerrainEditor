using UnityEngine;

namespace RuntimeTerrainEditor.Sculpting
{
    /// <summary>
    /// Scatters trees of one type under the brush, keeping at least <see cref="Spacing"/> meters between trees.
    /// Strength is how much of the brush's free space to fill per dab (1 = try to fill it all at once).
    /// </summary>
    public sealed class TreePaintTool : ITerrainTool
    {
        private const int MaxAttemptsPerDab = 64;

        /// <summary>Index into the terrain's tree types (prototypes).</summary>
        public int TreeType { get; set; }

        /// <summary>Minimum distance between trees in meters. Smaller packs trees closer together.</summary>
        public float Spacing { get; set; } = 4f;

        /// <summary>Random height scale range for new trees.</summary>
        public Vector2 HeightScale { get; set; } = new Vector2(0.8f, 1.2f);

        /// <summary>Random width scale range for new trees (ignored when <see cref="LockWidthToHeight"/> is on).</summary>
        public Vector2 WidthScale { get; set; } = new Vector2(0.8f, 1.2f);

        /// <summary>Scale trees evenly instead of picking width and height separately.</summary>
        public bool LockWidthToHeight { get; set; } = true;

        /// <summary>Edits the trees.</summary>
        public TerrainChannel Channel => TerrainChannel.Trees;

        /// <summary>Tries random spots under the brush and places a tree wherever there is room.</summary>
        public void Apply(in TerrainToolContext context)
        {
            TreeField trees = context.Trees;
            if (TreeType < 0 || TreeType >= trees.TypeCount)
                return;

            BrushArea area = context.Area;
            Vector3 size = context.TerrainSize;
            float spacing = Mathf.Max(0.1f, Spacing);

            // Attempts scale with how many trees fit in the brush, so strength sets how quickly an area fills up
            float expected = Mathf.Max(0f, context.Strength) * area.AreaSquareMeters / (spacing * spacing);
            int attempts = (int)expected + (Random.value < expected - (int)expected ? 1 : 0);
            attempts = Mathf.Min(attempts, MaxAttemptsPerDab);

            float extent = area.Extent;
            for (int i = 0; i < attempts; i++)
            {
                float offsetX = Random.Range(-extent, extent);
                float offsetZ = Random.Range(-extent, extent);
                float weight = area.WeightAtOffset(offsetX, offsetZ);
                if (weight <= 0f || Random.value > weight)
                    continue;

                Vector2 position = new Vector2(area.Center.x + offsetX / size.x, area.Center.y + offsetZ / size.z);
                if (position.x < 0f || position.x > 1f || position.y < 0f || position.y > 1f)
                    continue;
                if (trees.AnyWithin(position, spacing, size))
                    continue;

                float height = Random.Range(HeightScale.x, HeightScale.y);
                trees.Add(new TreeInstance
                {
                    position = new Vector3(position.x, context.HeightAt(position), position.y),
                    prototypeIndex = TreeType,
                    heightScale = height,
                    widthScale = LockWidthToHeight ? height : Random.Range(WidthScale.x, WidthScale.y),
                    rotation = Random.Range(0f, Mathf.PI * 2f),
                    color = Color.white,
                    lightmapColor = Color.white,
                });
            }
        }
    }
}
