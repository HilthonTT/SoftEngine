using SoftEngine.Core.Geometry;
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
}
