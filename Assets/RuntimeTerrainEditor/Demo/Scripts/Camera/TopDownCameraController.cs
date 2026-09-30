using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace RuntimeTerrainEditor.Demo
{
    /// <summary>
    /// Demo camera driven by the Camera actions in Demo/Input/DemoControls.inputactions: WASD / arrow keys to move, Q/E
    /// to move down/up, middle mouse to pan, right mouse to look, scroll wheel to zoom and Left Shift to move faster.
    /// Movement, pan and zoom speed up with height. The camera stays within a margin around the edited terrain, which
    /// grows when tiles are added.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class TopDownCameraController : MonoBehaviour
    {
        [Header("Actions")]
        [Tooltip("Moves along the ground (Vector2: forward/back, left/right)")]
        [SerializeField] private InputActionReference moveAction;
        [Tooltip("Moves down (-) or up (+) (axis)")]
        [SerializeField] private InputActionReference elevateAction;
        [Tooltip("Hold to move faster (button)")]
        [SerializeField] private InputActionReference fastAction;
        [Tooltip("Hold to pan with the pointer (button)")]
        [SerializeField] private InputActionReference panAction;
        [Tooltip("Hold to look around with the pointer (button)")]
        [SerializeField] private InputActionReference lookAction;
        [Tooltip("Pointer movement in pixels, used while panning or looking (Vector2)")]
        [SerializeField] private InputActionReference pointerDeltaAction;
        [Tooltip("Zooms along the view direction, about 1 per scroll wheel notch (axis)")]
        [SerializeField] private InputActionReference zoomAction;

        [Header("Movement Limits")]
        [Tooltip("Lowest and highest camera height")]
        [SerializeField] private Vector2 heightLimit = new Vector2(2f, 300f);
        [Tooltip("How far the camera can move past the edges of the terrain, in meters")]
        [SerializeField, Min(0f)] private float edgeMargin = 200f;

        [Header("Speeds")]
        [Tooltip("Meters per second")]
        [SerializeField] private float moveSpeed = 30.0f;
        [Tooltip("Meters per second while the Fast action is held")]
        [SerializeField] private float fastMoveSpeed = 90.0f;
        [Tooltip("How far the camera pans per pixel of pointer movement")]
        [SerializeField] private float panSensitivity = 1.0f;
        [Tooltip("Degrees per pixel of pointer movement")]
        [SerializeField] private float lookSensitivity = 0.15f;
        [Tooltip("How far the camera moves per scroll wheel notch")]
        [SerializeField] private float zoomSensitivity = 1.0f;
        [Tooltip("Movement, pan and zoom scale with height; this is the height where the scale is 1")]
        [SerializeField] private float referenceHeight = 60.0f;

        private float _pitch;
        private float _yaw;
        // A drag keeps going while the cursor passes over the UI, but only starts outside it
        private bool _panning;
        private bool _looking;

        /// <summary>Set to false to stop all camera controls, e.g. while a menu is open.</summary>
        public static bool IsEnabled { get; set; } = true;

        /// <summary>Turns the camera actions on.</summary>
        private void OnEnable()
        {
            foreach (InputActionReference reference in GetActions())
            {
                if (reference != null)
                    reference.action.Enable();
            }
        }

        /// <summary>Turns the camera actions off.</summary>
        private void OnDisable()
        {
            foreach (InputActionReference reference in GetActions())
            {
                if (reference != null)
                    reference.action.Disable();
            }
        }

        /// <summary>Takes over the camera's starting rotation.</summary>
        private void Start()
        {
            Vector3 euler = transform.eulerAngles;
            _pitch = euler.x > 180f ? euler.x - 360f : euler.x;
            _yaw = euler.y;
        }

        /// <summary>Moves, pans, rotates and zooms from the camera actions.</summary>
        private void Update()
        {
            if (!IsEnabled)
                return;

            bool pointerOverUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            float heightScale = Mathf.Max(0.2f, transform.position.y / referenceHeight);

            HandleMovement(heightScale);
            HandlePointer(pointerOverUI, heightScale);
            ClampToLimits();
        }

        /// <summary>Moves along the ground plane, and up or down, from the Move and Elevate actions.</summary>
        private void HandleMovement(float heightScale)
        {
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (forward.sqrMagnitude < 1e-4f)
                forward = Vector3.ProjectOnPlane(transform.up, Vector3.up); // looking straight down
            forward.Normalize();
            Vector3 right = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;

            Vector2 input = ReadValue<Vector2>(moveAction);
            Vector3 move = forward * input.y + right * input.x + Vector3.up * ReadValue<float>(elevateAction);
            if (move.sqrMagnitude <= 0f)
                return;

            float speed = IsPressed(fastAction) ? fastMoveSpeed : moveSpeed;
            transform.position += Vector3.ClampMagnitude(move, 1f) * (speed * heightScale * Time.unscaledDeltaTime);
        }

        /// <summary>Pans while Pan is held, looks around while Look is held and zooms with the Zoom action.</summary>
        private void HandlePointer(bool pointerOverUI, float heightScale)
        {
            Vector2 delta = ReadValue<Vector2>(pointerDeltaAction);

            if (WasPressedThisFrame(panAction) && !pointerOverUI)
                _panning = true;
            if (!IsPressed(panAction))
                _panning = false;
            if (WasPressedThisFrame(lookAction) && !pointerOverUI)
                _looking = true;
            if (!IsPressed(lookAction))
                _looking = false;

            if (_panning)
            {
                float panScale = panSensitivity * 0.1f * heightScale;
                transform.position -= (transform.right * delta.x + transform.up * delta.y) * panScale;
            }

            if (_looking)
            {
                _yaw += delta.x * lookSensitivity;
                _pitch = Mathf.Clamp(_pitch - delta.y * lookSensitivity, -89f, 89f);
                transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            }

            // Ctrl/Alt + scroll resize and rotate the terrain brush; the camera leaves those scrolls alone
            if (pointerOverUI || IsBrushScroll())
                return;
            float scroll = ReadValue<float>(zoomAction);
            if (Mathf.Abs(scroll) > 0.001f)
                transform.position += transform.forward * (scroll * zoomSensitivity * 10f * heightScale);
        }

        /// <summary>True when this frame's input changed the terrain brush size or rotation.</summary>
        private static bool IsBrushScroll()
        {
            TerrainEditor editor = TerrainEditor.Instance;
            return editor != null && editor.Controls != null && editor.Controls.BrushStepThisFrame;
        }

        /// <summary>Keeps the camera between the height limits and within the margin around the terrain.</summary>
        private void ClampToLimits()
        {
            Vector3 position = transform.position;
            position.y = Mathf.Clamp(position.y, heightLimit.x, heightLimit.y);

            TerrainEditor editor = TerrainEditor.Instance;
            if (editor != null && editor.Surface != null)
            {
                Bounds bounds = editor.Surface.WorldBounds;
                position.x = Mathf.Clamp(position.x, bounds.min.x - edgeMargin, bounds.max.x + edgeMargin);
                position.z = Mathf.Clamp(position.z, bounds.min.z - edgeMargin, bounds.max.z + edgeMargin);
            }
            transform.position = position;
        }

        /// <summary>All action references of this component.</summary>
        private InputActionReference[] GetActions()
        {
            return new[] { moveAction, elevateAction, fastAction, panAction, lookAction, pointerDeltaAction, zoomAction };
        }

        /// <summary>The action's current value, or the default when the reference is empty.</summary>
        private static T ReadValue<T>(InputActionReference reference) where T : struct
        {
            return reference != null ? reference.action.ReadValue<T>() : default;
        }

        /// <summary>True while the action is held (false when the reference is empty).</summary>
        private static bool IsPressed(InputActionReference reference)
        {
            return reference != null && reference.action.IsPressed();
        }

        /// <summary>True in the frame the action was pressed (false when the reference is empty).</summary>
        private static bool WasPressedThisFrame(InputActionReference reference)
        {
            return reference != null && reference.action.WasPressedThisFrame();
        }
    }
}
