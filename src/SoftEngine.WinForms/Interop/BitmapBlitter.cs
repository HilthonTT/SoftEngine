using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SoftEngine.WinForms.Interop;

public static class BitmapBlitter
{
    public static void FillBitmap(Bitmap bmp, int[] buffer)
    {
        if (buffer.Length != bmp.Width * bmp.Height)
        {
            throw new ArgumentException($"Expected {bmp.Width * bmp.Height} pixels for a {bmp.Width}×{bmp.Height} bitmap, got {buffer.Length}.", nameof(buffer));
        }

        var bmpData = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.WriteOnly, bmp.PixelFormat);
        try
        {
            Marshal.Copy(buffer, 0, bmpData.Scan0, buffer.Length);
        }
        finally
        {
            bmp.UnlockBits(bmpData);
        }
    }
}
