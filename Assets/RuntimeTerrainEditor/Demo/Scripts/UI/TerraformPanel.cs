namespace RuntimeTerrainEditor.Demo
{
    /// <summary>
    /// UI panel for Raise/Lower, Smooth, Flatten and Holes. Remembers what the two-way tools do (raise or lower, dig or
    /// fill; chosen with the switches under them), also while another tool is selected.
    /// </summary>
    public class TerraformPanel : TerrainPanel
    {
        private DeformMode _mode = DeformMode.Raise;
        private DeformMode _heightMode = DeformMode.Raise;
        private DeformMode _holeMode = DeformMode.DigHoles;

        /// <summary>Selects the tool that was in use when the panel was last open.</summary>
        protected override void OnOpened(TerrainEditor editor)
        {
            editor.SetDeformMode(_mode);
        }

        /// <summary>Selects the Raise/Lower tool when its toggle turns on, raising or lowering as its switch says.</summary>
        public void SelectRaiseLower(bool isOn)
        {
            if (isOn)
                SetDeformMode(_heightMode);
        }

        /// <summary>Selects the Flatten tool when its toggle turns on.</summary>
        public void SelectFlatten(bool isOn)
        {
            if (isOn)
                SetDeformMode(DeformMode.Flatten);
        }

        /// <summary>Selects the Smooth tool when its toggle turns on.</summary>
        public void SelectSmooth(bool isOn)
        {
            if (isOn)
                SetDeformMode(DeformMode.Smooth);
        }

        /// <summary>Selects the Holes tool when its toggle turns on, digging or filling as its switch says.</summary>
        public void SelectHoles(bool isOn)
        {
            if (isOn)
                SetDeformMode(_holeMode);
        }

        /// <summary>
        /// What a two-way tool does without Invert held: Raise or Lower for the Raise/Lower tool, DigHoles or FillHoles
        /// for the Holes tool.
        /// </summary>
        /// <param name="toolMode">Either mode of the tool.</param>
        public DeformMode GetToolMode(DeformMode toolMode)
        {
            return IsHoleMode(toolMode) ? _holeMode : _heightMode;
        }

        /// <summary>
        /// Chooses what a two-way tool does (e.g. Lower, or FillHoles); switches right away when that tool is selected.
        /// </summary>
        public void SetToolMode(DeformMode mode)
        {
            if (IsHoleMode(mode))
                _holeMode = mode;
            else if (mode == DeformMode.Raise || mode == DeformMode.Lower)
                _heightMode = mode;
            else
                return;

            if (_mode == mode || _mode == mode.GetOpposite())
                SetDeformMode(mode);
        }

        /// <summary>Flattens the whole terrain back to its starting state.</summary>
        public void ResetTerrain()
        {
            TerrainEditor.Instance.ResetTerrain();
        }

        /// <summary>Remembers the tool and tells the editor.</summary>
        private void SetDeformMode(DeformMode mode)
        {
            _mode = mode;
            TerrainEditor.Instance.SetDeformMode(mode);
        }

        /// <summary>True for the modes of the Holes tool.</summary>
        private static bool IsHoleMode(DeformMode mode)
        {
            return mode == DeformMode.DigHoles || mode == DeformMode.FillHoles;
        }
    }
}
