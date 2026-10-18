namespace SoftEngine.Core.Diagnostics;

public static class SceneObjectIds
{
    public const int RenderTarget = 0;
    public const int DepthBuffer = 1;
    public const int Camera = 2;
    public const int Projection = 3;
    public const int Painter = 4;
    public const int ShadowMap = 5;
    public const int PostProcess = 6;

    public const int First = 7;

    public static int Light(int lightIndex) => First + lightIndex;

    public static int Mesh(int lightCount, int meshIndex) => First + lightCount + meshIndex;

    public static int AfterMeshes(int lightCount, int meshCount) => First + lightCount + meshCount;
}
