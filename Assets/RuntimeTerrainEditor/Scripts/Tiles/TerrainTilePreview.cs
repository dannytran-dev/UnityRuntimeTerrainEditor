using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RuntimeTerrainEditor
{
    /// <summary>
    /// Shows where terrain tiles can be added (an outlined square with a plus on every free slot, the one under the
    /// pointer highlighted) and which tile would be removed (an outlined box around it). <see cref="TerrainEditor"/> drives
    /// it in the AddTiles and RemoveTiles modes. Optional: tiles can be added and removed without it.
    /// </summary>
    public class TerrainTilePreview : MonoBehaviour
    {
        private const string ShaderName = "RuntimeTerrainEditor/TilePreview";

        private static readonly int ShowPlusId = Shader.PropertyToID("_ShowPlus");
        private static readonly int ZTestId = Shader.PropertyToID("_ZTest");

        [Tooltip("Material with the RuntimeTerrainEditor/TilePreview shader")]
        [SerializeField] private Material material;
        [Tooltip("Color of the free slots")]
        [SerializeField] private Color addColor = Color.white;
        [Tooltip("Color of the box around the tile that would be removed")]
        [SerializeField] private Color removeColor = new Color(1f, 0.32f, 0.25f, 1f);
        [Tooltip("Opacity of the free slots that aren't under the pointer")]
        [SerializeField, Range(0f, 1f)] private float idleOpacity = 0.45f;
        [Tooltip("Meters the slots float above the height a new tile starts at, so the ground doesn't cover them")]
        [SerializeField, Min(0f)] private float lift = 0.3f;

        private PreviewMesh _slots;
        private PreviewMesh _box;

        /// <summary>Creates the slot and box meshes, each with its own copy of the material.</summary>
        private void Awake()
        {
            Material source = material;
            if (source == null)
            {
                Shader shader = Shader.Find(ShaderName);
                if (shader == null)
                {
                    Debug.LogWarning("TerrainTilePreview: assign a material with the " + ShaderName + " shader.", this);
                    enabled = false;
                    return;
                }
                source = new Material(shader);
            }

            Material slotMaterial = new Material(source) { name = "Tile Slots (Runtime)", color = addColor };
            slotMaterial.SetFloat(ShowPlusId, 1f);
            slotMaterial.SetFloat(ZTestId, (float)CompareFunction.LessEqual);

            // The box is drawn over the ground, so the whole tile shows up even where hills cover its sides
            Material boxMaterial = new Material(source) { name = "Tile Removal (Runtime)", color = removeColor };
            boxMaterial.SetFloat(ShowPlusId, 0f);
            boxMaterial.SetFloat(ZTestId, (float)CompareFunction.Always);

            if (source != material)
                Destroy(source);
            _slots = new PreviewMesh("Tile Slots", transform, slotMaterial);
            _box = new PreviewMesh("Tile Removal", transform, boxMaterial);
        }

        /// <summary>Destroys the meshes and materials made at runtime.</summary>
        private void OnDestroy()
        {
            _slots?.Destroy();
            _box?.Destroy();
        }

        /// <summary>
        /// Shows the free slots as outlined squares with a plus, and hides the removal box.
        /// </summary>
        /// <param name="slots">Corner (smallest X and Z) of every free slot.</param>
        /// <param name="tileSize">Size of one tile in meters.</param>
        /// <param name="height">World height of the ground of a new tile.</param>
        /// <param name="hoveredSlot">Index of the slot under the pointer (drawn fully opaque), or -1.</param>
        public void ShowSlots(IReadOnlyList<Vector3> slots, Vector3 tileSize, float height, int hoveredSlot)
        {
            if (_slots == null)
                return;
            _box.SetVisible(false);
            _slots.Clear();
            Vector2 size = new Vector2(tileSize.x, tileSize.z);
            float y = height + lift;
            for (int i = 0; i < slots.Count; i++)
            {
                Vector3 corner = new Vector3(slots[i].x, y, slots[i].z);
                float alpha = i == hoveredSlot ? 1f : idleOpacity;
                _slots.AddQuad(corner, corner + new Vector3(size.x, 0f, 0f), corner + new Vector3(size.x, 0f, size.y),
                               corner + new Vector3(0f, 0f, size.y), size, alpha);
            }
            _slots.Apply();
        }

        /// <summary>Shows an outlined box (world space) around the tile that would be removed, and hides the slots.</summary>
        public void ShowTile(Bounds bounds)
        {
            if (_box == null)
                return;
            _slots.SetVisible(false);
            _box.Clear();
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            Vector3 size = bounds.size;
            // Top and bottom
            _box.AddQuad(new Vector3(min.x, max.y, min.z), new Vector3(max.x, max.y, min.z), new Vector3(max.x, max.y, max.z), new Vector3(min.x, max.y, max.z), new Vector2(size.x, size.z), 1f);
            _box.AddQuad(new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z), new Vector3(max.x, min.y, max.z), new Vector3(min.x, min.y, max.z), new Vector2(size.x, size.z), 1f);
            // Front and back
            _box.AddQuad(new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z), new Vector3(max.x, max.y, min.z), new Vector3(min.x, max.y, min.z), new Vector2(size.x, size.y), 1f);
            _box.AddQuad(new Vector3(min.x, min.y, max.z), new Vector3(max.x, min.y, max.z), new Vector3(max.x, max.y, max.z), new Vector3(min.x, max.y, max.z), new Vector2(size.x, size.y), 1f);
            // Left and right
            _box.AddQuad(new Vector3(min.x, min.y, min.z), new Vector3(min.x, min.y, max.z), new Vector3(min.x, max.y, max.z), new Vector3(min.x, max.y, min.z), new Vector2(size.z, size.y), 1f);
            _box.AddQuad(new Vector3(max.x, min.y, min.z), new Vector3(max.x, min.y, max.z), new Vector3(max.x, max.y, max.z), new Vector3(max.x, max.y, min.z), new Vector2(size.z, size.y), 1f);
            _box.Apply();
        }

        /// <summary>Hides the slots and the removal box.</summary>
        public void Hide()
        {
            _slots?.SetVisible(false);
            _box?.SetVisible(false);
        }

        /// <summary>
        /// One preview mesh built from quads in world space, drawn by a child object. Every quad gets 0-1 UVs across it and
        /// its size in meters in the second UV set, which the TilePreview shader uses for outlines of even width.
        /// </summary>
        private sealed class PreviewMesh
        {
            private readonly GameObject _object;
            private readonly Mesh _mesh;
            private readonly Material _material;
            private readonly List<Vector3> _vertices = new List<Vector3>();
            private readonly List<Vector2> _uvs = new List<Vector2>();
            private readonly List<Vector2> _sizes = new List<Vector2>();
            private readonly List<Color> _colors = new List<Color>();
            private readonly List<int> _triangles = new List<int>();

            /// <summary>Creates the (hidden) child object that draws the mesh.</summary>
            public PreviewMesh(string name, Transform parent, Material material)
            {
                _material = material;
                _object = new GameObject(name);
                _object.transform.SetParent(parent, false);
                _object.SetActive(false);

                _mesh = new Mesh { name = name };
                _mesh.MarkDynamic();
                _object.AddComponent<MeshFilter>().sharedMesh = _mesh;
                MeshRenderer meshRenderer = _object.AddComponent<MeshRenderer>();
                meshRenderer.sharedMaterial = material;
                meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
                meshRenderer.receiveShadows = false;
            }

            /// <summary>Removes all quads (call <see cref="Apply"/> to show the result).</summary>
            public void Clear()
            {
                _vertices.Clear();
                _uvs.Clear();
                _sizes.Clear();
                _colors.Clear();
                _triangles.Clear();
            }

            /// <summary>Adds a quad from four world-space corners, going around it.</summary>
            /// <param name="size">Width and height of the quad in meters.</param>
            /// <param name="alpha">Opacity of the quad.</param>
            public void AddQuad(Vector3 corner0, Vector3 corner1, Vector3 corner2, Vector3 corner3, Vector2 size, float alpha)
            {
                int first = _vertices.Count;
                Matrix4x4 toLocal = _object.transform.worldToLocalMatrix;
                _vertices.Add(toLocal.MultiplyPoint3x4(corner0));
                _vertices.Add(toLocal.MultiplyPoint3x4(corner1));
                _vertices.Add(toLocal.MultiplyPoint3x4(corner2));
                _vertices.Add(toLocal.MultiplyPoint3x4(corner3));
                _uvs.Add(new Vector2(0f, 0f));
                _uvs.Add(new Vector2(1f, 0f));
                _uvs.Add(new Vector2(1f, 1f));
                _uvs.Add(new Vector2(0f, 1f));
                Color color = new Color(1f, 1f, 1f, alpha);
                for (int i = 0; i < 4; i++)
                {
                    _sizes.Add(size);
                    _colors.Add(color);
                }
                _triangles.Add(first);
                _triangles.Add(first + 2);
                _triangles.Add(first + 1);
                _triangles.Add(first);
                _triangles.Add(first + 3);
                _triangles.Add(first + 2);
            }

            /// <summary>Uploads the quads to the mesh and shows it (hides it when there are none).</summary>
            public void Apply()
            {
                _mesh.Clear();
                _mesh.SetVertices(_vertices);
                _mesh.SetUVs(0, _uvs);
                _mesh.SetUVs(1, _sizes);
                _mesh.SetColors(_colors);
                _mesh.SetTriangles(_triangles, 0);
                _mesh.RecalculateBounds();
                SetVisible(_vertices.Count > 0);
            }

            /// <summary>Shows or hides the mesh.</summary>
            public void SetVisible(bool visible)
            {
                if (_object.activeSelf != visible)
                    _object.SetActive(visible);
            }

            /// <summary>Destroys the child object, mesh and material.</summary>
            public void Destroy()
            {
                if (_object != null)
                    Object.Destroy(_object);
                if (_mesh != null)
                    Object.Destroy(_mesh);
                if (_material != null)
                    Object.Destroy(_material);
            }
        }
    }
}
