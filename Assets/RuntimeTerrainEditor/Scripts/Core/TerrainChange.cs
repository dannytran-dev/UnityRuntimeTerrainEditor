using RuntimeTerrainEditor.Sculpting;
using UnityEngine;

namespace RuntimeTerrainEditor
{
    /// <summary>What caused a <see cref="TerrainChange"/>.</summary>
    public enum TerrainChangeSource
    {
        /// <summary>A brush stroke finished.</summary>
        Stroke,
        /// <summary>A step was undone.</summary>
        Undo,
        /// <summary>A step was redone.</summary>
        Redo,
        /// <summary>The terrain was reset to its starting state.</summary>
        Reset,
        /// <summary>A save was loaded.</summary>
        Load,
        /// <summary>A heightmap was imported.</summary>
        Import,
        /// <summary>A terrain tile was added.</summary>
        TileAdded,
        /// <summary>A terrain tile was removed.</summary>
        TileRemoved,
    }

    /// <summary>
    /// Describes one finished edit: what kind of data changed and where. Sent by <see cref="TerrainEditor.TerrainChanged"/>
    /// so gameplay can react, e.g. rebuild navigation or put objects back on the ground.
    /// </summary>
    public readonly struct TerrainChange
    {
        /// <summary>What caused the change.</summary>
        public readonly TerrainChangeSource Source;

        /// <summary>The kinds of terrain data that changed.</summary>
        public readonly TerrainChannels Channels;

        /// <summary>World-space box around the changed area (full terrain height).</summary>
        public readonly Bounds WorldBounds;

        /// <summary>Creates a change description.</summary>
        public TerrainChange(TerrainChangeSource source, TerrainChannels channels, Bounds worldBounds)
        {
            Source = source;
            Channels = channels;
            WorldBounds = worldBounds;
        }

        /// <summary>True when any of the given kinds of data changed.</summary>
        public bool Includes(TerrainChannels channels)
        {
            return (Channels & channels) != 0;
        }

        /// <summary>True when a world position lies inside the changed area (ignoring height).</summary>
        public bool Covers(Vector3 worldPosition)
        {
            return worldPosition.x >= WorldBounds.min.x && worldPosition.x <= WorldBounds.max.x
                && worldPosition.z >= WorldBounds.min.z && worldPosition.z <= WorldBounds.max.z;
        }
    }
}
