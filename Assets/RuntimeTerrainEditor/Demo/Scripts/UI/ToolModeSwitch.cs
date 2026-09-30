using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RuntimeTerrainEditor.Demo
{
    /// <summary>
    /// The switch of one of the <see cref="TerraformPanel"/>'s two-way tools: Raise/Lower, or Dig/Fill holes. It is on for
    /// On Mode and off for its opposite. While its tool is active it also shows the Invert key (Shift) flipping it.
    /// Showing that would fire the toggle's onValueChanged, so this listens for a pointer click instead.
    /// </summary>
    [RequireComponent(typeof(Toggle))]
    public class ToolModeSwitch : MonoBehaviour, IPointerClickHandler
    {
        [Tooltip("The mode shown with the switch on; off is the opposite mode (Raise / Lower, Fill Holes / Dig Holes)")]
        [SerializeField] private DeformMode onMode = DeformMode.Raise;

        private Toggle _toggle;
        private TerraformPanel _panel;

        /// <summary>Finds the toggle and the panel it belongs to.</summary>
        private void Awake()
        {
            _toggle = GetComponent<Toggle>();
            _panel = GetComponentInParent<TerraformPanel>(true);
            if (_panel == null)
                Debug.LogError("ToolModeSwitch: needs a TerraformPanel on a parent.", this);
        }

        /// <summary>Shows the panel's choice for this tool, flipped while Invert is held during that tool.</summary>
        private void Update()
        {
            TerrainEditor editor = TerrainEditor.Instance;
            if (_panel == null || editor == null)
                return;

            DeformMode shown = _panel.GetToolMode(onMode);
            DeformMode active = editor.ActiveMode;
            if (active == onMode || active == onMode.GetOpposite())
                shown = active;
            _toggle.SetIsOnWithoutNotify(shown == onMode);
        }

        /// <summary>Swaps the tool between its two modes.</summary>
        public void OnPointerClick(PointerEventData eventData)
        {
            if (_panel != null)
                _panel.SetToolMode(_panel.GetToolMode(onMode).GetOpposite());
        }
    }
}
