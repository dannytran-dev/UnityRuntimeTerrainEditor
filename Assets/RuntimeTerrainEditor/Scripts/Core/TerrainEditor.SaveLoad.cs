using System;
using System.Collections.Generic;
using System.IO;
using RuntimeTerrainEditor.Sculpting;
using UnityEngine;

namespace RuntimeTerrainEditor
{
    // Saving, loading and heightmap import/export. The rest of TerrainEditor (and its summary) is in TerrainEditor.cs.
    public partial class TerrainEditor
    {
        /// <summary>Default place for a save slot: Application.persistentDataPath/slotName.rterrain</summary>
        public static string GetSavePath(string slotName)
        {
            return Path.Combine(Application.persistentDataPath, slotName + TerrainSaveFile.FileExtension);
        }

        /// <summary>
        /// Heights, painted layers, grass, trees and holes of every tile, and where the tiles are, as bytes
        /// (see <see cref="TerrainSaveFile"/>).
        /// </summary>
        public byte[] SaveToBytes()
        {
            if (_strokeActive)
                EndStroke();
            List<Terrain> tiles = new List<Terrain>(Surface.Tiles.Count);
            foreach (TerrainTile tile in Surface.Tiles)
                tiles.Add(tile.Terrain);
            return TerrainSaveFile.Save(tiles);
        }

        /// <summary>Saves the terrain to a file, creating its folder when needed (see <see cref="GetSavePath"/>).</summary>
        public void SaveToFile(string path)
        {
            string folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);
            File.WriteAllBytes(path, SaveToBytes());
        }

        /// <summary>
        /// Replaces the terrain with saved data and clears the undo history. When the save knows where its tiles were,
        /// tiles are added and removed first so the terrain has the saved layout. Returns false when the data can't be
        /// read; the terrain is left as it was then.
        /// </summary>
        public bool LoadFromBytes(byte[] bytes)
        {
            if (_strokeActive)
                EndStroke();

            TerrainSaveFile.Contents contents;
            try
            {
                // The whole file is read and checked before anything changes, so a damaged file changes nothing
                contents = TerrainSaveFile.Read(bytes);
            }
            catch (Exception exception)
            {
                Debug.LogError("TerrainEditor: couldn't load the terrain: " + exception.Message, this);
                return false;
            }

            List<string> warnings = new List<string>();
            try
            {
                List<TerrainData> tiles = MatchTileLayout(contents, warnings);
                warnings.AddRange(TerrainSaveFile.Apply(contents, tiles));
            }
            catch (Exception exception)
            {
                Debug.LogError("TerrainEditor: couldn't load the terrain: " + exception.Message, this);
                Surface.Reload(); // in case writing to the terrain failed halfway
                return false;
            }
            foreach (string warning in warnings)
                Debug.LogWarning("TerrainEditor: " + warning, this);

            Surface.Reload();
            ClearHistory();
            RefreshBaseBoard();
            RaiseChanged(new TerrainChange(TerrainChangeSource.Load, TerrainChannels.All, Surface.WorldBounds));
            return true;
        }

        /// <summary>Loads a terrain saved with <see cref="SaveToFile"/>. Returns false when the file is missing or can't be read.</summary>
        public bool LoadFromFile(string path)
        {
            if (!File.Exists(path))
            {
                Debug.LogWarning("TerrainEditor: no save file at " + path, this);
                return false;
            }
            return LoadFromBytes(File.ReadAllBytes(path));
        }

        /// <summary>
        /// Replaces the heights with a heightmap (normalized [0,1], indexed [z, x], any resolution; it is stretched over all
        /// tiles). This is one undo step.
        /// </summary>
        public void ImportHeightmap(float[,] heights)
        {
            if (heights == null)
                return;
            if (_strokeActive)
                EndStroke();

            Vector3 size = Surface.Size;
            Vector3 center = Surface.WorldBounds.center;
            Surface.BeginStroke();
            // A solid brush twice the terrain's size covers every height sample of every tile with full weight
            Surface.Apply(new SetHeightsTool { Source = heights }, new BrushDab(center, 2f * Mathf.Max(size.x, size.z), BrushShape.Solid, 1f));
            TerrainStrokeRecord record = Surface.EndStroke();
            if (record == null)
                return;

            RecordStep(record);
            RefreshBaseBoard();
            RaiseChanged(new TerrainChange(TerrainChangeSource.Import, record.Channels, Surface.WorldBounds));
        }

        /// <summary>Replaces the heights with a grayscale image (bright is high).</summary>
        public void ImportHeightmap(Texture2D image)
        {
            ImportHeightmap(TerrainHeightmapIO.FromImage(image));
        }

        /// <summary>Replaces the heights with a square 16-bit RAW heightmap.</summary>
        public void ImportHeightmapRaw(byte[] raw, bool littleEndian = true)
        {
            ImportHeightmap(TerrainHeightmapIO.FromRaw16(raw, littleEndian));
        }

        /// <summary>
        /// The heights of all tiles joined into one 16-bit RAW heightmap. It can be imported again when the tiles form a
        /// square grid (RAW files have no header, so only square ones can be read back).
        /// </summary>
        public byte[] ExportHeightmapRaw(bool littleEndian = true)
        {
            if (_strokeActive)
                EndStroke();
            return TerrainHeightmapIO.ToRaw16(Surface.GetHeights(), littleEndian);
        }

        /// <summary>
        /// Gives the terrain the tile layout of a save that knows where its tiles were: tiles are added where the save has
        /// one and the terrain doesn't, and removed where the terrain has one the save doesn't. Returns the TerrainData to
        /// load each saved tile into, in the save's order. Saves without positions, or whose tiles don't fit this
        /// terrain's grid, are matched in order.
        /// </summary>
        private List<TerrainData> MatchTileLayout(TerrainSaveFile.Contents contents, List<string> warnings)
        {
            if (!contents.HasTilePositions || !Surface.IsGrid)
                return GetTileData();

            Vector3 tileSize = Surface.TileSize;
            float baseHeight = Surface.WorldBounds.min.y;
            for (int i = 0; i < contents.TileCount; i++)
            {
                Vector3 position = contents.GetTilePosition(i);
                Vector3 size = contents.GetTileSize(i);
                Vector3 slot = Surface.GetSlotPosition(position + tileSize * 0.5f);
                bool sameSize = Mathf.Abs(size.x - tileSize.x) < 0.01f && Mathf.Abs(size.z - tileSize.z) < 0.01f;
                bool onGrid = Mathf.Abs(slot.x - position.x) < 0.01f && Mathf.Abs(slot.z - position.z) < 0.01f
                              && Mathf.Abs(position.y - baseHeight) < 0.01f;
                if (!sameSize || !onGrid)
                {
                    warnings.Add("The saved tiles don't line up with this terrain's tiles, so they were matched in order.");
                    return GetTileData();
                }
            }

            // Add the missing tiles before removing any, so the terrain never runs out of tiles
            for (int i = 0; i < contents.TileCount; i++)
            {
                Vector3 position = contents.GetTilePosition(i);
                if (Surface.GetTileAtSlot(position) == null)
                    CreateTile(position);
            }

            List<TerrainData> tiles = new List<TerrainData>(contents.TileCount);
            List<TerrainTile> saved = new List<TerrainTile>(contents.TileCount);
            for (int i = 0; i < contents.TileCount; i++)
            {
                TerrainTile tile = Surface.GetTileAtSlot(contents.GetTilePosition(i));
                tiles.Add(tile.Data);
                saved.Add(tile);
            }
            for (int i = Surface.Tiles.Count - 1; i >= 0; i--)
            {
                TerrainTile tile = Surface.Tiles[i];
                if (!saved.Contains(tile))
                    DetachTile(tile);
            }
            return tiles;
        }

        /// <summary>The TerrainData of every tile, in the surface's tile order.</summary>
        private List<TerrainData> GetTileData()
        {
            List<TerrainData> tiles = new List<TerrainData>(Surface.Tiles.Count);
            foreach (TerrainTile tile in Surface.Tiles)
                tiles.Add(tile.Data);
            return tiles;
        }
    }
}
