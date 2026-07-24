using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SoftEngine.WinForms.Interop;

public static class BitmapBlitter
{
    // Taken from https://stackoverflow.com/a/11740297/24472
    public static void FillBitmap(Bitmap bmp, int[] buffer)
    {
        // A buffer larger than the bitmap would write past the locked native bits —
        // silent heap corruption rather than an exception — so reject any mismatch.
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
