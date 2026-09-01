using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Rasterization.Shaders;
using SoftEngine.Core.Rasterization.Varyings;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Lights;
using System.Numerics;

namespace SoftEngine.Core.Rasterization.Painters;

public sealed class PhongPainter(
    ILight? light = null,
    float ambient = 0.12f,
    float specularStrength = 0.35f,
    float shininess = 32f) : LitPainter(light, ambient)
{
    private readonly float _specularStrength = specularStrength;
    private readonly float _shininess = shininess;

    private Vector3 _eye;

    protected override void PrepareCore(Scene scene)
    {
        _eye = Matrix4x4.Invert(scene.Camera.ViewMatrix, out var inverseView)
            ? inverseView.Translation
            : scene.Camera.Position;
    }

    public override void DrawTriangle(FrameBuffer surface, ColorRGB color, VertexBuffer vertexBuffer, int triangleIndice, in ScreenTile tile)
    {
        ArgumentNullException.ThrowIfNull(vertexBuffer.Mesh, nameof(vertexBuffer));

        var t = vertexBuffer.GetTriangle(triangleIndice);

        var (a, b, c) = (vertexBuffer.GetVertex(t.I0), vertexBuffer.GetVertex(t.I1), vertexBuffer.GetVertex(t.I2));

        ScanlineRasterizer.Fill(
            surface,
            surface.ToScreen3(a.Proj), surface.ToScreen3(b.Proj), surface.ToScreen3(c.Proj),
            1f / a.Proj.W, 1f / b.Proj.W, 1f / c.Proj.W,
            new PhongVarying(a.World, a.Norm),
            new PhongVarying(b.World, b.Norm),
            new PhongVarying(c.World, c.Norm),
            new BlinnPhongShader(color, Lights, _eye, AmbientLight, _specularStrength, _shininess, GammaCorrect, Shadows),
            StateFor(vertexBuffer.Mesh),
            tile);
    }
}
