using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Gizmos;
using SoftEngine.Core.Rasterization;
using SoftEngine.Core.Rasterization.Painters;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Cameras;
using SoftEngine.Core.Scenes.Projections;
using System.Numerics;

namespace SoftEngine.Core.Pipeline;

public sealed class Renderer : IRenderer
{
    private readonly WireFramePainter _internalWireFramePainter = new();

    public RendererSettings Settings { get; set; } = new();

    public RenderStats Stats { get; } = new();

    public void Render(Scene scene, IPainter? painter)
    {
        FrameBuffer surface = scene.Surface;
        ICamera camera = scene.Camera;
        IProjection projection = scene.Projection;
        IWorld world = scene.World;
        RendererSettings rendererSettings = Settings;

        Stats.Clear();
        Stats.PaintTime();

        // Match the depth buffer to the projection's clip planes for this frame.
        surface.SetDepthRange(projection.ZNear, projection.ZFar);

        surface.Clear();
        Stats.CalculationTime();

        // model => worldMatrix => world => viewMatrix => view => projectionMatrix => projection => toNdc => ndc => toScreen => screen

        var viewMatrix = camera.ViewMatrix;
        var projectionMatrix = projection.ProjectionMatrix(surface.Width, surface.Height);

        // Allocate arrays to store transformed vertices
        using var worldBuffer = new WorldBuffer(world);

        List<IMesh> meshes = world.Meshes;
        int volumeCount = meshes.Count;

        for (var idxVolume = 0; idxVolume < volumeCount; idxVolume++)
        {

            var vbx = worldBuffer.VertexBuffers[idxVolume];
            var mesh = meshes[idxVolume];

            var worldMatrix = mesh.WorldMatrix;
            var modelViewMatrix = worldMatrix * viewMatrix;

            vbx.Mesh = mesh;
            vbx.WorldMatrix = worldMatrix;

            Stats.TotalTriangleCount += mesh.Triangles.Length;

            var vertices = mesh.Vertices;

            // Transform and store vertices to View
            var vertexCount = vertices.Length;
            for (var idxVertex = 0; idxVertex < vertexCount; idxVertex++)
            {
                vbx.Vertices[idxVertex] = vbx.Vertices[idxVertex].SetView(Vector3.Transform(vertices[idxVertex], modelViewMatrix));
            }

            var triangleCount = mesh.Triangles.Length;
            for (var idxTriangle = 0; idxTriangle < triangleCount; idxTriangle++)
            {
                Triangle t = mesh.Triangles[idxTriangle];

                // Discard if behind far plane
                if (t.IsBehindFarPlane(vbx))
                {
                    Stats.BehindViewTriangleCount++;
                    continue;
                }

                // Discard if back facing 
                if (rendererSettings.BackFaceCulling && t.IsFacingBack(vbx))
                {
                    Stats.FacingBackTriangleCount++;
                    continue;
                }

                // Project in frustum
                t.TransformProjection(vbx, projectionMatrix);

                // Discard if outside view frustum
                if (t.IsOutsideFrustum(vbx))
                {
                    Stats.OutOfViewTriangleCount++;
                    continue;
                }

                Stats.PaintTime();

                var color = mesh.TriangleColors[idxTriangle];

                if (rendererSettings.ShowTriangles)
                {
                    _internalWireFramePainter.DrawTriangle(scene.Surface, ColorRGB.Magenta, vbx, idxTriangle);
                }

                painter?.DrawTriangle(scene.Surface, color, vbx, idxTriangle);

                Stats.DrawnTriangleCount++;

                Stats.CalculationTime();
            }
        }

        if (rendererSettings.ShowXZGrid)
        {
            GizmoRenderer.DrawGrid(surface, viewMatrix * projectionMatrix, -10, 10);
        }

        if (rendererSettings.ShowAxes)
        {
            GizmoRenderer.DrawAxes(surface, viewMatrix * projectionMatrix);
        }
    }
}
