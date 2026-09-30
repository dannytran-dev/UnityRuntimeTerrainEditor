using System.Collections.Generic;
using UnityEngine;

namespace RuntimeTerrainEditor.Demo
{
    /// <summary>
    /// UI panel for painting terrain layers, with one button per layer.
    /// </summary>
    public class PaintPanel : TerrainPanel
    {
        [Header("Terrain Layers")]
        [Tooltip("Inactive button that is copied once per terrain layer, next to it in the hierarchy")]
        [SerializeField] private PaletteButton paintButtonTemplate;

        private readonly List<PaletteButton> _paintButtons = new List<PaletteButton>();
        private TerrainLayerPalette _palette;

        /// <summary>Selects Paint mode and makes one button per terrain layer, rebuilt whenever the layers change.</summary>
        protected override void OnOpened(TerrainEditor editor)
        {
            _palette = editor.LayerPalette;
            if (_palette != null)
                _palette.LayersChanged += RebuildPaintButtons;
            RebuildPaintButtons();
            SelectPaint();
        }

        /// <summary>Stops following layer changes.</summary>
        protected override void OnClosed(TerrainEditor editor)
        {
            if (_palette != null)
                _palette.LayersChanged -= RebuildPaintButtons;
            _palette = null;
        }

        /// <summary>Selects the terrain layer to paint with (the layer buttons call this).</summary>
        public void SetPaintLayer(int layer)
        {
            TerrainEditor.Instance.SetPaintLayer(layer);
        }

        /// <summary>Paints the base layer (layer 0) when the eraser toggle turns on, which wipes out the other layers.</summary>
        public void SelectEraser(bool isOn)
        {
            if (isOn)
                SetPaintLayer(0);
        }

        /// <summary>Switches the editor to Paint mode.</summary>
        public void SelectPaint()
        {
            TerrainEditor.Instance.SetDeformMode(DeformMode.Paint);
        }

        /// <summary>
        /// Makes one paint button per terrain layer, reusing the buttons that already exist.
        /// </summary>
        private void RebuildPaintButtons()
        {
            TerrainEditor editor = TerrainEditor.Instance;
            if (editor == null || paintButtonTemplate == null)
                return;

            TerrainLayer[] layers = editor.Surface.Tiles[0].Data.terrainLayers;
            PaletteButton.Rebuild(paintButtonTemplate, _paintButtons, layers.Length,
                (button, i) => button.Setup(i, layers[i], SetPaintLayer));
        }
    }
}
