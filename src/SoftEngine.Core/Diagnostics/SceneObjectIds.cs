namespace SoftEngine.Core.Diagnostics;

/// <summary>
/// The identifier scheme shared by the graphics event list and the graphics object table:
/// every object a frame touches gets a stable <c>obj:N</c> slot. Fixed slots come first,
/// then the world's lights, then its meshes; anything a front-end wants to list beyond
/// that (textures, for instance) starts at <see cref="AfterMeshes"/>.
/// </summary>
public static class SceneObjectIds
{
    public const int RenderTarget = 0;
    public const int DepthBuffer = 1;
    public const int Camera = 2;
    public const int Projection = 3;
    public const int Painter = 4;

    /// <summary>First identifier handed out to world contents.</summary>
    public const int First = 5;

    public static int Light(int lightIndex) => First + lightIndex;

    public static int Mesh(int lightCount, int meshIndex) => First + lightCount + meshIndex;

    public static int AfterMeshes(int lightCount, int meshCount) => First + lightCount + meshCount;
}
