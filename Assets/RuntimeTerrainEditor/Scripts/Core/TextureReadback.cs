using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace RuntimeTerrainEditor
{
    /// <summary>
    /// Reads a texture's pixels on the CPU, also when Read/Write isn't enabled in its import settings.
    /// </summary>
    internal static class TextureReadback
    {
        /// <summary>
        /// The pixels as they are stored in the texture (sRGB values stay sRGB), bottom row first.
        /// Textures without Read/Write are copied through the GPU at 8 bits per channel.
        /// </summary>
        public static Color[] GetPixels(Texture2D texture)
        {
            if (texture.isReadable)
                return texture.GetPixels();

            // Blitting decodes sRGB textures; an sRGB target encodes them again, so the stored values come back unchanged
            bool srgb = GraphicsFormatUtility.IsSRGBFormat(texture.graphicsFormat);
            RenderTexture previous = RenderTexture.active;
            RenderTexture target = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32,
                                                              srgb ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear);
            Texture2D copy = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false, !srgb);
            try
            {
                Graphics.Blit(texture, target);
                RenderTexture.active = target;
                copy.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0, false);
                return copy.GetPixels();
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                // DestroyImmediate so this also works from editor scripts
                Object.DestroyImmediate(copy);
            }
        }
    }
}
