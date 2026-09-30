using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RuntimeTerrainEditor.Demo
{
    /// <summary>
    /// Shows a slider's value in a label, with one decimal.
    /// </summary>
    [RequireComponent(typeof(Slider))]
    public class SliderLabel : MonoBehaviour
    {
        [Tooltip("The slider to show (the one on this object)")]
        [SerializeField] private Slider slider;
        [Tooltip("Label that shows the value")]
        [SerializeField] private TMP_Text label;

        /// <summary>Picks up the slider on this object in the editor.</summary>
        private void OnValidate()
        {
            slider = GetComponent<Slider>();
        }

        /// <summary>Starts following the slider and shows its current value.</summary>
        private void Start()
        {
            if (slider == null)
                slider = GetComponent<Slider>();

            slider.onValueChanged.AddListener(HandleValueChanged);
            HandleValueChanged(slider.value);
        }

        /// <summary>Stops following the slider.</summary>
        private void OnDestroy()
        {
            if (slider != null)
                slider.onValueChanged.RemoveListener(HandleValueChanged);
        }

        /// <summary>Writes the value into the label.</summary>
        private void HandleValueChanged(float value)
        {
            if (label != null)
                label.text = value.ToString("F1");
        }
    }
}
