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

    // Visible (mesh, triangle) pairs collected by the sequential cull phase and
    // consumed by the parallel paint phase. Reused across frames.
    private readonly List<(int MeshIndex, int TriangleIndex)> _visible = [];

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

        painter?.Prepare(scene);

        var viewMatrix = camera.ViewMatrix;
        var projectionMatrix = projection.ProjectionMatrix(surface.Width, surface.Height);

        // View-space frustum planes for whole-mesh bounding-sphere culling.
        Span<Vector4> frustumPlanes = stackalloc Vector4[6];
        BuildFrustumPlanes(projectionMatrix, frustumPlanes);

        // Allocate arrays to store transformed vertices
        using var worldBuffer = new WorldBuffer(world);

        List<IMesh> meshes = world.Meshes;
        int volumeCount = meshes.Count;

        // Phase 1 (sequential): transform, cull and project; collect visible triangles.
        _visible.Clear();

        for (var idxVolume = 0; idxVolume < volumeCount; idxVolume++)
        {
            var vbx = worldBuffer.VertexBuffers[idxVolume];
            var mesh = meshes[idxVolume];

            var worldMatrix = mesh.WorldMatrix;
            var modelViewMatrix = worldMatrix * viewMatrix;

            vbx.Mesh = mesh;
            vbx.WorldMatrix = worldMatrix;

            Stats.TotalTriangleCount += mesh.Triangles.Length;

            // Whole-mesh rejection: if the mesh's bounding sphere is fully outside the
            // frustum, skip transforming its vertices and culling its triangles.
            var radius = mesh.BoundingRadius * MaxAbsComponent(mesh.Scale);
            if (!float.IsPositiveInfinity(radius))
            {
                var viewCenter = Vector3.Transform(Vector3.Zero, modelViewMatrix);
                if (IsSphereOutside(frustumPlanes, viewCenter, radius))
                {
                    Stats.OutOfViewTriangleCount += mesh.Triangles.Length;
                    continue;
                }
            }

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

                // Cache world positions and normals while still single-threaded, so the
                // parallel paint phase only reads the vertex buffer.
                t.TransformWorld(vbx);

                _visible.Add((idxVolume, idxTriangle));
                Stats.DrawnTriangleCount++;
            }
        }

        Stats.PaintTime();

        // Phase 2 (parallel): fill the visible triangles. Each worker owns an
        // interleaved set of screen rows, so pixel writes never overlap.
        if (painter is not null && _visible.Count > 0)
        {
            var sliceCount = System.Math.Clamp(Environment.ProcessorCount, 1, 16);

            if (sliceCount == 1 || _visible.Count < 32)
            {
                PaintSlice(painter, surface, meshes, worldBuffer, RowSlice.Full);
            }
            else
            {
                Parallel.For(0, sliceCount, s =>
                    PaintSlice(painter, surface, meshes, worldBuffer, new RowSlice(s, sliceCount)));
            }
        }

        // The wireframe overlay draws lines across arbitrary rows, so it runs after the
        // parallel fills, sequentially. Drawing last also keeps the lines visible on top.
        if (rendererSettings.ShowTriangles)
        {
            foreach (var (meshIndex, triangleIndex) in _visible)
            {
                _internalWireFramePainter.DrawTriangle(surface, ColorRGB.Magenta, worldBuffer.VertexBuffers[meshIndex], triangleIndex, RowSlice.Full);
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

        Stats.StopTime();
    }

    private void PaintSlice(IPainter painter, FrameBuffer surface, List<IMesh> meshes, WorldBuffer worldBuffer, in RowSlice slice)
    {
        var count = _visible.Count;
        for (var i = 0; i < count; i++)
        {
            var (meshIndex, triangleIndex) = _visible[i];
            painter.DrawTriangle(
                surface,
                meshes[meshIndex].TriangleColors[triangleIndex],
                worldBuffer.VertexBuffers[meshIndex],
                triangleIndex,
                slice);
        }
    }

    /// <summary>
    /// Extracts the six view-space frustum planes from a projection matrix
    /// (row-vector convention, clip z in [0, w]). Planes point inward:
    /// dot(normal, point) + distance ≥ 0 means inside.
    /// </summary>
    private static void BuildFrustumPlanes(in Matrix4x4 p, Span<Vector4> planes)
    {
        var c1 = new Vector4(p.M11, p.M21, p.M31, p.M41);
        var c2 = new Vector4(p.M12, p.M22, p.M32, p.M42);
        var c3 = new Vector4(p.M13, p.M23, p.M33, p.M43);
        var c4 = new Vector4(p.M14, p.M24, p.M34, p.M44);

        planes[0] = c4 + c1; // left
        planes[1] = c4 - c1; // right
        planes[2] = c4 + c2; // bottom
        planes[3] = c4 - c2; // top
        planes[4] = c3;      // near (z >= 0)
        planes[5] = c4 - c3; // far
    }

    private static bool IsSphereOutside(ReadOnlySpan<Vector4> planes, Vector3 center, float radius)
    {
        foreach (var plane in planes)
        {
            var normal = new Vector3(plane.X, plane.Y, plane.Z);
            if (Vector3.Dot(normal, center) + plane.W < -radius * normal.Length())
            {
                return true;
            }
        }
        return false;
    }

    private static float MaxAbsComponent(Vector3 v) =>
        MathF.Max(MathF.Abs(v.X), MathF.Max(MathF.Abs(v.Y), MathF.Abs(v.Z)));
}
