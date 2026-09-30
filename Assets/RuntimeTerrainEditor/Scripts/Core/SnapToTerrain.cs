using RuntimeTerrainEditor.Sculpting;
using UnityEngine;

namespace RuntimeTerrainEditor
{
    /// <summary>
    /// Keeps an object standing on the terrain: whenever an edit changes the heights under it, it moves back onto the
    /// ground. Put it on props, buildings or spawn points placed on an editable terrain.
    /// </summary>
    public class SnapToTerrain : MonoBehaviour
    {
        [Tooltip("Height above the ground in meters")]
        [SerializeField] private float heightOffset;
        [Tooltip("Tilt the object to match the slope of the ground")]
        [SerializeField] private bool alignToSlope;
        [Tooltip("Snap once when play starts too")]
        [SerializeField] private bool snapOnStart = true;

        private TerrainEditor _editor;

        /// <summary>Starts listening for terrain edits and snaps once if Snap On Start is set.</summary>
        private void Start()
        {
            _editor = TerrainEditor.Instance;
            if (_editor != null)
                _editor.TerrainChanged += HandleTerrainChanged;
            if (snapOnStart)
                Snap();
        }

        /// <summary>Stops listening for terrain edits.</summary>
        private void OnDestroy()
        {
            if (_editor != null)
                _editor.TerrainChanged -= HandleTerrainChanged;
        }

        /// <summary>
        /// Snaps when the heights under the object changed. Not named OnTerrainChanged: Unity reserves that name for its
        /// own terrain message.
        /// </summary>
        private void HandleTerrainChanged(TerrainChange change)
        {
            if (change.Includes(TerrainChannels.Heights) && change.Covers(transform.position))
                Snap();
        }

        /// <summary>Moves the object onto the ground right now (of whichever terrain tile it stands on).</summary>
        public void Snap()
        {
            if (_editor != null && _editor.Surface != null)
            {
                TerrainSurface surface = _editor.Surface;
                Vector3 position = transform.position;
                position.y = surface.SampleHeight(position) + heightOffset;
                transform.position = position;
                if (alignToSlope)
                    AlignTo(surface.SampleNormal(position));
                return;
            }

            // Without an editor, snap to the terrain Unity considers active
            Terrain terrain = Terrain.activeTerrain;
            if (terrain == null)
                return;

            Vector3 point = transform.position;
            Vector3 terrainPosition = terrain.GetPosition();
            point.y = terrain.SampleHeight(point) + terrainPosition.y + heightOffset;
            transform.position = point;

            if (alignToSlope)
            {
                Vector3 size = terrain.terrainData.size;
                AlignTo(terrain.terrainData.GetInterpolatedNormal((point.x - terrainPosition.x) / size.x, (point.z - terrainPosition.z) / size.z));
            }
        }

        /// <summary>Tilts the object so its up axis follows a ground normal.</summary>
        private void AlignTo(Vector3 normal)
        {
            transform.rotation = Quaternion.FromToRotation(transform.up, normal) * transform.rotation;
        }
    }
}
