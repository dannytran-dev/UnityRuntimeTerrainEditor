using System;
using UnityEngine;

namespace RuntimeTerrainEditor
{
    /// <summary>
    /// Reads and writes heightmaps as images or 16-bit RAW files. Heights are normalized [0,1] grids indexed [z, x],
    /// with row 0 along the terrain's front edge (z = 0), the same way Unity's heightmap is stored.
    /// </summary>
    public static class TerrainHeightmapIO
    {
        /// <summary>
        /// Heights from a grayscale image (the red channel). Bright is high. 8-bit images give 256 height steps, and so do
        /// images without Read/Write enabled; use RAW files for full precision.
        /// </summary>
        public static float[,] FromImage(Texture2D image)
        {
            if (image == null)
                throw new ArgumentNullException(nameof(image));

            Color[] pixels = TextureReadback.GetPixels(image);
            int width = image.width;
            int height = image.height;
            float[,] heights = new float[height, width];
            for (int z = 0; z < height; z++)
                for (int x = 0; x < width; x++)
                    heights[z, x] = pixels[z * width + x].r;
            return heights;
        }

        /// <summary>
        /// Heights from a square 16-bit RAW file (like Unity's terrain Export Raw). The resolution is worked out from the size.
        /// </summary>
        public static float[,] FromRaw16(byte[] raw, bool littleEndian = true)
        {
            if (raw == null)
                throw new ArgumentNullException(nameof(raw));
            int resolution = Mathf.RoundToInt(Mathf.Sqrt(raw.Length / 2f));
            if (resolution < 2 || resolution * resolution * 2 != raw.Length)
                throw new ArgumentException("A 16-bit RAW heightmap must be square; " + raw.Length + " bytes isn't.");

            float[,] heights = new float[resolution, resolution];
            for (int i = 0, z = 0; z < resolution; z++)
            {
                for (int x = 0; x < resolution; x++, i += 2)
                {
                    int value = littleEndian ? raw[i] | (raw[i + 1] << 8) : (raw[i] << 8) | raw[i + 1];
                    heights[z, x] = value / 65535f;
                }
            }
            return heights;
        }

        /// <summary>The terrain's heights as a 16-bit RAW file.</summary>
        public static byte[] ToRaw16(TerrainData data, bool littleEndian = true)
        {
            int resolution = data.heightmapResolution;
            return ToRaw16(data.GetHeights(0, 0, resolution, resolution), littleEndian);
        }

        /// <summary>
        /// A [z, x] grid of normalized heights as a 16-bit RAW file. <see cref="FromRaw16"/> reads back square grids only.
        /// </summary>
        public static byte[] ToRaw16(float[,] heights, bool littleEndian = true)
        {
            int rows = heights.GetLength(0);
            int columns = heights.GetLength(1);
            byte[] raw = new byte[rows * columns * 2];
            for (int i = 0, z = 0; z < rows; z++)
            {
                for (int x = 0; x < columns; x++, i += 2)
                {
                    int value = Mathf.RoundToInt(Mathf.Clamp01(heights[z, x]) * 65535f);
                    raw[littleEndian ? i : i + 1] = (byte)(value & 0xFF);
                    raw[littleEndian ? i + 1 : i] = (byte)(value >> 8);
                }
            }
            return raw;
        }
    }
}
