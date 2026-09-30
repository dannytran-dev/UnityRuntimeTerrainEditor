using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace RuntimeTerrainEditor
{
    /// <summary>
    /// Connects the terrain editor to its Input System actions, which live in an Input Action Asset
    /// (Input/TerrainEditorControls.inputactions by default). Change the bindings in that asset, point these fields at
    /// actions of your own, or rebind at runtime through the action properties. Point and Apply use the generic pointer,
    /// so mouse, pen and touch all work.
    /// </summary>
    [DisallowMultipleComponent]
    public class TerrainEditorInput : MonoBehaviour
    {
        // The actions asset that Reset picks up when the component is added in the editor
        private const string DefaultActionsAsset = "TerrainEditorControls";

        [Header("Actions")]
        [Tooltip("Screen position of the brush (Vector2)")]
        [SerializeField] private InputActionReference pointAction;
        [Tooltip("Hold to edit (button)")]
        [SerializeField] private InputActionReference applyAction;
        [Tooltip("Hold for the opposite action: lower instead of raise, fill holes instead of digging, erase trees and grass, remove tiles instead of adding (button)")]
        [SerializeField] private InputActionReference invertAction;
        [Tooltip("Undoes the last edit (button)")]
        [SerializeField] private InputActionReference undoAction;
        [Tooltip("Redoes the last undone edit (button)")]
        [SerializeField] private InputActionReference redoAction;
        [Tooltip("Shows or hides the brush preview (button)")]
        [SerializeField] private InputActionReference togglePreviewAction;
        [Tooltip("Each step changes the brush size by Size Step (axis: + grows, - shrinks)")]
        [SerializeField] private InputActionReference brushSizeAction;
        [Tooltip("Each step rotates the brush by Rotation Step (axis)")]
        [SerializeField] private InputActionReference brushRotationAction;

        [Header("Steps")]
        [Tooltip("Brush size slider change per size step (the slider runs 0-1)")]
        [SerializeField] private float sizeStep = 0.05f;
        [Tooltip("Degrees per rotation step")]
        [SerializeField] private float rotationStep = 15f;

        private int _sizeSteps;
        private int _rotationSteps;
        // Frame of the last size or rotation step that was actually taken
        private int _lastStepFrame = -1;

        /// <summary>Screen position of the pointer (mouse, pen or touch).</summary>
        public InputAction Point => GetAction(pointAction);

        /// <summary>Held while editing.</summary>
        public InputAction Apply => GetAction(applyAction);

        /// <summary>Held for the opposite action (see <see cref="TerrainEditor.ActiveMode"/>), or to erase trees and grass.</summary>
        public InputAction Invert => GetAction(invertAction);

        /// <summary>Undoes the last edit.</summary>
        public InputAction Undo => GetAction(undoAction);

        /// <summary>Redoes the last undone edit.</summary>
        public InputAction Redo => GetAction(redoAction);

        /// <summary>Shows or hides the brush preview.</summary>
        public InputAction TogglePreview => GetAction(togglePreviewAction);

        /// <summary>Grows (+) or shrinks (-) the brush one step per press or scroll notch.</summary>
        public InputAction BrushSize => GetAction(brushSizeAction);

        /// <summary>Rotates the brush one step per press or scroll notch.</summary>
        public InputAction BrushRotation => GetAction(brushRotationAction);

        /// <summary>Brush size slider change per size step (the slider runs 0-1).</summary>
        public float SizeStep => sizeStep;

        /// <summary>Degrees per rotation step.</summary>
        public float RotationStep => rotationStep;

        /// <summary>
        /// True when the brush size or rotation changed by a step this frame (e.g. Ctrl or Alt + scroll), so other scroll
        /// users such as a camera zoom can leave that input alone. Unlike the actions' WasPerformedThisFrame, this ignores
        /// plain scrolls: the pass-through actions also perform, with 0, when the scroll wheel moves without the modifier.
        /// </summary>
        public bool BrushStepThisFrame => _lastStepFrame == Time.frameCount;

        /// <summary>Current screen position of the pointer.</summary>
        public Vector2 PointerPosition => Point.ReadValue<Vector2>();

        /// <summary>True when every action is assigned; the editor can't run without them.</summary>
        public bool HasAllActions
        {
            get
            {
                foreach (InputAction action in GetActions())
                {
                    if (action == null)
                        return false;
                }
                return true;
            }
        }

        /// <summary>Points the fields at the default actions asset when the component is added in the editor.</summary>
        private void Reset()
        {
#if UNITY_EDITOR
            AssignDefaultActions();
#endif
        }

        /// <summary>Turns the actions on and starts counting size and rotation steps.</summary>
        private void OnEnable()
        {
            foreach (InputAction action in GetActions())
            {
                if (action != null)
                    action.Enable();
            }
            if (BrushSize != null)
                BrushSize.performed += HandleBrushSize;
            if (BrushRotation != null)
                BrushRotation.performed += HandleBrushRotation;
        }

        /// <summary>Stops counting steps and turns the actions off.</summary>
        private void OnDisable()
        {
            if (BrushSize != null)
                BrushSize.performed -= HandleBrushSize;
            if (BrushRotation != null)
                BrushRotation.performed -= HandleBrushRotation;
            foreach (InputAction action in GetActions())
            {
                if (action != null)
                    action.Disable();
            }
        }

        /// <summary>Returns the size steps since the last call (+ bigger, - smaller) and resets the count.</summary>
        public int ConsumeSizeSteps()
        {
            int steps = _sizeSteps;
            _sizeSteps = 0;
            return steps;
        }

        /// <summary>Returns the rotation steps since the last call and resets the count.</summary>
        public int ConsumeRotationSteps()
        {
            int steps = _rotationSteps;
            _rotationSteps = 0;
            return steps;
        }

        /// <summary>All actions of this component (null where a reference is missing).</summary>
        private InputAction[] GetActions()
        {
            return new[] { Point, Apply, Invert, Undo, Redo, TogglePreview, BrushSize, BrushRotation };
        }

        /// <summary>Counts one size step per key press or scroll notch.</summary>
        private void HandleBrushSize(InputAction.CallbackContext context)
        {
            _sizeSteps += TakeStep(context.ReadValue<float>());
        }

        /// <summary>Counts one rotation step per key press or scroll notch.</summary>
        private void HandleBrushRotation(InputAction.CallbackContext context)
        {
            _rotationSteps += TakeStep(context.ReadValue<float>());
        }

        /// <summary>Turns an action value into a step and remembers the frame when it is one (see <see cref="BrushStepThisFrame"/>).</summary>
        private int TakeStep(float value)
        {
            int step = ToStep(value);
            if (step != 0)
                _lastStepFrame = Time.frameCount;
            return step;
        }

        /// <summary>The action a reference points at, or null when the reference is empty.</summary>
        private static InputAction GetAction(InputActionReference reference)
        {
            return reference != null ? reference.action : null;
        }

        /// <summary>Key presses and scroll notches both arrive as one non-zero value; this turns it into -1, 0 or +1.</summary>
        private static int ToStep(float value)
        {
            return Mathf.Abs(value) < 0.01f ? 0 : (int)Mathf.Sign(value);
        }

#if UNITY_EDITOR
        /// <summary>Fills every empty field with the action of the same name from the default actions asset.</summary>
        private void AssignDefaultActions()
        {
            string[] guids = AssetDatabase.FindAssets(DefaultActionsAsset + " t:InputActionAsset");
            if (guids.Length == 0)
                return;

            Dictionary<string, InputActionReference> references = new Dictionary<string, InputActionReference>();
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(guids[0])))
            {
                if (asset is InputActionReference reference && reference.action != null)
                    references[reference.action.name] = reference;
            }

            pointAction = pointAction != null ? pointAction : Find(references, "Point");
            applyAction = applyAction != null ? applyAction : Find(references, "Apply");
            invertAction = invertAction != null ? invertAction : Find(references, "Invert");
            undoAction = undoAction != null ? undoAction : Find(references, "Undo");
            redoAction = redoAction != null ? redoAction : Find(references, "Redo");
            togglePreviewAction = togglePreviewAction != null ? togglePreviewAction : Find(references, "Toggle Brush Preview");
            brushSizeAction = brushSizeAction != null ? brushSizeAction : Find(references, "Brush Size");
            brushRotationAction = brushRotationAction != null ? brushRotationAction : Find(references, "Brush Rotation");
        }

        /// <summary>The reference to an action by name, or null.</summary>
        private static InputActionReference Find(Dictionary<string, InputActionReference> references, string actionName)
        {
            references.TryGetValue(actionName, out InputActionReference reference);
            return reference;
        }
#endif
    }
}
