namespace RuntimeTerrainEditor
{
    /// <summary>
    /// The built-in editing modes of <see cref="TerrainEditor"/>. Scenes store the selected mode by position,
    /// so add new modes at the end.
    /// </summary>
    public enum DeformMode
    {
        /// <summary>Raises the ground (lowers it while Invert is held).</summary>
        Raise,
        /// <summary>Lowers the ground (raises it while Invert is held).</summary>
        Lower,
        /// <summary>Moves the ground towards the brush target height.</summary>
        Flatten,
        /// <summary>Evens out bumps by blending towards the average height around each point.</summary>
        Smooth,
        /// <summary>Paints the selected terrain layer (texture).</summary>
        Paint,
        /// <summary>Places the selected tree type, or erases trees.</summary>
        Trees,
        /// <summary>Paints the selected grass (detail) type, or erases grass.</summary>
        Details,
        /// <summary>Cuts holes in the terrain, e.g. for cave entrances (fills them while Invert is held).</summary>
        DigHoles,
        /// <summary>Fills holes in the terrain again (digs while Invert is held).</summary>
        FillHoles,
        /// <summary>Adds a terrain tile to a free spot next to the terrain (removes tiles while Invert is held).</summary>
        AddTiles,
        /// <summary>Removes the clicked terrain tile (adds tiles while Invert is held).</summary>
        RemoveTiles,
    }

    /// <summary>Helpers for <see cref="DeformMode"/>.</summary>
    public static class DeformModeExtensions
    {
        /// <summary>
        /// The mode that does the opposite: Raise and Lower, DigHoles and FillHoles, and AddTiles and RemoveTiles swap.
        /// Other modes have no opposite and return themselves. Holding Invert uses the opposite mode.
        /// </summary>
        public static DeformMode GetOpposite(this DeformMode mode)
        {
            switch (mode)
            {
                case DeformMode.Raise: return DeformMode.Lower;
                case DeformMode.Lower: return DeformMode.Raise;
                case DeformMode.DigHoles: return DeformMode.FillHoles;
                case DeformMode.FillHoles: return DeformMode.DigHoles;
                case DeformMode.AddTiles: return DeformMode.RemoveTiles;
                case DeformMode.RemoveTiles: return DeformMode.AddTiles;
                default: return mode;
            }
        }

        /// <summary>True for the modes that add or remove terrain tiles instead of painting with the brush.</summary>
        public static bool IsTileMode(this DeformMode mode)
        {
            return mode == DeformMode.AddTiles || mode == DeformMode.RemoveTiles;
        }
    }
}
