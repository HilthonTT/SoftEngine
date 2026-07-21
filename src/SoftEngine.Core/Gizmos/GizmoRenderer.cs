using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Rasterization.Painters;
using System.Numerics;

namespace SoftEngine.Core.Gizmos;

public static class GizmoRenderer
{
    public static void DrawGrid(FrameBuffer surface, Matrix4x4 world2Projection, float from, float to)
    {
        for (var xz = from; xz <= to; xz++)
        {
            DrawLine(surface, world2Projection, new Vector3(xz, 0, from), new Vector3(xz, 0, to), xz == 0 ? ColorRGB.Red : ColorRGB.Green);
            DrawLine(surface, world2Projection, new Vector3(from, 0, xz), new Vector3(to, 0, xz), ColorRGB.Green);
        }
    }

    public static void DrawLine(FrameBuffer surface, Matrix4x4 world2Projection, Vector3 worldP0, Vector3 worldP1, ColorRGB color)
    {
        Vector4 projectionP0 = Vector4.Transform(worldP0, world2Projection);
        var projectionP1 = Vector4.Transform(worldP1, world2Projection);

        WireFramePainter.DrawLine(surface, color, projectionP0, projectionP1);
    }

    public static void DrawAxes(FrameBuffer surface, Matrix4x4 world2Projection)
    {
        DrawLine(surface, world2Projection, new Vector3(0, 0, 0), new Vector3(1, 0, 0), ColorRGB.Red);
        DrawLine(surface, world2Projection, new Vector3(1, 0, 0), new Vector3(.75f, .25f, 0), ColorRGB.Red);
        DrawLine(surface, world2Projection, new Vector3(1, 0, 0), new Vector3(.75f, -.25f, 0), ColorRGB.Red);
        DrawLine(surface, world2Projection, new Vector3(1, 0, 0), new Vector3(.75f, 0, .25f), ColorRGB.Red);
        DrawLine(surface, world2Projection, new Vector3(1, 0, 0), new Vector3(.75f, 0, -.25f), ColorRGB.Red);

        DrawLine(surface, world2Projection, new Vector3(0, 0, 0), new Vector3(0, 1, 0), ColorRGB.Green);
        DrawLine(surface, world2Projection, new Vector3(0, 1, 0), new Vector3(-.25f, .75f, 0), ColorRGB.Green);
        DrawLine(surface, world2Projection, new Vector3(0, 1, 0), new Vector3(.25f, .75f, 0), ColorRGB.Green);
        DrawLine(surface, world2Projection, new Vector3(0, 1, 0), new Vector3(0, .75f, -.25f), ColorRGB.Green);
        DrawLine(surface, world2Projection, new Vector3(0, 1, 0), new Vector3(0, .75f, .25f), ColorRGB.Green);

        DrawLine(surface, world2Projection, new Vector3(0, 0, 0), new Vector3(0, 0, 1), ColorRGB.Blue);
        DrawLine(surface, world2Projection, new Vector3(0, 0, 1), new Vector3(-.25f, 0, .75f), ColorRGB.Blue);
        DrawLine(surface, world2Projection, new Vector3(0, 0, 1), new Vector3(.25f, 0, .75f), ColorRGB.Blue);
        DrawLine(surface, world2Projection, new Vector3(0, 0, 1), new Vector3(0, -.25f, .75f), ColorRGB.Blue);
        DrawLine(surface, world2Projection, new Vector3(0, 0, 1), new Vector3(0, .25f, .75f), ColorRGB.Blue);
    }
}
