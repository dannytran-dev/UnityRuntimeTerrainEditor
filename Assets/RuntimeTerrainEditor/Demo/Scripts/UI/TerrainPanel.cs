using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RuntimeTerrainEditor.Demo
{
    /// <summary>
    /// Shared part of the demo's editing panels: the brush strength, size and target sliders and the brush picker,
    /// which gets one button per brush in the brush settings. Opening a panel turns the terrain editor on and closing
    /// it turns it off. The sliders follow the brush, so keyboard shortcuts and other panels stay in step.
    /// </summary>
    public abstract class TerrainPanel : MonoBehaviour
    {
        [Tooltip("Sets the brush strength")]
        [SerializeField] private Slider brushStrengthSlider;
        [Tooltip("Sets the brush size")]
        [SerializeField] private Slider brushSizeSlider;
        [Tooltip("Sets the flatten height / paint opacity / density")]
        [SerializeField] private Slider brushTargetSlider;

        [Tooltip("Shows the selected brush")]
        [SerializeField] private Image selectedBrushImage;
        [Tooltip("Inactive button that is copied once per brush in the brush settings")]
        [SerializeField] private PaletteButton brushButtonTemplate;

        private readonly List<PaletteButton> _brushButtons = new List<PaletteButton>();
        private Sprite _selectedBrushSprite;
        // Set while the sliders are being updated from the brush, so their change events don't write back
        private bool _syncing;

        /// <summary>Turns editing on, builds the brush buttons and lets the panel select its mode.</summary>
        protected virtual void OnEnable()
        {
            TerrainEditor editor = TerrainEditor.Instance;
            if (editor == null)
                return;

            editor.Brush.Changed += SyncFromBrush;
            editor.SetEditorEnabled(true);
            RebuildBrushButtons(editor.Brush);
            OnOpened(editor);
            SyncFromBrush();
        }

        /// <summary>Turns editing off.</summary>
        protected virtual void OnDisable()
        {
            TerrainEditor editor = TerrainEditor.Instance;
            if (editor == null)
                return;

            editor.Brush.Changed -= SyncFromBrush;
            OnClosed(editor);
            editor.SetEditorEnabled(false);
        }

        /// <summary>Destroys the sprite made for the selected brush image.</summary>
        private void OnDestroy()
        {
            if (_selectedBrushSprite != null)
                Destroy(_selectedBrushSprite);
        }

        /// <summary>Called when the panel opens, after editing was switched on. Select the panel's mode here.</summary>
        protected abstract void OnOpened(TerrainEditor editor);

        /// <summary>Called when the panel closes, before editing is switched off.</summary>
        protected virtual void OnClosed(TerrainEditor editor)
        {
        }

        /// <summary>Makes one button per brush texture.</summary>
        private void RebuildBrushButtons(TerrainBrush brush)
        {
            if (brushButtonTemplate == null)
                return;

            TerrainBrushSettings settings = brush.Settings;
            PaletteButton.Rebuild(brushButtonTemplate, _brushButtons, settings.Count, (button, i) =>
            {
                Texture2D texture = settings.GetTexture(i);
                button.Setup(i, texture, texture != null ? texture.name : null, SetBrush);
            });
        }

        /// <summary>
        /// Moves the sliders, the selected brush image and the brush button highlight to the brush's current values, so
        /// every panel shows the brush in use.
        /// </summary>
        private void SyncFromBrush()
        {
            if (_syncing)
                return;
            _syncing = true;
            TerrainBrush brush = TerrainEditor.Instance.Brush;
            if (brushStrengthSlider != null)
                brushStrengthSlider.value = brush.Strength;
            if (brushSizeSlider != null)
                brushSizeSlider.value = brush.SizeValue;
            if (brushTargetSlider != null)
                brushTargetSlider.value = brush.Target;
            ShowSelectedBrush(brush.CurrentTexture);
            for (int i = 0; i < _brushButtons.Count; i++)
                _brushButtons[i].SetSelectedWithoutNotify(i == brush.BrushIndex);
            _syncing = false;
        }

        /// <summary>Shows a brush texture in the selected brush image.</summary>
        private void ShowSelectedBrush(Texture2D texture)
        {
            if (selectedBrushImage == null || (_selectedBrushSprite != null && _selectedBrushSprite.texture == texture))
                return;

            if (_selectedBrushSprite != null)
                Destroy(_selectedBrushSprite);
            _selectedBrushSprite = texture != null
                ? Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f))
                : null;
            selectedBrushImage.sprite = _selectedBrushSprite;
        }

        /// <summary>Sets the flatten height / paint opacity / density (target slider).</summary>
        public void SetBrushTarget(float target)
        {
            TerrainEditor.Instance.SetBrushTarget(target);
        }

        /// <summary>Sets the brush strength (strength slider).</summary>
        public void SetBrushStrength(float strength)
        {
            TerrainEditor.Instance.SetBrushStrength(strength);
        }

        /// <summary>Sets the brush size (size slider).</summary>
        public void SetBrushSize(float size)
        {
            TerrainEditor.Instance.SetBrushSize(size);
        }

        /// <summary>Selects a brush shape (brush buttons).</summary>
        public void SetBrush(int brushIndex)
        {
            TerrainEditor.Instance.SetBrush(brushIndex);
        }
    }
}
