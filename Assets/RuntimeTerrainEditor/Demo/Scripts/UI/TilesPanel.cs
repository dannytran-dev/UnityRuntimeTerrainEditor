using TMPro;
using UnityEngine;

namespace RuntimeTerrainEditor.Demo
{
    /// <summary>
    /// UI panel for adding and removing terrain tiles. Add shows a plus on every free spot next to the terrain and adds a
    /// tile where you click; Remove outlines the tile under the pointer and removes it on click. Holding Invert (Shift)
    /// swaps the two. Remembers the choice while other panels are open, and shows how many tiles there are.
    /// </summary>
    public class TilesPanel : TerrainPanel
    {
        [Tooltip("Optional. Shows the number of tiles")]
        [SerializeField] private TMP_Text tileCountText;

        private DeformMode _mode = DeformMode.AddTiles;

        /// <summary>Selects the tile mode that was in use when the panel was last open and starts counting tiles.</summary>
        protected override void OnOpened(TerrainEditor editor)
        {
            editor.SetDeformMode(_mode);
            editor.TerrainChanged += HandleTerrainChanged;
            UpdateTileCount();
        }

        /// <summary>Stops counting tiles.</summary>
        protected override void OnClosed(TerrainEditor editor)
        {
            editor.TerrainChanged -= HandleTerrainChanged;
        }

        /// <summary>Adds tiles where you click, once the Add toggle turns on.</summary>
        public void SelectAddTiles(bool isOn)
        {
            if (isOn)
                SetDeformMode(DeformMode.AddTiles);
        }

        /// <summary>Removes the tile you click, once the Remove toggle turns on.</summary>
        public void SelectRemoveTiles(bool isOn)
        {
            if (isOn)
                SetDeformMode(DeformMode.RemoveTiles);
        }

        /// <summary>Remembers the mode and tells the editor.</summary>
        private void SetDeformMode(DeformMode mode)
        {
            _mode = mode;
            TerrainEditor.Instance.SetDeformMode(mode);
        }

        /// <summary>Updates the tile count after every edit (tiles added or removed, undo, load).</summary>
        private void HandleTerrainChanged(TerrainChange change)
        {
            UpdateTileCount();
        }

        /// <summary>Shows the number of tiles.</summary>
        private void UpdateTileCount()
        {
            if (tileCountText == null)
                return;
            int count = TerrainEditor.Instance.Surface.Tiles.Count;
            tileCountText.text = count == 1 ? "1 TILE" : count + " TILES";
        }
    }
}
