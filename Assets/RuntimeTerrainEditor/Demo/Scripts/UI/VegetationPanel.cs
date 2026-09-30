using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RuntimeTerrainEditor.Demo
{
    /// <summary>
    /// UI panel for placing trees and painting grass, with one button per tree type and grass type plus an eraser
    /// for each that clears every type. All of these buttons share one ToggleGroup, so one of them is selected at a time.
    /// The erase switch makes the tree and grass buttons erase just their own type. The target slider sets the density.
    /// Holding Invert (Shift) flips between painting and erasing.
    /// </summary>
    public class VegetationPanel : TerrainPanel
    {
        [Header("Trees and Grass")]
        [Tooltip("Inactive button that is copied once per tree type")]
        [SerializeField] private PaletteButton treeButtonTemplate;
        [Tooltip("Inactive button that is copied once per grass (detail) type")]
        [SerializeField] private PaletteButton grassButtonTemplate;
        [Tooltip("Optional. When on, the selected tree or grass button erases that type instead of painting it")]
        [SerializeField] private Toggle eraseSwitch;

        private readonly List<PaletteButton> _treeButtons = new List<PaletteButton>();
        private readonly List<PaletteButton> _grassButtons = new List<PaletteButton>();
        // Matches the Tree Eraser button, which starts selected
        private DeformMode _mode = DeformMode.Trees;
        private bool _eraserSelected = true;
        private bool _eraseSelectedType;

        /// <summary>Shows the erase switch flipped while Invert (Shift) is held, like the raise/lower switch.</summary>
        private void Update()
        {
            TerrainEditor editor = TerrainEditor.Instance;
            if (eraseSwitch != null && editor != null)
                eraseSwitch.SetIsOnWithoutNotify(_eraseSelectedType != editor.Controls.Invert.IsPressed());
        }

        /// <summary>Makes the tree and grass buttons and applies the current selection.</summary>
        protected override void OnOpened(TerrainEditor editor)
        {
            RebuildButtons(editor);
            ApplySelection();
            if (eraseSwitch != null)
                eraseSwitch.onValueChanged.AddListener(HandleEraseSwitchChanged);
        }

        /// <summary>Stops listening to the erase switch and turns erasing off again.</summary>
        protected override void OnClosed(TerrainEditor editor)
        {
            if (eraseSwitch != null)
                eraseSwitch.onValueChanged.RemoveListener(HandleEraseSwitchChanged);
            editor.SetVegetationErase(false);
            editor.SetEraseSelectedTypeOnly(false);
        }

        /// <summary>Selects a tree type to place.</summary>
        public void SelectTree(int treeType)
        {
            _mode = DeformMode.Trees;
            _eraserSelected = false;
            TerrainEditor.Instance.SetTreeType(treeType);
            ApplySelection();
        }

        /// <summary>Selects a grass type to paint.</summary>
        public void SelectGrass(int detailType)
        {
            _mode = DeformMode.Details;
            _eraserSelected = false;
            TerrainEditor.Instance.SetDetailType(detailType);
            ApplySelection();
        }

        /// <summary>Selects the tree eraser, which removes every tree type, when its toggle turns on.</summary>
        public void SelectTreeEraser(bool isOn)
        {
            if (!isOn)
                return;
            _mode = DeformMode.Trees;
            _eraserSelected = true;
            ApplySelection();
        }

        /// <summary>Selects the grass eraser, which removes every grass type, when its toggle turns on.</summary>
        public void SelectGrassEraser(bool isOn)
        {
            if (!isOn)
                return;
            _mode = DeformMode.Details;
            _eraserSelected = true;
            ApplySelection();
        }

        /// <summary>Applies a click on the erase switch.</summary>
        private void HandleEraseSwitchChanged(bool isOn)
        {
            // Take the Invert key back out of what the switch showed
            _eraseSelectedType = isOn != TerrainEditor.Instance.Controls.Invert.IsPressed();
            ApplySelection();
        }

        /// <summary>
        /// Tells the editor the mode and how to erase: the erasers clear every type, and a selected tree or grass type
        /// erases only itself while the erase switch is on.
        /// </summary>
        private void ApplySelection()
        {
            TerrainEditor editor = TerrainEditor.Instance;
            editor.SetDeformMode(_mode);
            editor.SetVegetationErase(_eraserSelected || _eraseSelectedType);
            editor.SetEraseSelectedTypeOnly(!_eraserSelected);
        }

        /// <summary>Makes one button per tree type and grass type on the terrain.</summary>
        private void RebuildButtons(TerrainEditor editor)
        {
            TerrainVegetationPalette palette = editor.VegetationPalette;
            TerrainData data = editor.Surface.Tiles[0].Data; // tiles share their tree and grass types

            if (treeButtonTemplate != null)
            {
                TreePrototype[] trees = data.treePrototypes;
                PaletteButton.Rebuild(treeButtonTemplate, _treeButtons, trees.Length, (button, i) =>
                    button.Setup(i, palette != null ? palette.GetTreeIcon(i) : null, trees[i].prefab != null ? trees[i].prefab.name : null, SelectTree));
            }

            if (grassButtonTemplate != null)
            {
                DetailPrototype[] details = data.detailPrototypes;
                PaletteButton.Rebuild(grassButtonTemplate, _grassButtons, details.Length, (button, i) =>
                    button.Setup(i, palette != null ? palette.GetDetailIcon(i) : details[i].prototypeTexture,
                                 details[i].prototypeTexture != null ? details[i].prototypeTexture.name : null, SelectGrass));
            }
        }
    }
}
