using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Lights;
using System.Numerics;

namespace SoftEngine.Core.Rasterization.Painters;

/// <summary>
/// Per-pixel Blinn-Phong: interpolates world position and normal across the triangle
/// and lights every fragment, giving smooth highlights that Gouraud misses.
/// </summary>
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
        // Camera.Position is the translation fed into the view matrix, not the eye's
        // world position — invert the view matrix to get the true eye point.
        _eye = Matrix4x4.Invert(scene.Camera.ViewMatrix, out var inverseView)
            ? inverseView.Translation
            : scene.Camera.Position;
    }

    public override void DrawTriangle(FrameBuffer surface, ColorRGB color, VertexBuffer vertexBuffer, int triangleIndice, in RowSlice slice)
    {
        ArgumentNullException.ThrowIfNull(vertexBuffer.Mesh, nameof(vertexBuffer));

        var t = vertexBuffer.Mesh.Triangles[triangleIndice];
        t.TransformWorld(vertexBuffer);

        var (a, b, c) = (vertexBuffer.Vertices[t.I0], vertexBuffer.Vertices[t.I1], vertexBuffer.Vertices[t.I2]);

        // Resolve the light to plain vectors so the per-pixel shader stays dispatch-free.
        var (lightVector, isDirectional) = Light switch
        {
            DirectionalLight d => (d.DirectionFrom(Vector3.Zero), true),
            PointLight p => (p.Position, false),
            _ => (Light.DirectionFrom((a.World + b.World + c.World) / 3f), true),
        };

        ScanlineRasterizer.Fill(
            surface,
            surface.ToScreen3(a.Proj), surface.ToScreen3(b.Proj), surface.ToScreen3(c.Proj),
            1f / a.Proj.W, 1f / b.Proj.W, 1f / c.Proj.W,
            new PhongVarying(a.World, a.Norm),
            new PhongVarying(b.World, b.Norm),
            new PhongVarying(c.World, c.Norm),
            new BlinnPhongShader(color, lightVector, isDirectional, Light.Intensity, _eye, Ambient, _specularStrength, _shininess),
            slice);
    }
}
