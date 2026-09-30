using RuntimeTerrainEditor.Sculpting;
using UnityEngine;
using UnityEngine.Rendering;

namespace RuntimeTerrainEditor
{
    /// <summary>
    /// Shows the brush on the terrain. Unity's Projector component only works in the Built-in render pipeline,
    /// so this draws a grid mesh that follows the terrain surface instead. It can also show the flatten target
    /// height as a flat disc.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class BrushProjector : MonoBehaviour
    {
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        [Tooltip("Material with the RuntimeTerrainEditor/BrushPreview shader. One is created when empty.")]
        [SerializeField] private Material brushMaterial;
        [Tooltip("Color of the brush outline on the terrain")]
        [SerializeField] private Color brushColor = new Color(0.3f, 0.75f, 1f, 0.85f);
        [Tooltip("Color of the flat disc that shows the flatten target height")]
        [SerializeField] private Color targetHeightColor = new Color(1f, 0.75f, 0.25f, 0.45f);
        [Tooltip("Vertices per side of the preview mesh")]
        [SerializeField, Range(8, 128)] private int resolution = 48;
        [Tooltip("Lifts the preview above the terrain to avoid z-fighting")]
        [SerializeField] private float surfaceOffset = 0.1f;

        private Mesh _mesh;
        private Vector3[] _vertices;
        private Vector2[] _uvs;
        private MeshRenderer _renderer;
        private Material _materialInstance;

        private Mesh _targetMesh;
        private MeshRenderer _targetRenderer;
        private Material _targetMaterial;
        private readonly Vector3[] _targetVertices = new Vector3[4];
        private readonly Vector2[] _targetUVs = new Vector2[4];

        private bool _visible = true;
        // Where the brush was last drawn, so the flatten disc can be placed under it
        private Vector3 _center;
        private float _extent;
        // What the preview mesh was last built for, so an unchanged brush isn't rebuilt every frame
        private TerrainSurface _lastSurface;
        private int _lastHeightsVersion;
        private Vector3 _lastCenter;
        private float _lastDiameter;
        private float _lastRotation;

        /// <summary>False while the preview is switched off (see <see cref="SetVisible"/>).</summary>
        public bool IsVisible => _visible;

        /// <summary>Builds the preview meshes and materials.</summary>
        private void Awake()
        {
            EnsureInitialized();
        }

        /// <summary>Destroys the meshes and materials made at runtime.</summary>
        private void OnDestroy()
        {
            if (_mesh != null)
                Destroy(_mesh);
            if (_materialInstance != null)
                Destroy(_materialInstance);
            if (_targetMesh != null)
                Destroy(_targetMesh);
            if (_targetMaterial != null)
                Destroy(_targetMaterial);
        }

        /// <summary>
        /// Builds the meshes and materials once. TerrainEditor can call in before this component's Awake runs, so every
        /// public method initializes on demand.
        /// </summary>
        private void EnsureInitialized()
        {
            if (_mesh != null)
                return;

            _renderer = GetComponent<MeshRenderer>();
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;

            if (brushMaterial == null)
            {
                Shader shader = Shader.Find("RuntimeTerrainEditor/BrushPreview");
                brushMaterial = new Material(shader) { name = "BrushPreview (Runtime)" };
            }
            _materialInstance = new Material(brushMaterial);
            _materialInstance.SetColor(ColorId, brushColor);
            _renderer.sharedMaterial = _materialInstance;

            BuildMesh();
            BuildTargetDisc();
            _renderer.enabled = false;
        }

        /// <summary>Uses a brush texture for the preview (its alpha channel is the brush strength).</summary>
        public void SetBrushTexture(Texture2D texture)
        {
            EnsureInitialized();
            _materialInstance.SetTexture(MainTexId, texture);
            _targetMaterial.SetTexture(MainTexId, texture);
        }

        /// <summary>Switches the preview on or off; while off, <see cref="UpdateBrush"/> draws nothing.</summary>
        public void SetVisible(bool visible)
        {
            EnsureInitialized();
            _visible = visible;
            if (!visible)
                Hide();
        }

        /// <summary>Switches the preview on or off.</summary>
        public void ToggleVisible()
        {
            SetVisible(!_visible);
        }

        /// <summary>Hides the brush and the flatten disc until the next <see cref="UpdateBrush"/>.</summary>
        public void Hide()
        {
            EnsureInitialized();
            _renderer.enabled = false;
            _targetRenderer.enabled = false;
        }

        /// <summary>
        /// Draws the brush on the terrain surface, centered on a world position with a diameter in meters and a rotation
        /// in degrees. It follows the ground across tile seams and over holes.
        /// </summary>
        public void UpdateBrush(TerrainSurface surface, Vector3 worldCenter, float diameter, float rotation = 0f)
        {
            EnsureInitialized();
            if (!_visible)
                return;

            // Nothing to rebuild while the brush and the ground under it stay the same
            if (_renderer.enabled && surface == _lastSurface && surface.HeightsVersion == _lastHeightsVersion
                && worldCenter == _lastCenter && diameter == _lastDiameter && rotation == _lastRotation)
                return;
            _lastSurface = surface;
            _lastHeightsVersion = surface.HeightsVersion;
            _lastCenter = worldCenter;
            _lastDiameter = diameter;
            _lastRotation = rotation;

            float radius = diameter * 0.5f;
            float radians = rotation * Mathf.Deg2Rad;
            float cos = Mathf.Cos(radians);
            float sin = Mathf.Sin(radians);
            // The grid covers the rotated brush square; the shader hides everything outside the brush
            float extent = radius * (Mathf.Abs(cos) + Mathf.Abs(sin));
            _center = worldCenter;
            _extent = extent;

            // The mesh lives in world space; keep this transform at the origin.
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            transform.localScale = Vector3.one;

            int i = 0;
            for (int z = 0; z < resolution; z++)
            {
                float offsetZ = ((float)z / (resolution - 1) * 2f - 1f) * extent;
                for (int x = 0; x < resolution; x++, i++)
                {
                    float offsetX = ((float)x / (resolution - 1) * 2f - 1f) * extent;
                    Vector3 world = new Vector3(worldCenter.x + offsetX, 0f, worldCenter.z + offsetZ);
                    world.y = surface.SampleHeight(world) + surfaceOffset;
                    _vertices[i] = world;
                    _uvs[i] = BrushArea.ToBrushUV(offsetX, offsetZ, radius, cos, sin);
                }
            }
            _mesh.vertices = _vertices;
            _mesh.uv = _uvs;
            _mesh.RecalculateBounds();
            _renderer.enabled = true;

            for (int corner = 0; corner < 4; corner++)
            {
                float offsetX = (corner == 1 || corner == 2 ? 1f : -1f) * extent;
                float offsetZ = (corner >= 2 ? 1f : -1f) * extent;
                _targetUVs[corner] = BrushArea.ToBrushUV(offsetX, offsetZ, radius, cos, sin);
            }
            _targetMesh.uv = _targetUVs;
        }

        /// <summary>Shows the flatten target as a flat disc at a world height, under the current brush.</summary>
        public void ShowTargetHeight(float worldHeight)
        {
            EnsureInitialized();
            if (!_visible || !_renderer.enabled)
            {
                _targetRenderer.enabled = false;
                return;
            }

            for (int corner = 0; corner < 4; corner++)
            {
                float offsetX = (corner == 1 || corner == 2 ? 1f : -1f) * _extent;
                float offsetZ = (corner >= 2 ? 1f : -1f) * _extent;
                _targetVertices[corner] = new Vector3(_center.x + offsetX, worldHeight, _center.z + offsetZ);
            }
            _targetMesh.vertices = _targetVertices;
            _targetMesh.RecalculateBounds();
            _targetRenderer.enabled = true;
        }

        /// <summary>Hides the flatten disc.</summary>
        public void HideTargetHeight()
        {
            EnsureInitialized();
            _targetRenderer.enabled = false;
        }

        /// <summary>Creates the grid mesh; <see cref="UpdateBrush"/> moves its vertices onto the terrain.</summary>
        private void BuildMesh()
        {
            _mesh = new Mesh { name = "BrushPreview" };
            _mesh.MarkDynamic();
            _vertices = new Vector3[resolution * resolution];
            _uvs = new Vector2[_vertices.Length];
            int[] triangles = new int[(resolution - 1) * (resolution - 1) * 6];

            for (int z = 0, index = 0; z < resolution - 1; z++)
            {
                for (int x = 0; x < resolution - 1; x++)
                {
                    int vertex = z * resolution + x;
                    triangles[index++] = vertex;
                    triangles[index++] = vertex + resolution;
                    triangles[index++] = vertex + 1;
                    triangles[index++] = vertex + 1;
                    triangles[index++] = vertex + resolution;
                    triangles[index++] = vertex + resolution + 1;
                }
            }

            _mesh.vertices = _vertices;
            _mesh.uv = _uvs;
            _mesh.triangles = triangles;
            GetComponent<MeshFilter>().sharedMesh = _mesh;
        }

        /// <summary>Creates the child object with the flat quad that shows the flatten target height.</summary>
        private void BuildTargetDisc()
        {
            GameObject disc = new GameObject("Flatten Target");
            disc.transform.SetParent(transform, false);
            disc.layer = gameObject.layer;

            _targetMesh = new Mesh { name = "BrushTargetHeight" };
            _targetMesh.MarkDynamic();
            _targetMesh.vertices = _targetVertices;
            _targetMesh.uv = _targetUVs;
            _targetMesh.triangles = new[] { 0, 3, 1, 1, 3, 2 };
            disc.AddComponent<MeshFilter>().sharedMesh = _targetMesh;

            _targetMaterial = new Material(brushMaterial) { name = "BrushTargetHeight (Runtime)" };
            _targetMaterial.SetColor(ColorId, targetHeightColor);
            _targetRenderer = disc.AddComponent<MeshRenderer>();
            _targetRenderer.sharedMaterial = _targetMaterial;
            _targetRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _targetRenderer.receiveShadows = false;
            _targetRenderer.enabled = false;
        }
    }
}
