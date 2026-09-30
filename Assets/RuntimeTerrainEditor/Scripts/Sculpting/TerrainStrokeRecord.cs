using System;
using UnityEngine;

namespace RuntimeTerrainEditor.Sculpting
{
    /// <summary>Which kinds of terrain data an edit touched.</summary>
    [Flags]
    public enum TerrainChannels
    {
        /// <summary>Nothing.</summary>
        None = 0,
        /// <summary>The heightmap.</summary>
        Heights = 1,
        /// <summary>The painted terrain layer weights.</summary>
        Splat = 2,
        /// <summary>The grass and other detail densities.</summary>
        Details = 4,
        /// <summary>The tree instances.</summary>
        Trees = 8,
        /// <summary>The holes.</summary>
        Holes = 16,
        /// <summary>Everything.</summary>
        All = Heights | Splat | Details | Trees | Holes,
    }

    /// <summary>
    /// Undo step for one stroke, which can span several terrain tiles. Stores the values of only the 32x32 blocks of each
    /// grid the stroke changed (and the trees it added and removed). Each block is kept once: undo and redo swap it with
    /// what the terrain holds.
    /// </summary>
    public sealed class TerrainStrokeRecord : ITerrainUndoAction
    {
        // Rough memory of one TreeInstance
        internal const long TreeSizeInBytes = 40;

        private readonly TileChanges[] _tiles;

        /// <summary>What the stroke changed.</summary>
        public TerrainChannels Channels { get; }

        /// <summary>True when the stroke changed heights.</summary>
        public bool ChangesHeights => (Channels & TerrainChannels.Heights) != 0;

        /// <summary>True when the stroke painted terrain layers. These steps can't be replayed after the layer count changes.</summary>
        public bool ChangesSplat => (Channels & TerrainChannels.Splat) != 0;

        /// <summary>World-space box around everything the stroke touched.</summary>
        public Bounds WorldBounds { get; }

        /// <summary>Approximate memory this undo step holds.</summary>
        public long SizeInBytes { get; }

        /// <summary>Creates the undo step from the changes of every tile; made by <see cref="TerrainSurface.EndStroke"/>.</summary>
        internal TerrainStrokeRecord(TileChanges[] tiles)
        {
            _tiles = tiles;
            Bounds bounds = tiles[0].WorldBounds;
            foreach (TileChanges tile in tiles)
            {
                Channels |= tile.Channels;
                SizeInBytes += tile.SizeInBytes;
                bounds.Encapsulate(tile.WorldBounds);
            }
            WorldBounds = bounds;
        }

        /// <summary>Writes the values from before the stroke back onto the terrain.</summary>
        public void Undo()
        {
            foreach (TileChanges tile in _tiles)
                tile.Restore(true);
        }

        /// <summary>Writes the values from after the stroke back onto the terrain.</summary>
        public void Redo()
        {
            foreach (TileChanges tile in _tiles)
                tile.Restore(false);
        }

        /// <summary>True when the stroke changed a tile.</summary>
        public bool Touches(TerrainTile tile)
        {
            foreach (TileChanges changes in _tiles)
            {
                if (changes.Tile == tile)
                    return true;
            }
            return false;
        }

        /// <summary>The <see cref="TerrainChannels"/> flag of a single channel.</summary>
        internal static TerrainChannels ToFlag(TerrainChannel channel)
        {
            return (TerrainChannels)(1 << (int)channel);
        }

        /// <summary>What one stroke changed on one terrain tile.</summary>
        internal sealed class TileChanges
        {
            /// <summary>The tile the changes belong to.</summary>
            public readonly TerrainTile Tile;
            /// <summary>Changed values of the tile's grids.</summary>
            public readonly Delta[] Deltas;
            /// <summary>Added and removed trees, or null when the stroke didn't touch trees.</summary>
            public readonly TreeDelta TreeDelta;
            /// <summary>World-space box around what the stroke touched on this tile.</summary>
            public readonly Bounds WorldBounds;

            /// <summary>What the stroke changed on this tile.</summary>
            public TerrainChannels Channels { get; }

            /// <summary>Approximate memory these changes hold.</summary>
            public long SizeInBytes { get; }

            /// <summary>Collects one tile's changes.</summary>
            public TileChanges(TerrainTile tile, Delta[] deltas, TreeDelta treeDelta, Bounds worldBounds)
            {
                Tile = tile;
                Deltas = deltas;
                TreeDelta = treeDelta;
                WorldBounds = worldBounds;
                foreach (Delta delta in deltas)
                {
                    Channels |= ToFlag(delta.Channel);
                    SizeInBytes += delta.Values.Length * sizeof(float);
                }
                if (treeDelta != null)
                {
                    Channels |= TerrainChannels.Trees;
                    SizeInBytes += (treeDelta.Added.Length + treeDelta.Removed.Length) * TreeSizeInBytes;
                }
            }

            /// <summary>Puts the recorded state back (from before the stroke for undo, after it for redo).</summary>
            public void Restore(bool undo)
            {
                foreach (Delta delta in Deltas)
                    Tile.Restore(delta, undo);
                if (TreeDelta != null)
                    Tile.Restore(TreeDelta, undo);
            }
        }

        /// <summary>
        /// The changed blocks of one grid channel. <see cref="Values"/> holds the state the terrain doesn't have: from before
        /// the stroke while it can be undone, from after it once undone.
        /// </summary>
        internal sealed class Delta
        {
            /// <summary>The grid the values belong to.</summary>
            public readonly TerrainChannel Channel;
            /// <summary>Grid resolution when recorded; restoring is skipped if it changed since.</summary>
            public readonly int Resolution;
            /// <summary>Values per cell when recorded; restoring is skipped if it changed since.</summary>
            public readonly int Channels;
            /// <summary>The recorded blocks of cells, in the order their values are packed.</summary>
            public readonly RectInt[] Blocks;
            /// <summary>Box around every cell the stroke changed, in cells.</summary>
            public readonly RectInt Bounds;
            /// <summary>Packed values of all blocks, swapped with the terrain's on undo and redo.</summary>
            public readonly float[] Values;

            /// <summary>True once undone (<see cref="Values"/> then holds the state after the stroke).</summary>
            public bool IsUndone;

            /// <summary>Creates a delta from the packed values from before the stroke.</summary>
            public Delta(TerrainChannel channel, int resolution, int channels, RectInt[] blocks, RectInt bounds, float[] before)
            {
                Channel = channel;
                Resolution = resolution;
                Channels = channels;
                Blocks = blocks;
                Bounds = bounds;
                Values = before;
            }
        }

        /// <summary>The trees a stroke added and removed on one tile.</summary>
        internal sealed class TreeDelta
        {
            /// <summary>Trees the stroke added.</summary>
            public readonly TreeInstance[] Added;
            /// <summary>Trees the stroke removed.</summary>
            public readonly TreeInstance[] Removed;

            /// <summary>True once undone.</summary>
            public bool IsUndone;

            /// <summary>Creates a tree delta.</summary>
            public TreeDelta(TreeInstance[] added, TreeInstance[] removed)
            {
                Added = added;
                Removed = removed;
            }
        }
    }
}
