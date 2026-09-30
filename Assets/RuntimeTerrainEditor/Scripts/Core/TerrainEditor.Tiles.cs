using System.Collections.Generic;
using RuntimeTerrainEditor.Sculpting;
using UnityEngine;

namespace RuntimeTerrainEditor
{
    // Adding and removing terrain tiles while the game runs. The rest of TerrainEditor (and its summary) is in TerrainEditor.cs.
    public partial class TerrainEditor
    {
        /// <summary>
        /// True when a tile can be added where a world position is: the tiles form a grid, that slot of the grid is free
        /// and it shares a side with a tile.
        /// </summary>
        public bool CanAddTile(Vector3 worldPosition)
        {
            return Surface.IsGrid && Surface.IsFreeSlot(Surface.GetSlotPosition(worldPosition));
        }

        /// <summary>
        /// Adds a terrain tile in the free grid slot at a world position (see <see cref="CanAddTile"/>). The new tile gets
        /// the size, resolutions, layers, trees, grass and look of the other tiles. Its ground starts at the starting
        /// height and blends into the edges of the tiles next to it. This is one undo step.
        /// </summary>
        /// <returns>The new tile's terrain, or null when no tile can be added there.</returns>
        public Terrain AddTile(Vector3 worldPosition)
        {
            if (!CanAddTile(worldPosition))
                return null;
            if (_strokeActive)
                EndStroke();

            TerrainTile tile = CreateTile(Surface.GetSlotPosition(worldPosition));
            // Where two neighbours disagree about a corner they share with the new tile, the seams are evened out on their
            // side too; that change is part of the undo step
            Surface.BeginStroke();
            Surface.CloseSeams(tile);
            TerrainStrokeRecord seams = Surface.EndStroke();

            RecordStep(new TileEdit(this, tile, true, seams));
            RaiseChanged(new TerrainChange(TerrainChangeSource.TileAdded, TerrainChannels.All, tile.WorldBounds));
            return tile.Terrain;
        }

        /// <summary>
        /// Removes a terrain tile. Its terrain object is switched off and kept while undo can bring it back, then destroyed.
        /// The last tile can't be removed. This is one undo step.
        /// </summary>
        /// <returns>False when the terrain isn't one of the tiles, or is the last one.</returns>
        public bool RemoveTile(Terrain terrain)
        {
            TerrainTile tile = FindTile(terrain);
            if (tile == null || Surface.Tiles.Count <= 1)
                return false;
            if (_strokeActive)
                EndStroke();

            Bounds bounds = tile.WorldBounds;
            DetachTile(tile);
            RecordStep(new TileEdit(this, tile, false, null));
            RaiseChanged(new TerrainChange(TerrainChangeSource.TileRemoved, TerrainChannels.All, bounds));
            return true;
        }

        /// <summary>
        /// The AddTiles and RemoveTiles modes: shows the free slots or the tile under the pointer, and adds or removes a
        /// tile when Apply is pressed.
        /// </summary>
        private void HandleTileEditing(bool pointerOverUI)
        {
            if (brushProjector != null)
                brushProjector.Hide();
            if (ActiveMode == DeformMode.AddTiles)
                HandleTileAdding(pointerOverUI);
            else
                HandleTileRemoving(pointerOverUI);
        }

        /// <summary>Shows the free slots, highlights the one under the pointer and adds a tile there on click.</summary>
        private void HandleTileAdding(bool pointerOverUI)
        {
            if (_freeSlotsVersion != Surface.LayoutVersion)
            {
                Surface.GetFreeSlots(_freeSlots);
                _freeSlotsVersion = Surface.LayoutVersion;
            }
            float groundHeight = GetNewTileGroundHeight();
            int hoveredSlot = -1;
            if (!pointerOverUI && _mouseSelection.RaycastHeight(_input.PointerPosition, groundHeight, out Vector3 point))
                hoveredSlot = FindSlot(_freeSlots, Surface.GetSlotPosition(point));

            if (tilePreview != null)
                tilePreview.ShowSlots(_freeSlots, Surface.TileSize, groundHeight, hoveredSlot);
            if (hoveredSlot >= 0 && _input.Apply.WasPressedThisFrame())
                AddTile(_freeSlots[hoveredSlot] + Surface.TileSize * 0.5f);
        }

        /// <summary>Outlines the tile under the pointer and removes it on click (never the last tile).</summary>
        private void HandleTileRemoving(bool pointerOverUI)
        {
            TerrainTile tile = null;
            if (!pointerOverUI && _mouseSelection.IsHittingTerrain && Surface.Tiles.Count > 1)
                tile = Surface.GetTile(_mouseSelection.HitPoint);

            if (tilePreview != null)
            {
                if (tile != null)
                    tilePreview.ShowTile(GetTileBox(tile));
                else
                    tilePreview.Hide();
            }
            if (tile != null && _input.Apply.WasPressedThisFrame())
                RemoveTile(tile.Terrain);
        }

        /// <summary>
        /// Creates a terrain tile at a free slot, shaped and set up like the first tile, and adds it to the surface.
        /// </summary>
        private TerrainTile CreateTile(Vector3 slotPosition)
        {
            TerrainTile model = Surface.Tiles[0];
            TerrainData data = TerrainTileFactory.CreateDataLike(model.Data);
            _runtimeTerrainData.Add(data);
            data.SetHeights(0, 0, GetNewTileHeights(slotPosition, data.heightmapResolution));

            string tileName = "Terrain Tile (" + slotPosition.x.ToString("0") + ", " + slotPosition.z.ToString("0") + ")";
            Terrain terrain = TerrainTileFactory.CreateTerrain(data, slotPosition, tileName, model.Terrain, model.Terrain.transform.parent);
            TerrainTile tile = Surface.AddTile(terrain);
            UpdateTileLinks();
            return tile;
        }

        /// <summary>
        /// Heights for a new tile [z, x]: the starting height, rising or falling towards the heights along the edge of
        /// each neighbouring tile over the outer part of the tile (New Tile Blend), so the new tile continues the ground it
        /// joins. Along a shared edge it matches the neighbour exactly.
        /// </summary>
        private float[,] GetNewTileHeights(Vector3 slotPosition, int resolution)
        {
            int last = resolution - 1;
            float blend = Mathf.Max(1f, last * newTileBlend);
            // The neighbours' edges facing the new tile, in the order left, right, front, back (null: no neighbour there)
            Vector3 size = Surface.TileSize;
            float[][] edges =
            {
                ReadNeighborEdge(slotPosition - new Vector3(size.x, 0f, 0f), new RectInt(last, 0, 1, resolution)),
                ReadNeighborEdge(slotPosition + new Vector3(size.x, 0f, 0f), new RectInt(0, 0, 1, resolution)),
                ReadNeighborEdge(slotPosition - new Vector3(0f, 0f, size.z), new RectInt(0, last, resolution, 1)),
                ReadNeighborEdge(slotPosition + new Vector3(0f, 0f, size.z), new RectInt(0, 0, resolution, 1)),
            };

            float[,] heights = new float[resolution, resolution];
            for (int z = 0; z < resolution; z++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    float influence = 0f;
                    float weightSum = 0f;
                    float edgeSum = 0f;
                    for (int side = 0; side < 4; side++)
                    {
                        if (edges[side] == null)
                            continue;
                        int distance = side == 0 ? x : side == 1 ? last - x : side == 2 ? z : last - z;
                        if (distance >= blend)
                            continue;
                        // The nearest edge counts most, and fully on the edge itself, so shared edges match exactly
                        float weight = 1f / (distance * distance + 0.0001f);
                        weightSum += weight;
                        edgeSum += weight * edges[side][side <= 1 ? z : x];
                        influence = Mathf.Max(influence, 1f - Mathf.SmoothStep(0f, 1f, distance / blend));
                    }
                    heights[z, x] = weightSum > 0f ? Mathf.Lerp(startingHeightPercent, edgeSum / weightSum, influence) : startingHeightPercent;
                }
            }
            return heights;
        }

        /// <summary>The heights of a region of the tile at a slot (one edge row or column), or null when the slot is empty.</summary>
        private float[] ReadNeighborEdge(Vector3 slotPosition, RectInt edge)
        {
            TerrainTile neighbor = Surface.GetTileAtSlot(slotPosition);
            return neighbor != null ? neighbor.GetHeightRegion(edge) : null;
        }

        /// <summary>Puts a removed tile back: switches its terrain on and adds it to the surface.</summary>
        private void AttachTile(TerrainTile tile)
        {
            tile.Terrain.gameObject.SetActive(true);
            MatchTileSettings(tile);
            Surface.AddTile(tile);
            _detachedTiles.Remove(tile);
            UpdateTileLinks();
        }

        /// <summary>Takes a tile off the surface and switches its terrain off, keeping it for undo.</summary>
        private void DetachTile(TerrainTile tile)
        {
            if (!Surface.RemoveTile(tile))
                return;
            tile.Terrain.gameObject.SetActive(false);
            _detachedTiles.Add(tile);
            UpdateTileLinks();
        }

        /// <summary>
        /// Gives a tile that was removed for a while the layers, trees and grass the other tiles have now, in case they
        /// were changed in the meantime.
        /// </summary>
        private void MatchTileSettings(TerrainTile tile)
        {
            TerrainData model = Surface.Tiles[0].Data;
            if (HasSameSettings(model, tile.Data))
                return;
            TerrainTileFactory.CopySettings(model, tile.Data);
            tile.Reload();
        }

        /// <summary>Connects the palettes, the base board and Unity's terrain neighbours to the current tiles.</summary>
        private void UpdateTileLinks()
        {
            List<TerrainData> tileData = GetTileData();
            if (layerPalette != null)
                layerPalette.SetTiles(tileData);
            if (vegetationPalette != null)
                vegetationPalette.SetTiles(tileData);
            if (_terrainBaseBoard != null)
                _terrainBaseBoard.Rebuild();
            Terrain.SetConnectivityDirty();
        }

        /// <summary>Adds an undo step, then destroys the removed tiles no step refers to any more.</summary>
        private void RecordStep(ITerrainUndoAction action)
        {
            _undoRedo.Record(action);
            ReleaseUnusedTiles();
        }

        /// <summary>Clears the undo history and destroys the removed tiles it kept.</summary>
        private void ClearHistory()
        {
            _undoRedo.Clear();
            ReleaseUnusedTiles();
        }

        /// <summary>
        /// Destroys the removed tiles that no undo or redo step can bring back any more, with the TerrainData made for them.
        /// </summary>
        private void ReleaseUnusedTiles()
        {
            for (int i = _detachedTiles.Count - 1; i >= 0; i--)
            {
                TerrainTile tile = _detachedTiles[i];
                if (_undoRedo.Contains(action => RefersTo(action, tile)))
                    continue;

                _detachedTiles.RemoveAt(i);
                TerrainData data = tile.Data;
                if (tile.Terrain != null)
                    Destroy(tile.Terrain.gameObject);
                if (_runtimeTerrainData.Remove(data))
                    Destroy(data);
            }
        }

        /// <summary>The surface's tile for a terrain, or null.</summary>
        private TerrainTile FindTile(Terrain terrain)
        {
            foreach (TerrainTile tile in Surface.Tiles)
            {
                if (tile.Terrain == terrain)
                    return tile;
            }
            return null;
        }

        /// <summary>World height of the ground of a new tile (the starting height).</summary>
        private float GetNewTileGroundHeight()
        {
            return Surface.WorldBounds.min.y + startingHeightPercent * Surface.TileSize.y;
        }

        /// <summary>Index of a slot position in a list of slots, or -1.</summary>
        private int FindSlot(List<Vector3> slots, Vector3 slot)
        {
            float tolerance = Surface.TileSize.x * 0.01f;
            for (int i = 0; i < slots.Count; i++)
            {
                if (Mathf.Abs(slots[i].x - slot.x) < tolerance && Mathf.Abs(slots[i].z - slot.z) < tolerance)
                    return i;
            }
            return -1;
        }

        /// <summary>World-space box around a tile's ground, from its lowest to its highest point.</summary>
        private static Bounds GetTileBox(TerrainTile tile)
        {
            Bounds bounds = tile.Data.bounds;
            bounds.center += tile.Terrain.GetPosition();
            bounds.Expand(new Vector3(0f, 1f, 0f));
            return bounds;
        }

        /// <summary>True when an undo step needs a tile: it adds or removes it, or changed it.</summary>
        private static bool RefersTo(ITerrainUndoAction action, TerrainTile tile)
        {
            if (action is TileEdit edit)
                return edit.Tile == tile;
            return action is TerrainStrokeRecord record && record.Touches(tile);
        }

        /// <summary>True when two TerrainData have the same layers, tree types and grass types.</summary>
        private static bool HasSameSettings(TerrainData first, TerrainData second)
        {
            TerrainLayer[] firstLayers = first.terrainLayers;
            TerrainLayer[] secondLayers = second.terrainLayers;
            TreePrototype[] firstTrees = first.treePrototypes;
            TreePrototype[] secondTrees = second.treePrototypes;
            DetailPrototype[] firstDetails = first.detailPrototypes;
            DetailPrototype[] secondDetails = second.detailPrototypes;
            if (firstLayers.Length != secondLayers.Length || firstTrees.Length != secondTrees.Length || firstDetails.Length != secondDetails.Length)
                return false;
            for (int i = 0; i < firstLayers.Length; i++)
            {
                if (firstLayers[i] != secondLayers[i])
                    return false;
            }
            for (int i = 0; i < firstTrees.Length; i++)
            {
                if (firstTrees[i].prefab != secondTrees[i].prefab)
                    return false;
            }
            for (int i = 0; i < firstDetails.Length; i++)
            {
                if (firstDetails[i].prototype != secondDetails[i].prototype || firstDetails[i].prototypeTexture != secondDetails[i].prototypeTexture)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Undo step for adding or removing a tile. A removed tile's terrain is kept, switched off, so undo and redo can put
        /// it back with everything painted on it.
        /// </summary>
        private sealed class TileEdit : ITerrainUndoAction
        {
            private readonly TerrainEditor _editor;
            private readonly bool _added;
            // Seam heights evened out on the neighbours when the tile was added (null when nothing changed)
            private readonly TerrainStrokeRecord _seams;

            /// <summary>The tile that was added or removed.</summary>
            public TerrainTile Tile { get; }

            /// <summary>World-space box around the tile.</summary>
            public Bounds WorldBounds { get; }

            /// <summary>
            /// Approximate memory the step keeps alive: the tile's CPU copies and its TerrainData (about as big), which stay
            /// while undo or redo can bring the tile back.
            /// </summary>
            public long SizeInBytes { get; }

            /// <summary>Creates the undo step for a tile that was just added or removed.</summary>
            public TileEdit(TerrainEditor editor, TerrainTile tile, bool added, TerrainStrokeRecord seams)
            {
                _editor = editor;
                _added = added;
                _seams = seams;
                Tile = tile;
                WorldBounds = tile.WorldBounds;
                SizeInBytes = tile.SizeInBytes * 2 + (seams != null ? seams.SizeInBytes : 0);
            }

            /// <summary>Removes an added tile again, or puts a removed one back.</summary>
            public void Undo()
            {
                if (!_added)
                {
                    _editor.AttachTile(Tile);
                    return;
                }
                _seams?.Undo();
                _editor.DetachTile(Tile);
            }

            /// <summary>Adds the tile again, or removes it again.</summary>
            public void Redo()
            {
                if (!_added)
                {
                    _editor.DetachTile(Tile);
                    return;
                }
                _editor.AttachTile(Tile);
                _seams?.Redo();
            }
        }
    }
}
