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

    // Per-mesh index of the PainterDrawTriangles event, so a probed pixel write can point
    // back at the event that produced it. Grown to the mesh count, reused across frames.
    private int[] _meshDrawEvent = [];

    public RendererSettings Settings { get; set; } = new();

    public RenderStats Stats { get; } = new();

    public RenderDiagnostics Diagnostics { get; } = new();

    public void Render(Scene scene, IPainter? painter)
    {
        FrameBuffer surface = scene.Surface;
        ICamera camera = scene.Camera;
        IProjection projection = scene.Projection;
        IWorld world = scene.World;
        RendererSettings rendererSettings = Settings;

        RenderDiagnostics diagnostics = Diagnostics;
        GraphicsEventLog events = diagnostics.Events;
        int meshIdBase = SceneObjectIds.Mesh(world.Lights.Count, 0);

        Stats.Clear();
        Stats.PaintTime();

        diagnostics.FrameNumber++;
        events.Clear();
        events.Add(GraphicsEventKind.FrameBegin, -1, diagnostics.FrameNumber);
        events.Add(GraphicsEventKind.RendererSetViewport, SceneObjectIds.RenderTarget, surface.Width, surface.Height);

        // A probe re-runs the whole frame with pixel-history recording on; the front-end
        // keeps it set while a pixel stays selected, so the history follows the camera.
        PixelHistory? history = null;
        if (diagnostics.IsProbing && diagnostics.ProbeX < surface.Width && diagnostics.ProbeY < surface.Height)
        {
            history = new PixelHistory(diagnostics.ProbeX, diagnostics.ProbeY, diagnostics.FrameNumber);
            surface.BeginProbe(history);
        }
        diagnostics.PixelHistory = history;

        // Match the depth buffer to the projection's clip planes for this frame.
        surface.SetDepthRange(projection.ZNear, projection.ZFar);
        events.Add(GraphicsEventKind.FrameBufferSetDepthRange, SceneObjectIds.DepthBuffer, projection.ZNear, projection.ZFar);

        var clearEvent = events.Add(GraphicsEventKind.FrameBufferClearRenderTarget, SceneObjectIds.RenderTarget, surface.Width, surface.Height);
        events.Add(GraphicsEventKind.FrameBufferClearDepthBuffer, SceneObjectIds.DepthBuffer, surface.Width, surface.Height);

        // Reads the pre-clear content, so it has to run before the clear itself.
        surface.RecordProbeClear(clearEvent);

        surface.Clear();
        Stats.CalculationTime();

        // model => worldMatrix => world => viewMatrix => view => projectionMatrix => projection => toNdc => ndc => toScreen => screen

        painter?.Prepare(scene);
        events.Add(GraphicsEventKind.PainterPrepare, SceneObjectIds.Painter);

        var viewMatrix = camera.ViewMatrix;
        var projectionMatrix = projection.ProjectionMatrix(surface.Width, surface.Height);

        var eye = camera.Position;
        events.Add(GraphicsEventKind.CameraSetViewMatrix, SceneObjectIds.Camera, eye.X, eye.Y, eye.Z);
        events.Add(GraphicsEventKind.ProjectionSetProjectionMatrix, SceneObjectIds.Projection,
            projection.ZNear, projection.ZFar, surface.Width / (float)surface.Height);

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
            var objectId = meshIdBase + idxVolume;

            var worldMatrix = mesh.WorldMatrix;
            var modelViewMatrix = worldMatrix * viewMatrix;

            vbx.Mesh = mesh;
            vbx.WorldMatrix = worldMatrix;

            Stats.TotalTriangleCount += mesh.Triangles.Length;

            // Deactivated from the graphics object table.
            if (!mesh.Visible)
            {
                events.Add(GraphicsEventKind.MeshSkipInactive, objectId, mesh.Triangles.Length);
                continue;
            }

            // Whole-mesh rejection: if the mesh's bounding sphere is fully outside the
            // frustum, skip transforming its vertices and culling its triangles.
            var radius = mesh.BoundingRadius * MaxAbsComponent(mesh.Scale);
            if (!float.IsPositiveInfinity(radius))
            {
                var viewCenter = Vector3.Transform(Vector3.Zero, modelViewMatrix);
                if (IsSphereOutside(frustumPlanes, viewCenter, radius))
                {
                    Stats.OutOfViewTriangleCount += mesh.Triangles.Length;
                    events.Add(GraphicsEventKind.MeshCullBoundingSphere, objectId, mesh.Triangles.Length);
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

            events.Add(GraphicsEventKind.MeshTransformVertices, objectId, vertexCount);

            var triangleCount = mesh.Triangles.Length;
            var drawn = 0;
            var facingBack = 0;
            var clipped = 0;

            for (var idxTriangle = 0; idxTriangle < triangleCount; idxTriangle++)
            {
                Triangle t = mesh.Triangles[idxTriangle];

                // Discard if behind far plane
                if (t.IsBehindFarPlane(vbx))
                {
                    Stats.BehindViewTriangleCount++;
                    clipped++;
                    continue;
                }

                // Discard if back facing
                if (rendererSettings.BackFaceCulling && t.IsFacingBack(vbx))
                {
                    Stats.FacingBackTriangleCount++;
                    facingBack++;
                    continue;
                }

                // Project in frustum
                t.TransformProjection(vbx, projectionMatrix);

                // Discard if outside view frustum
                if (t.IsOutsideFrustum(vbx))
                {
                    Stats.OutOfViewTriangleCount++;
                    clipped++;
                    continue;
                }

                // Cache world positions and normals while still single-threaded, so the
                // parallel paint phase only reads the vertex buffer.
                t.TransformWorld(vbx);

                _visible.Add((idxVolume, idxTriangle));
                Stats.DrawnTriangleCount++;
                drawn++;
            }

            events.Add(GraphicsEventKind.MeshCullTriangles, objectId, drawn, facingBack, clipped);
        }

        Stats.PaintTime();

        // One draw event per mesh, in the order phase 1 collected them. Emitted before the
        // fill so the event list keeps pipeline order even though the fill runs in parallel.
        var drawEvents = RecordDrawEvents(events, meshIdBase, volumeCount, history is not null);

        // Phase 2 (parallel): fill the visible triangles. Each worker owns an
        // interleaved set of screen rows, so pixel writes never overlap.
        if (painter is not null && _visible.Count > 0)
        {
            var sliceCount = System.Math.Clamp(Environment.ProcessorCount, 1, 16);

            if (sliceCount == 1 || _visible.Count < 32 || !painter.SupportsRowSlices)
            {
                PaintSlice(painter, surface, meshes, worldBuffer, RowSlice.Full, drawEvents, meshIdBase);
            }
            else
            {
                Parallel.For(0, sliceCount, s =>
                    PaintSlice(painter, surface, meshes, worldBuffer, new RowSlice(s, sliceCount), drawEvents, meshIdBase));
            }
        }

        // The wireframe overlay draws lines across arbitrary rows, so it runs after the
        // parallel fills, sequentially. Drawing last also keeps the lines visible on top.
        if (rendererSettings.ShowTriangles)
        {
            var wireFrameEvent = events.Add(GraphicsEventKind.WireFrameOverlayDraw, -1, _visible.Count);

            foreach (var (meshIndex, triangleIndex) in _visible)
            {
                if (drawEvents is not null)
                {
                    FrameBuffer.SetProbeContext(wireFrameEvent, PixelWriteSource.WireFrame, meshIdBase + meshIndex, triangleIndex, worldBuffer.VertexBuffers[meshIndex]);
                }

                _internalWireFramePainter.DrawTriangle(surface, ColorRGB.Magenta, worldBuffer.VertexBuffers[meshIndex], triangleIndex, RowSlice.Full);
            }
        }

        if (rendererSettings.ShowXZGrid)
        {
            const float gridFrom = -10f;
            const float gridTo = 10f;

            // DrawGrid walks the range once, drawing a line along each axis per step.
            var gridLines = ((int)(gridTo - gridFrom) + 1) * 2;

            var gridEvent = events.Add(GraphicsEventKind.GizmoDrawGrid, -1, gridLines, gridFrom, gridTo);
            if (drawEvents is not null)
            {
                FrameBuffer.SetProbeContext(gridEvent, PixelWriteSource.Grid, -1, -1, null);
            }

            GizmoRenderer.DrawGrid(surface, viewMatrix * projectionMatrix, gridFrom, gridTo);
        }

        if (rendererSettings.ShowAxes)
        {
            var axesEvent = events.Add(GraphicsEventKind.GizmoDrawAxes);
            if (drawEvents is not null)
            {
                FrameBuffer.SetProbeContext(axesEvent, PixelWriteSource.Axes, -1, -1, null);
            }

            GizmoRenderer.DrawAxes(surface, viewMatrix * projectionMatrix);
        }

        Stats.StopTime();

        events.Add(GraphicsEventKind.FramePresent, SceneObjectIds.RenderTarget, Stats.DrawnPixelCount, Stats.BehindZPixelCount);

        if (history is not null)
        {
            history.FinalColor = surface.GetColor(history.X, history.Y);
            history.FinalDepth = surface.GetDepth(history.X, history.Y);
            surface.EndProbe();
        }
    }

    /// <summary>
    /// Emits one <see cref="GraphicsEventKind.PainterDrawTriangles"/> event per mesh that
    /// survived culling. Returns the per-mesh event indices when a pixel is being probed
    /// (the paint phase tags its writes with them), otherwise null.
    /// </summary>
    private int[]? RecordDrawEvents(GraphicsEventLog events, int meshIdBase, int meshCount, bool probing)
    {
        if (!events.IsEnabled && !probing)
        {
            return null;
        }

        if (_meshDrawEvent.Length < meshCount)
        {
            _meshDrawEvent = new int[System.Math.Max(meshCount, _meshDrawEvent.Length * 2)];
        }

        Array.Fill(_meshDrawEvent, -1, 0, meshCount);

        var count = _visible.Count;
        var i = 0;
        while (i < count)
        {
            var meshIndex = _visible[i].MeshIndex;

            var run = i;
            while (run < count && _visible[run].MeshIndex == meshIndex)
            {
                run++;
            }

            _meshDrawEvent[meshIndex] = events.Add(GraphicsEventKind.PainterDrawTriangles, meshIdBase + meshIndex, run - i);
            i = run;
        }

        return probing ? _meshDrawEvent : null;
    }

    private void PaintSlice(IPainter painter, FrameBuffer surface, List<IMesh> meshes, WorldBuffer worldBuffer, in RowSlice slice, int[]? drawEvents, int meshIdBase)
    {
        var count = _visible.Count;
        for (var i = 0; i < count; i++)
        {
            var (meshIndex, triangleIndex) = _visible[i];

            if (drawEvents is not null)
            {
                FrameBuffer.SetProbeContext(drawEvents[meshIndex], PixelWriteSource.Triangle, meshIdBase + meshIndex, triangleIndex, worldBuffer.VertexBuffers[meshIndex]);
            }

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
