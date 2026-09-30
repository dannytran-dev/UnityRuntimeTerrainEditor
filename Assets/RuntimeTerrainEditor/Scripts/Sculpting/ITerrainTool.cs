using UnityEngine;

namespace RuntimeTerrainEditor.Sculpting
{
    /// <summary>
    /// A brush operation that <see cref="TerrainSurface"/> can run. Implement this to add new tools without
    /// changing the surface: the surface rasterizes the brush on every tile it touches, handles uploading, collider
    /// refreshes, tile seams and undo.
    /// </summary>
    public interface ITerrainTool
    {
        /// <summary>
        /// The data this tool edits. For grid channels (heights, splat, details, holes) the footprint passed to
        /// <see cref="Apply"/> is built on that grid and the tool may only write cells inside it (reading outside it is fine).
        /// Tree tools get a footprint on the height grid and edit <see cref="TerrainToolContext.Trees"/>.
        /// </summary>
        TerrainChannel Channel { get; }

        /// <summary>
        /// Applies one brush dab to one tile. Called by <see cref="TerrainSurface.Apply"/> once per tile the dab touches,
        /// never directly.
        /// </summary>
        void Apply(in TerrainToolContext context);
    }

    /// <summary>
    /// Everything a tool gets for one dab on one terrain tile.
    /// </summary>
    public readonly struct TerrainToolContext
    {
        /// <summary>The cells the dab covers on the tool's grid and the brush weight [0,1] of each.</summary>
        public readonly BrushFootprint Footprint;
        /// <summary>Where the brush is, for tools that place things at arbitrary positions.</summary>
        public readonly BrushArea Area;
        /// <summary>The terrain heights (always there; tree tools read them to place trees on the ground).</summary>
        public readonly HeightField Heights;
        /// <summary>Null when the terrain has no layers.</summary>
        public readonly SplatField Splat;
        /// <summary>Null when the terrain has no detail (grass) types or no detail resolution.</summary>
        public readonly DetailField Details;
        /// <summary>The terrain's trees.</summary>
        public readonly TreeField Trees;
        /// <summary>The terrain's holes.</summary>
        public readonly HoleField Holes;
        /// <summary>Tool specific amount for this dab (see <see cref="BrushDab.Strength"/>).</summary>
        public readonly float Strength;
        /// <summary>Size of one cell of the footprint's grid in meters (x along terrain X, y along terrain Z).</summary>
        public readonly Vector2 CellSize;
        /// <summary>Size of this tile in meters.</summary>
        public readonly Vector3 TerrainSize;
        /// <summary>
        /// This tile's part of the whole surface, in normalized surface coordinates (the whole surface is 0-1). Tools that
        /// work on data covering the whole surface, like an imported heightmap, map their data onto the tile with it.
        /// </summary>
        public readonly Rect TileArea;

        /// <summary>Bundles one dab's data; made by <see cref="TerrainTile"/>.</summary>
        internal TerrainToolContext(BrushFootprint footprint, in BrushArea area, HeightField heights, SplatField splat, DetailField details,
                                    TreeField trees, HoleField holes, float strength, Vector2 cellSize, Vector3 terrainSize, Rect tileArea)
        {
            Footprint = footprint;
            Area = area;
            Heights = heights;
            Splat = splat;
            Details = details;
            Trees = trees;
            Holes = holes;
            Strength = strength;
            CellSize = cellSize;
            TerrainSize = terrainSize;
            TileArea = tileArea;
        }

        /// <summary>Normalized height [0,1] at a normalized terrain position, bilinear between heightmap samples.</summary>
        public float HeightAt(Vector2 normalizedPosition)
        {
            return Heights.Sample(normalizedPosition.x, normalizedPosition.y);
        }
    }
}
