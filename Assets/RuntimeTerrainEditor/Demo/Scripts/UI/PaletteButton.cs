using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RuntimeTerrainEditor.Demo
{
    /// <summary>
    /// A button for one brush, terrain layer, tree type or grass type. The panels copy an inactive template once per item.
    /// It is a Toggle in a ToggleGroup, so the selected item stays highlighted.
    /// </summary>
    [RequireComponent(typeof(Toggle))]
    public class PaletteButton : MonoBehaviour
    {
        [Tooltip("Image that shows the item's picture")]
        [SerializeField] private Image iconImage;

        private Toggle _toggle;
        private Sprite _sprite;
        private Action<int> _selectedCallback;

        /// <summary>Index of the item this button stands for.</summary>
        public int Index { get; private set; }

        /// <summary>Calls the selected callback with this button's index when its toggle turns on.</summary>
        private void Awake()
        {
            GetToggle().onValueChanged.AddListener(HandleValueChanged);
        }

        /// <summary>Destroys the sprite made for the icon.</summary>
        private void OnDestroy()
        {
            if (_sprite != null)
                Destroy(_sprite);
        }

        /// <summary>Sets the button up for a terrain layer, showing its diffuse texture.</summary>
        public void Setup(int index, TerrainLayer layer, Action<int> onSelected)
        {
            Setup(index, layer != null ? layer.diffuseTexture : null, layer != null ? layer.name : null, onSelected);
        }

        /// <summary>Sets the button up for an item: its index, picture, name (for the hierarchy) and click callback.</summary>
        public void Setup(int index, Texture2D icon, string label, Action<int> onSelected)
        {
            Index = index;
            _selectedCallback = onSelected;
            name = "Item " + index + (string.IsNullOrEmpty(label) ? string.Empty : " - " + label);
            SetIcon(icon);
        }

        /// <summary>Highlights the button or clears its highlight, without calling the selected callback.</summary>
        public void SetSelectedWithoutNotify(bool selected)
        {
            GetToggle().SetIsOnWithoutNotify(selected);
        }

        /// <summary>Shows a texture as the button's picture (none when null).</summary>
        public void SetIcon(Texture2D icon)
        {
            if (_sprite != null && _sprite.texture == icon)
                return;

            if (_sprite != null)
                Destroy(_sprite);
            _sprite = icon != null
                ? Sprite.Create(icon, new Rect(0.0f, 0.0f, icon.width, icon.height), new Vector2(0.5f, 0.5f), 100.0f)
                : null;
            iconImage.sprite = _sprite;
        }

        /// <summary>
        /// Keeps one active copy of the template per item next to the template, reusing existing copies. The copies share
        /// the template's ToggleGroup.
        /// </summary>
        public static void Rebuild(PaletteButton template, List<PaletteButton> buttons, int count, Action<PaletteButton, int> setup)
        {
            template.gameObject.SetActive(false);

            while (buttons.Count > count)
            {
                PaletteButton extra = buttons[buttons.Count - 1];
                buttons.RemoveAt(buttons.Count - 1);
                Destroy(extra.gameObject);
            }
            while (buttons.Count < count)
                buttons.Add(Instantiate(template, template.transform.parent, false));

            for (int i = 0; i < count; i++)
            {
                setup(buttons[i], i);
                buttons[i].gameObject.SetActive(true);
            }
        }

        /// <summary>Selects this button's item when the toggle turns on (turning off needs no action).</summary>
        private void HandleValueChanged(bool isOn)
        {
            if (isOn)
                _selectedCallback?.Invoke(Index);
        }

        /// <summary>The toggle on this object, looked up on first use (copies are set up before they wake).</summary>
        private Toggle GetToggle()
        {
            if (_toggle == null)
                _toggle = GetComponent<Toggle>();
            return _toggle;
        }
    }
}
