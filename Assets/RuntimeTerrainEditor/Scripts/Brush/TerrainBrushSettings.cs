using UnityEngine;

namespace RuntimeTerrainEditor
{
    /// <summary>
    /// A set of brush shapes. Only holds setup data: the brush in use (shape, size, strength...) lives on
    /// <see cref="TerrainBrush"/>, so playing never writes to this asset.
    /// </summary>
    [CreateAssetMenu(fileName = "New Terrain Brush Settings", menuName = "Runtime Terrain Editor/Brush Settings")]
    public class TerrainBrushSettings : ScriptableObject
    {
        [Tooltip("Brush shapes. The alpha channel is the brush strength.")]
        [SerializeField] private Texture2D[] brushTextures = new Texture2D[0];

        /// <summary>Number of brush shapes.</summary>
        public int Count => brushTextures != null ? brushTextures.Length : 0;

        /// <summary>The brush texture at an index (clamped to the list), or null when there are none.</summary>
        public Texture2D GetTexture(int index)
        {
            if (Count == 0)
                return null;
            return brushTextures[Mathf.Clamp(index, 0, brushTextures.Length - 1)];
        }
    }
}
