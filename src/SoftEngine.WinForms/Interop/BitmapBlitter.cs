using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SoftEngine.WinForms.Interop;

public static class BitmapBlitter
{
    // Taken from https://stackoverflow.com/a/11740297/24472
    public static void FillBitmap(Bitmap bmp, int[] buffer)
    {
        var bmpData = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.WriteOnly, bmp.PixelFormat);
        Marshal.Copy(buffer, 0, bmpData.Scan0, buffer.Length);
        bmp.UnlockBits(bmpData);
    }
}
