using UnityEngine;
using UnityEngine.UI;

namespace RuntimeTerrainEditor.Demo
{
    /// <summary>
    /// Makes a Toggle look like a switch: the knob sits on the right while the toggle is on and on the left while it is off.
    /// It follows isOn every frame, so it also works when code sets the toggle without notifying.
    /// </summary>
    [RequireComponent(typeof(Toggle))]
    public class ToggleSwitch : MonoBehaviour
    {
        [Tooltip("The knob that moves between the ends of the track")]
        [SerializeField] private RectTransform knob;
        [Tooltip("Gap between the knob and the end of the track")]
        [SerializeField] private float padding = 2f;

        private Toggle _toggle;

        /// <summary>Finds the toggle.</summary>
        private void Awake()
        {
            _toggle = GetComponent<Toggle>();
        }

        /// <summary>Moves the knob to the side that matches the toggle.</summary>
        private void LateUpdate()
        {
            if (knob == null)
                return;

            float side = _toggle.isOn ? 1f : 0f;
            knob.anchorMin = new Vector2(side, 0.5f);
            knob.anchorMax = new Vector2(side, 0.5f);
            knob.pivot = new Vector2(side, 0.5f);
            knob.anchoredPosition = new Vector2(_toggle.isOn ? -padding : padding, 0f);
        }
    }
}
