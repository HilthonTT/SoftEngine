using System.Numerics;

namespace SoftEngine.Gpu;

public static class GpuMatrices
{
    public static readonly Matrix4x4 DepthZeroToOne = new(
        1f, 0f, 0f, 0f,
        0f, 1f, 0f, 0f,
        0f, 0f, 2f, 0f,
        0f, 0f, -1f, 1f);

    public static readonly Matrix4x4 ScreenSpace = new(
        1f, 0f, 0f, 0f,
        0f, -1f, 0f, 0f,
        0f, 0f, 2f, 0f,
        0f, 0f, -1f, 1f);
}
