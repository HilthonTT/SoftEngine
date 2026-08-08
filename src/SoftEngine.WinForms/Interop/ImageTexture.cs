using SoftEngine.Core.Geometry;
using SoftEngine.Core.Geometry.Import.Gltf;
using SoftEngine.Core.Textures;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SoftEngine.WinForms.Interop;

/// <summary>
/// Decodes an image file into a CPU <see cref="Texture"/>. This is the Windows-specific half of
/// texture loading that <see cref="ObjImporter"/> keeps out of the platform-neutral Core: it is
/// passed in as the importer's <c>textureLoader</c> delegate.
/// </summary>
public static class ImageTexture
{
    /// <summary>
    /// Loads <paramref name="path"/> as a 32-bit ARGB texture, or returns null if the file is
    /// missing or cannot be decoded. The 32bppArgb byte order (BGRA little-endian) matches the
    /// packed <c>0xAARRGGBB</c> layout <see cref="Texture"/> samples.
    /// </summary>
    public static Texture? Load(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var bitmap = new Bitmap(path);
            var pixels = new int[bitmap.Width * bitmap.Height];

            var data = bitmap.LockBits(
                new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb);
            try
            {
                Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            return new Texture(bitmap.Width, bitmap.Height, pixels);
        }
        catch (Exception ex) when (ex is ArgumentException or ExternalException or OutOfMemoryException)
        {
            // Unsupported/corrupt image — fall back to an untextured mesh rather than failing the
            // load. GDI+ reports many corrupt/unsupported files as OutOfMemoryException.
            return null;
        }
    }

    /// <summary>
    /// Decodes already-read image bytes — what <see cref="GltfImporter"/> hands over, because
    /// a glTF's images can be a file beside the model, a <c>data:</c> URI or a stretch of the
    /// GLB's binary chunk, and only the first of those is a path anything could open.
    /// </summary>
    public static Texture? Load(ReadOnlyMemory<byte> encoded)
    {
        if (encoded.IsEmpty)
        {
            return null;
        }

        try
        {
            // Bitmap keeps the stream alive for the life of the image when it decodes lazily,
            // so the pixels are copied out before the stream goes.
            using var stream = new MemoryStream(encoded.ToArray(), writable: false);
            using var bitmap = new Bitmap(stream);

            var pixels = new int[bitmap.Width * bitmap.Height];

            var data = bitmap.LockBits(
                new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb);
            try
            {
                Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            return new Texture(bitmap.Width, bitmap.Height, pixels);
        }
        catch (Exception ex) when (ex is ArgumentException or ExternalException or OutOfMemoryException)
        {
            return null;
        }
    }
}
