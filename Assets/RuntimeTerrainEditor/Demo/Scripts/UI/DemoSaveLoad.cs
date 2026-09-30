using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RuntimeTerrainEditor.Demo
{
    /// <summary>
    /// Demo save slot: Save and Load buttons (and the Save Slot actions, F5 / F9 by default) write and read one file in
    /// Application.persistentDataPath.
    /// Shows how to use <see cref="TerrainEditor.SaveToFile"/> and <see cref="TerrainEditor.LoadFromFile"/>.
    /// </summary>
    public class DemoSaveLoad : MonoBehaviour
    {
        [Header("Actions")]
        [Tooltip("Saves the terrain (button)")]
        [SerializeField] private InputActionReference saveAction;
        [Tooltip("Loads the terrain (button)")]
        [SerializeField] private InputActionReference loadAction;

        [Header("Save Slot")]
        [Tooltip("File name of the save, without extension")]
        [SerializeField] private string slotName = "DemoTerrain";
        [Tooltip("Optional. Shows what happened for a moment.")]
        [SerializeField] private TMP_Text status;
        [Tooltip("Seconds the status message stays up")]
        [SerializeField] private float statusSeconds = 2.5f;

        private float _clearStatusAt;

        /// <summary>Where the save file goes.</summary>
        public string SavePath => TerrainEditor.GetSavePath(slotName);

        /// <summary>Turns the save and load actions on.</summary>
        private void OnEnable()
        {
            if (saveAction != null)
                saveAction.action.Enable();
            if (loadAction != null)
                loadAction.action.Enable();
        }

        /// <summary>Turns the save and load actions off.</summary>
        private void OnDisable()
        {
            if (saveAction != null)
                saveAction.action.Disable();
            if (loadAction != null)
                loadAction.action.Disable();
        }

        /// <summary>Clears the status text.</summary>
        private void Start()
        {
            ShowStatus(string.Empty);
        }

        /// <summary>Handles the save and load shortcuts and hides the status message after a while.</summary>
        private void Update()
        {
            if (saveAction != null && saveAction.action.WasPerformedThisFrame())
                Save();
            else if (loadAction != null && loadAction.action.WasPerformedThisFrame())
                Load();

            if (_clearStatusAt > 0f && Time.unscaledTime >= _clearStatusAt)
                ShowStatus(string.Empty);
        }

        /// <summary>Saves the terrain to the slot.</summary>
        public void Save()
        {
            TerrainEditor editor = TerrainEditor.Instance;
            if (editor == null)
                return;
            editor.SaveToFile(SavePath);
            ShowStatus("Terrain saved");
            Debug.Log("DemoSaveLoad: saved to " + SavePath);
        }

        /// <summary>Loads the terrain from the slot.</summary>
        public void Load()
        {
            TerrainEditor editor = TerrainEditor.Instance;
            if (editor == null)
                return;
            bool loaded = editor.LoadFromFile(SavePath);
            ShowStatus(loaded ? "Terrain loaded" : "No saved terrain yet");
        }

        /// <summary>Shows a message for Status Seconds (an empty message clears it).</summary>
        private void ShowStatus(string message)
        {
            if (status != null)
                status.text = message;
            _clearStatusAt = string.IsNullOrEmpty(message) ? 0f : Time.unscaledTime + statusSeconds;
        }
    }
}
