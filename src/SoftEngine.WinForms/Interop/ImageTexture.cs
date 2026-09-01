using SoftEngine.Core.Geometry;
using SoftEngine.Core.Geometry.Import.Gltf;
using SoftEngine.Core.Textures;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SoftEngine.WinForms.Interop;

public static class ImageTexture
{
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
            return null;
        }
    }

    public static Texture? Load(ReadOnlyMemory<byte> encoded)
    {
        if (encoded.IsEmpty)
        {
            return null;
        }

        try
        {
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
