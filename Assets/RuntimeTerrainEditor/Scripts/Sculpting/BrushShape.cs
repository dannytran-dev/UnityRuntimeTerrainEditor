using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace RuntimeTerrainEditor.Sculpting
{
    /// <summary>
    /// A brush texture decoded once into a grid of weights, so brush dabs never touch the texture again.
    /// The alpha channel is the brush strength. The texture does not need Read/Write enabled.
    /// </summary>
    public sealed class BrushShape
    {
        private static readonly Dictionary<Texture2D, BrushShape> Cache = new Dictionary<Texture2D, BrushShape>();

        private readonly float[] _weights;
        private readonly int _width;
        private readonly int _height;

        /// <summary>A brush with full weight everywhere (a hard square).</summary>
        public static BrushShape Solid { get; } = new BrushShape(null, new[] { 1f }, 1, 1);

        /// <summary>The texture this shape was decoded from (null for <see cref="Solid"/>).</summary>
        public Texture2D Source { get; }

        /// <summary>Wraps decoded weights; use <see cref="Get"/> to decode a texture.</summary>
        private BrushShape(Texture2D source, float[] weights, int width, int height)
        {
            Source = source;
            _weights = weights;
            _width = width;
            _height = height;
        }

        /// <summary>Returns the decoded shape for a texture, decoding it on first use.</summary>
        public static BrushShape Get(Texture2D texture)
        {
            if (texture == null)
                return null;
            if (!Cache.TryGetValue(texture, out BrushShape shape))
            {
                shape = Decode(texture);
                Cache[texture] = shape;
            }
            return shape;
        }

        /// <summary>Forgets decoded shapes, e.g. after brush textures were changed at runtime.</summary>
        public static void ClearCache()
        {
            Cache.Clear();
        }

        /// <summary>Bilinear weight at (u, v) in [0,1]; u runs along terrain X and v along terrain Z.</summary>
        public float Sample(float u, float v)
        {
            float pixelX = Mathf.Clamp01(u) * (_width - 1);
            float pixelY = Mathf.Clamp01(v) * (_height - 1);
            int x0 = (int)pixelX;
            int y0 = (int)pixelY;
            int x1 = Mathf.Min(x0 + 1, _width - 1);
            int y1 = Mathf.Min(y0 + 1, _height - 1);
            float fx = pixelX - x0;
            float fy = pixelY - y0;

            int row0 = y0 * _width;
            int row1 = y1 * _width;
            float bottom = _weights[row0 + x0] + (_weights[row0 + x1] - _weights[row0 + x0]) * fx;
            float top = _weights[row1 + x0] + (_weights[row1 + x1] - _weights[row1 + x0]) * fx;
            return bottom + (top - bottom) * fy;
        }

        /// <summary>Reads a texture's alpha channel into a grid of weights.</summary>
        private static BrushShape Decode(Texture2D texture)
        {
            if (!GraphicsFormatUtility.HasAlphaChannel(texture.graphicsFormat))
                Debug.LogWarning("BrushShape: brush texture '" + texture.name + "' has no alpha channel, so it works as a solid square. " +
                                 "The brush strength is read from alpha.", texture);

            Color[] pixels = TextureReadback.GetPixels(texture);
            float[] weights = new float[pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
                weights[i] = pixels[i].a;
            return new BrushShape(texture, weights, texture.width, texture.height);
        }
    }
}
