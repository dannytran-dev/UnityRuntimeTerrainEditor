using RuntimeTerrainEditor.Sculpting;
using UnityEngine;

namespace RuntimeTerrainEditor
{
    /// <summary>
    /// Finds where the pointer (mouse, pen or touch) is on the terrain. It follows the pointer ray over the heightmaps of all
    /// tiles instead of raycasting colliders, so other colliders never block the brush, freshly sculpted heights count
    /// right away and the brush can reach into holes to fill them.
    /// </summary>
    public class TerrainMouseSelection : MonoBehaviour
    {
        [Tooltip("Camera the mouse ray comes from. Uses the camera tagged MainCamera when empty.")]
        [SerializeField] private Camera raycastCamera;
        [Tooltip("Longest ray distance in meters")]
        [SerializeField] private float maxDistance = 1000.0f;

        private TerrainSurface _surface;

        /// <summary>True when the last raycast hit the terrain.</summary>
        public bool IsHittingTerrain { get; private set; }

        /// <summary>Where the last raycast hit the terrain (only valid while <see cref="IsHittingTerrain"/>).</summary>
        public Vector3 HitPoint { get; private set; }

        /// <summary>The camera rays come from; falls back to the camera tagged MainCamera.</summary>
        public Camera RaycastCamera
        {
            get
            {
                if (raycastCamera == null)
                    raycastCamera = Camera.main;
                return raycastCamera;
            }
            set => raycastCamera = value;
        }

        /// <summary>Connects to the terrain surface.</summary>
        public void Init(TerrainSurface surface)
        {
            _surface = surface;
        }

        /// <summary>Casts a ray from a screen position (mouse, pen or touch) against the terrain.</summary>
        public void Raycast(Vector2 screenPosition)
        {
            Camera camera = RaycastCamera;
            if (_surface == null || camera == null)
            {
                IsHittingTerrain = false;
                return;
            }

            Ray ray = camera.ScreenPointToRay(screenPosition);
            IsHittingTerrain = _surface.Raycast(ray, maxDistance, out Vector3 point);
            HitPoint = point;
        }

        /// <summary>Forgets the last hit, e.g. while the pointer is over the UI.</summary>
        public void ClearHit()
        {
            IsHittingTerrain = false;
        }

        /// <summary>
        /// Where the ray from a screen position crosses a level plane at a world height, e.g. to point at the empty spots
        /// next to the terrain where tiles can be added.
        /// </summary>
        /// <returns>False when the ray doesn't reach the plane within the longest ray distance, or there is no camera.</returns>
        public bool RaycastHeight(Vector2 screenPosition, float height, out Vector3 point)
        {
            point = default;
            Camera camera = RaycastCamera;
            if (camera == null)
                return false;

            Ray ray = camera.ScreenPointToRay(screenPosition);
            Plane plane = new Plane(Vector3.up, new Vector3(0f, height, 0f));
            if (!plane.Raycast(ray, out float distance) || distance > maxDistance)
                return false;
            point = ray.GetPoint(distance);
            return true;
        }
    }
}
