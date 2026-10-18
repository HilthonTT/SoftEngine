using System.Numerics;

namespace SoftEngine.WinForms.Demos;

internal static class DemoDefaults
{
    public const float FieldOfView = 40f * MathF.PI / 180f;

    public static readonly Vector3 CameraPosition = new(0, 0, -60);

    public static string ModelPath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Models", fileName);
}
