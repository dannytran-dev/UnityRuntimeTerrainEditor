using System;
using RuntimeTerrainEditor.Sculpting;
using UnityEngine;

namespace RuntimeTerrainEditor
{
    /// <summary>
    /// The brush in use: its shape, size, rotation, strength and target, and the selected terrain layer, tree type and
    /// grass type. The shapes come from a <see cref="TerrainBrushSettings"/> asset; everything else lives here and
    /// starts from the Starting Values every time play starts.
    /// </summary>
    public class TerrainBrush : MonoBehaviour
    {
        [Tooltip("The brush shapes (Create > Runtime Terrain Editor > Brush Settings)")]
        [SerializeField] private TerrainBrushSettings settings;

        [Header("Starting Values")]
        [Tooltip("Index of the brush shape used when play starts")]
        [SerializeField, Min(0)] private int startBrush;
        [Tooltip("Brush size slider value [0,1] used when play starts")]
        [SerializeField, Range(0f, 1f)] private float startSize = 0.3f;
        [Tooltip("Strength slider value [0,1] used when play starts")]
        [SerializeField, Range(0f, 1f)] private float startStrength = 1f;
        [Tooltip("Flatten height / paint opacity / tree and grass density [0,1] used when play starts")]
        [SerializeField, Range(0f, 1f)] private float startTarget = 1f;

        private TerrainSurface _surface;
        private BrushProjector _brushProjector;

        /// <summary>Raised when any brush setting changes, e.g. so UI sliders can follow keyboard shortcuts.</summary>
        public event Action Changed;

        /// <summary>The brush shapes.</summary>
        public TerrainBrushSettings Settings => settings;

        /// <summary>Index of the selected brush shape in the settings.</summary>
        public int BrushIndex { get; private set; }

        /// <summary>The selected brush texture, or null when the settings have none.</summary>
        public Texture2D CurrentTexture => settings != null ? settings.GetTexture(BrushIndex) : null;

        /// <summary>The decoded shape of the selected brush texture.</summary>
        public BrushShape CurrentShape { get; private set; }

        /// <summary>The size slider value [0,1] the current diameter came from.</summary>
        public float SizeValue { get; private set; }

        /// <summary>Brush diameter in meters.</summary>
        public float Diameter { get; private set; }

        /// <summary>Brush rotation in degrees.</summary>
        public float Rotation { get; private set; }

        /// <summary>Strength slider value [0,1].</summary>
        public float Strength { get; private set; }

        /// <summary>Flatten height / paint opacity / tree and grass density [0,1].</summary>
        public float Target { get; private set; }

        /// <summary>Terrain layer the Paint mode paints with.</summary>
        public int PaintLayer { get; private set; }

        /// <summary>Tree type the Trees mode places.</summary>
        public int TreeType { get; private set; }

        /// <summary>Grass (detail) type the Details mode paints.</summary>
        public int DetailType { get; private set; }

        /// <summary>Connects the brush to the terrain and preview, and applies the starting values.</summary>
        public void Init(TerrainSurface surface, BrushProjector brushProjector)
        {
            _surface = surface;
            _brushProjector = brushProjector;

            Strength = startStrength;
            Target = startTarget;
            SetBrush(startBrush);
            SetSize(startSize);
        }

        /// <summary>Selects a brush shape by its index in the settings (clamped to the list).</summary>
        public void SetBrush(int index)
        {
            int count = settings != null ? settings.Count : 0;
            BrushIndex = Mathf.Clamp(index, 0, Mathf.Max(0, count - 1));
            Texture2D texture = CurrentTexture;
            CurrentShape = BrushShape.Get(texture);
            if (_brushProjector != null)
                _brushProjector.SetBrushTexture(texture);
            Changed?.Invoke();
        }

        /// <summary>
        /// Sets the brush diameter from a slider value [0,1] (1 = the longest side of one terrain tile, so the brush
        /// keeps its size when tiles are added).
        /// </summary>
        public void SetSize(float sliderValue)
        {
            SizeValue = Mathf.Clamp01(sliderValue);
            Vector3 size = _surface.TileSize;
            Vector2 cell = _surface.HeightCellSize;
            // Squared so the slider has more room for small brushes; never smaller than one heightmap cell
            Diameter = Mathf.Max(Mathf.Max(cell.x, cell.y), SizeValue * SizeValue * Mathf.Max(size.x, size.z));
            Changed?.Invoke();
        }

        /// <summary>Grows or shrinks the brush by a slider amount (e.g. 0.05 per key press).</summary>
        public void NudgeSize(float sliderDelta)
        {
            SetSize(SizeValue + sliderDelta);
        }

        /// <summary>Sets the brush rotation in degrees.</summary>
        public void SetRotation(float degrees)
        {
            Rotation = Mathf.Repeat(degrees, 360f);
            Changed?.Invoke();
        }

        /// <summary>Rotates the brush further by some degrees.</summary>
        public void NudgeRotation(float degrees)
        {
            SetRotation(Rotation + degrees);
        }

        /// <summary>Sets the strength slider value [0,1].</summary>
        public void SetStrength(float strength)
        {
            Strength = Mathf.Clamp01(strength);
            Changed?.Invoke();
        }

        /// <summary>Sets the flatten height / paint opacity / tree and grass density [0,1].</summary>
        public void SetTarget(float target)
        {
            Target = Mathf.Clamp01(target);
            Changed?.Invoke();
        }

        /// <summary>Selects the terrain layer the Paint mode paints with.</summary>
        public void SetPaintLayer(int layer)
        {
            PaintLayer = Mathf.Max(0, layer);
            Changed?.Invoke();
        }

        /// <summary>Selects the tree type the Trees mode places.</summary>
        public void SetTreeType(int treeType)
        {
            TreeType = Mathf.Max(0, treeType);
            Changed?.Invoke();
        }

        /// <summary>Selects the grass (detail) type the Details mode paints.</summary>
        public void SetDetailType(int detailType)
        {
            DetailType = Mathf.Max(0, detailType);
            Changed?.Invoke();
        }
    }
}
