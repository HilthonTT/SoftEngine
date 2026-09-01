using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Pipeline.Culling;
using SoftEngine.Core.Pipeline.Debugging;
using SoftEngine.Core.Pipeline.PostProcess;
using SoftEngine.Core.Pipeline.Shadows;
using SoftEngine.Core.Pipeline.Temporal;
using SoftEngine.Core.Rasterization;
using SoftEngine.Core.Rasterization.Painters;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Cameras;
using SoftEngine.Core.Scenes.Projections;
using SoftEngine.Core.Shading;
using System.Numerics;
using System.Runtime.InteropServices;

namespace SoftEngine.Core.Pipeline;

public sealed partial class Renderer : IRenderer
{
    private const int ParallelFillThreshold = 64;

    private const int DepthBoundRefreshInterval = 4;

    private readonly WireFramePainter _internalWireFramePainter = new();

    private readonly List<(int MeshIndex, int TriangleIndex)> _visible = [];

    private readonly List<(int MeshIndex, int TriangleIndex)> _transparent = [];
    private float[] _transparentKeys = [];
    private (int MeshIndex, int TriangleIndex)[] _transparentOrder = [];

    private readonly TileBinner _opaqueBins = new();
    private readonly TileBinner _transparentBins = new();

    private readonly FragmentBuffer _fragments = new();

    private int[] _meshDrawEvent = [];

    private int[] _meshOrder = [];
    private float[] _meshDepth = [];
    private Matrix4x4[] _meshWorld = [];

    private WorldBuffer? _worldBuffer;

    private ShadowMapRenderer? _shadowRenderer;

    private BufferVisualizer? _visualizer;

    private readonly OcclusionCuller _occlusion = new();

    private readonly MotionState _motion = new();
    private readonly VelocityPass _velocityPass = new();
    private readonly VelocityBuffer _velocity = new();

    private TemporalResolver? _temporal;

    public RendererSettings Settings { get; set; } = new();

    public VelocityBuffer Velocity => _velocity;

    public TemporalResolver Temporal => _temporal ??= new TemporalResolver();

    public MotionBlur MotionBlur { get; } = new();

    public FragmentBuffer Fragments => _fragments;

    public void ResetHistory()
    {
        _motion.Reset();
        _velocityPass.Reset();
        _temporal?.Reset();
    }

    public OcclusionCuller Occlusion => _occlusion;

    public PostProcessStack? PostProcess { get; set; }

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

        var history = BeginFrame(scene, surface, projection, rendererSettings, diagnostics, events);

        scene.ShadowMap = RenderShadowMap(scene, events, painter);

        painter?.Prepare(scene);
        events.Add(GraphicsEventKind.PainterPrepare, SceneObjectIds.Painter);

        var viewMatrix = camera.ViewMatrix;
        var projectionMatrix = projection.ProjectionMatrix(surface.Width, surface.Height);

        var temporal = rendererSettings.TemporalAntiAliasing;
        var motionBlur = rendererSettings.MotionBlur;

        RenderVelocity(
            world,
            surface,
            viewMatrix * projectionMatrix,
            events,
            temporal || motionBlur || rendererSettings.DebugView == DebugView.Velocity);

        if (temporal)
        {
            projectionMatrix = TemporalJitter.Apply(
                projectionMatrix,
                TemporalJitter.Offset(diagnostics.FrameNumber),
                surface.Width,
                surface.Height);
        }

        RecordCameraEvents(camera, projection, surface, events);

        Span<Vector4> frustumPlanes = stackalloc Vector4[Frustum.PlaneCount];
        Frustum.Build(projectionMatrix, frustumPlanes);

        var occlusion = PrepareOcclusion(
            world, surface, rendererSettings, viewMatrix, projectionMatrix, frustumPlanes, events);

        var worldBuffer = EnsureWorldBuffer(world);

        List<IMesh> meshes = world.Meshes;
        int volumeCount = meshes.Count;

        var meshOrder = volumeCount > 1 && rendererSettings.NearestMeshesFirst &&
            !surface.IsProbing && !events.IsEnabled
                ? NearestFirstMeshOrder(meshes, volumeCount, viewMatrix)
                : null;

        CullPhase(
            meshes,
            volumeCount,
            meshOrder,
            worldBuffer,
            events,
            meshIdBase,
            viewMatrix,
            projectionMatrix,
            frustumPlanes,
            occlusion,
            rendererSettings.BackFaceCulling);

        Stats.PaintTime();

        var drawEvents = RecordDrawEvents(events, meshIdBase, volumeCount, history is not null);

        var parallelFill = painter is { SupportsTiles: true } && Environment.ProcessorCount > 1;

        PaintOpaque(painter, surface, meshes, worldBuffer, drawEvents, meshIdBase, parallelFill);

        DrawSky(scene, surface, events);

        var orderIndependent = rendererSettings.OrderIndependentTransparency;
        var transparentCount = SortTransparent(worldBuffer, orderIndependent);

        if (painter is not null && transparentCount > 0)
        {
            PaintTransparent(
                painter, surface, meshes, worldBuffer, transparentCount,
                parallelFill, orderIndependent, drawEvents, meshIdBase, events);
        }

        DrawOverlays(
            scene,
            surface,
            worldBuffer,
            rendererSettings,
            viewMatrix * projectionMatrix,
            events,
            drawEvents,
            meshIdBase,
            transparentCount);

        ApplyTemporalEffects(surface, events, temporal, motionBlur);

        FrameResolvePass.Resolve(surface, projection, PostProcess, events);

        if (rendererSettings.DebugView != DebugView.Off)
        {
            FrameResolvePass.RenderDebugView(
                ref _visualizer,
                surface,
                scene,
                projection,
                events,
                rendererSettings.DebugView,
                occlusion?.Buffer,
                _velocity);
        }

        EndFrame(surface, world, viewMatrix, projection, diagnostics, events, history);
    }

    private int[] NearestFirstMeshOrder(List<IMesh> meshes, int meshCount, in Matrix4x4 viewMatrix)
    {
        if (_meshOrder.Length < meshCount)
        {
            var capacity = System.Math.Max(meshCount, _meshOrder.Length * 2);

            _meshOrder = new int[capacity];
            _meshDepth = new float[capacity];
            _meshWorld = new Matrix4x4[capacity];
        }

        for (var i = 0; i < meshCount; i++)
        {
            var worldMatrix = meshes[i].WorldMatrix;
            _meshWorld[i] = worldMatrix;

            _meshDepth[i] = -Vector3.Transform(Vector3.Zero, worldMatrix * viewMatrix).Z;
            _meshOrder[i] = i;
        }

        _meshDepth.AsSpan(0, meshCount).Sort(_meshOrder.AsSpan(0, meshCount));

        return _meshOrder;
    }

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

        RecordDrawEventRuns(events, meshIdBase, _visible);
        RecordDrawEventRuns(events, meshIdBase, _transparent);

        return probing ? _meshDrawEvent : null;
    }

    private void RecordDrawEventRuns(GraphicsEventLog events, int meshIdBase, List<(int MeshIndex, int TriangleIndex)> list)
    {
        var count = list.Count;
        var i = 0;
        while (i < count)
        {
            var meshIndex = list[i].MeshIndex;

            var run = i;
            while (run < count && list[run].MeshIndex == meshIndex)
            {
                run++;
            }

            _meshDrawEvent[meshIndex] = events.Add(GraphicsEventKind.PainterDrawTriangles, meshIdBase + meshIndex, run - i);
            i = run;
        }
    }

    private const int ParallelBinThreshold = 2048;

    private const int BinBandSize = 512;

    private (int MeshIndex, int TriangleIndex)[] _binSource = [];
    private float[] _binBounds = [];

    private void BinTriangles(
        TileBinner bins,
        FrameBuffer surface,
        WorldBuffer worldBuffer,
        ReadOnlySpan<(int MeshIndex, int TriangleIndex)> list,
        int count)
    {
        bins.Reset(surface.Width, surface.Height);

        if (count < ParallelBinThreshold || Environment.ProcessorCount <= 1)
        {
            for (var i = 0; i < count; i++)
            {
                var (minX, minY, maxX, maxY, minZ) = Bounds(surface, worldBuffer, list[i]);
                bins.Add(minX, minY, maxX, maxY, minZ);
            }

            bins.Build();
            return;
        }

        if (_binSource.Length < count)
        {
            _binSource = new (int, int)[System.Math.Max(count, _binSource.Length * 2)];
            _binBounds = new float[_binSource.Length * 5];
        }

        var source = _binSource;
        var bounds = _binBounds;

        list[..count].CopyTo(source);

        var bands = (count + BinBandSize - 1) / BinBandSize;

        Parallel.For(0, bands, band =>
        {
            var from = band * BinBandSize;
            var to = System.Math.Min(from + BinBandSize, count);

            for (var i = from; i < to; i++)
            {
                var (minX, minY, maxX, maxY, minZ) = Bounds(surface, worldBuffer, source[i]);

                var slot = i * 5;
                bounds[slot] = minX;
                bounds[slot + 1] = minY;
                bounds[slot + 2] = maxX;
                bounds[slot + 3] = maxY;
                bounds[slot + 4] = minZ;
            }
        });

        for (var i = 0; i < count; i++)
        {
            var slot = i * 5;
            bins.Add(bounds[slot], bounds[slot + 1], bounds[slot + 2], bounds[slot + 3], bounds[slot + 4]);
        }

        bins.Build();
    }

    private static (float MinX, float MinY, float MaxX, float MaxY, float MinZ) Bounds(
        FrameBuffer surface,
        WorldBuffer worldBuffer,
        (int MeshIndex, int TriangleIndex) entry)
    {
        var vbx = worldBuffer.VertexBuffers[entry.MeshIndex];
        var t = vbx.GetTriangle(entry.TriangleIndex);

        var p0 = surface.ToScreen3(vbx.GetVertex(t.I0).Proj);
        var p1 = surface.ToScreen3(vbx.GetVertex(t.I1).Proj);
        var p2 = surface.ToScreen3(vbx.GetVertex(t.I2).Proj);

        return (
            MathF.Min(p0.X, MathF.Min(p1.X, p2.X)),
            MathF.Min(p0.Y, MathF.Min(p1.Y, p2.Y)),
            MathF.Max(p0.X, MathF.Max(p1.X, p2.X)),
            MathF.Max(p0.Y, MathF.Max(p1.Y, p2.Y)),
            MathF.Min(p0.Z, MathF.Min(p1.Z, p2.Z)));
    }

    private void BinTriangles(TileBinner bins, FrameBuffer surface, WorldBuffer worldBuffer, List<(int MeshIndex, int TriangleIndex)> list, int count) =>
        BinTriangles(bins, surface, worldBuffer, CollectionsMarshal.AsSpan(list), count);

    private void BinTriangles(TileBinner bins, FrameBuffer surface, WorldBuffer worldBuffer, (int MeshIndex, int TriangleIndex)[] list, int count) =>
        BinTriangles(bins, surface, worldBuffer, list.AsSpan(0, count), count);

    private void PaintOpaqueTile(IPainter painter, FrameBuffer surface, List<IMesh> meshes, WorldBuffer worldBuffer, int tileIndex, int[]? drawEvents, int meshIdBase)
    {
        var ordinals = _opaqueBins.TrianglesIn(tileIndex);
        if (ordinals.Length == 0)
        {
            return;
        }

        var tile = _opaqueBins.TileAt(tileIndex);

        var hierarchicalZ = Settings.HierarchicalZ && !surface.IsProbing;
        var bound = int.MaxValue;
        var interval = DepthBoundRefreshInterval;
        var sinceRefresh = 0;
        var rejectedSinceRefresh = 0;
        var rejected = 0;

        foreach (var ordinal in ordinals)
        {
            if (_opaqueBins.NearestDepth(ordinal) > bound)
            {
                rejected++;
                rejectedSinceRefresh++;
                continue;
            }

            var (meshIndex, triangleIndex) = _visible[ordinal];
            PaintTriangle(painter, surface, meshes, worldBuffer, meshIndex, triangleIndex, tile, drawEvents, meshIdBase);

            if (hierarchicalZ && ++sinceRefresh >= interval)
            {
                bound = surface.MaxDepthIn(tile.XFrom, tile.YFrom, tile.XTo, tile.YTo);

                interval = rejectedSinceRefresh == 0 ? interval * 2 : DepthBoundRefreshInterval;
                sinceRefresh = 0;
                rejectedSinceRefresh = 0;
            }
        }

        if (rejected > 0)
        {
            Stats.AddOccludedTriangles(rejected);
        }
    }

    private void PaintTransparent(
        IPainter painter,
        FrameBuffer surface,
        List<IMesh> meshes,
        WorldBuffer worldBuffer,
        int transparentCount,
        bool parallelFill,
        bool orderIndependent,
        int[]? drawEvents,
        int meshIdBase,
        GraphicsEventLog events)
    {
        var tiled = false;

        if (parallelFill)
        {
            BinTriangles(_transparentBins, surface, worldBuffer, _transparentOrder, transparentCount);
            tiled = _transparentBins.TotalItems >= ParallelFillThreshold;
        }

        if (!orderIndependent)
        {
            if (tiled)
            {
                Parallel.For(0, _transparentBins.TileCount, t =>
                    PaintTransparentTile(painter, surface, meshes, worldBuffer, t, drawEvents, meshIdBase));
            }
            else
            {
                PaintTransparentAll(painter, surface, meshes, worldBuffer, transparentCount, drawEvents, meshIdBase);
            }

            return;
        }

        _fragments.Begin(tiled ? _transparentBins.TileCount : 1, surface.IsProbing);

        if (tiled)
        {
            Parallel.For(0, _transparentBins.TileCount, t =>
            {
                if (_transparentBins.TrianglesIn(t).Length == 0)
                {
                    return;
                }

                var tile = _transparentBins.TileAt(t);

                FrameBuffer.SetFragmentArena(
                    _fragments.ArenaFor(t, tile.XFrom, tile.YFrom, tile.XTo, tile.YTo));

                try
                {
                    PaintTransparentTile(painter, surface, meshes, worldBuffer, t, drawEvents, meshIdBase);
                }
                finally
                {
                    FrameBuffer.SetFragmentArena(null);
                }
            });
        }
        else
        {
            FrameBuffer.SetFragmentArena(_fragments.ArenaFor(0, 0, 0, surface.Width, surface.Height));

            try
            {
                PaintTransparentAll(painter, surface, meshes, worldBuffer, transparentCount, drawEvents, meshIdBase);
            }
            finally
            {
                FrameBuffer.SetFragmentArena(null);
            }
        }

        _fragments.Resolve(surface);

        Stats.TransparentFragmentCount = _fragments.FragmentCount;
        Stats.TransparentPixelCount = _fragments.CoveredPixelCount;
        Stats.TransparentOverflowCount = _fragments.OverflowCount;

        events.Add(
            GraphicsEventKind.TransparencyResolve,
            SceneObjectIds.RenderTarget,
            _fragments.FragmentCount,
            _fragments.CoveredPixelCount,
            _fragments.OverflowCount);
    }

    private void PaintTransparentTile(IPainter painter, FrameBuffer surface, List<IMesh> meshes, WorldBuffer worldBuffer, int tileIndex, int[]? drawEvents, int meshIdBase)
    {
        var ordinals = _transparentBins.TrianglesIn(tileIndex);
        if (ordinals.Length == 0)
        {
            return;
        }

        var tile = _transparentBins.TileAt(tileIndex);

        var bound = Settings.HierarchicalZ && !surface.IsProbing
            ? surface.MaxDepthIn(tile.XFrom, tile.YFrom, tile.XTo, tile.YTo)
            : int.MaxValue;

        var rejected = 0;

        foreach (var ordinal in ordinals)
        {
            if (_transparentBins.NearestDepth(ordinal) > bound)
            {
                rejected++;
                continue;
            }

            var (meshIndex, triangleIndex) = _transparentOrder[ordinal];
            PaintTriangle(painter, surface, meshes, worldBuffer, meshIndex, triangleIndex, tile, drawEvents, meshIdBase);
        }

        if (rejected > 0)
        {
            Stats.AddOccludedTriangles(rejected);
        }
    }

    private void PaintAll(IPainter painter, FrameBuffer surface, List<IMesh> meshes, WorldBuffer worldBuffer, int[]? drawEvents, int meshIdBase)
    {
        var count = _visible.Count;
        for (var i = 0; i < count; i++)
        {
            var (meshIndex, triangleIndex) = _visible[i];
            PaintTriangle(painter, surface, meshes, worldBuffer, meshIndex, triangleIndex, ScreenTile.Full, drawEvents, meshIdBase);
        }
    }

    private void PaintTransparentAll(IPainter painter, FrameBuffer surface, List<IMesh> meshes, WorldBuffer worldBuffer, int count, int[]? drawEvents, int meshIdBase)
    {
        for (var i = 0; i < count; i++)
        {
            var (meshIndex, triangleIndex) = _transparentOrder[i];
            PaintTriangle(painter, surface, meshes, worldBuffer, meshIndex, triangleIndex, ScreenTile.Full, drawEvents, meshIdBase);
        }
    }

    private static void PaintTriangle(IPainter painter, FrameBuffer surface, List<IMesh> meshes, WorldBuffer worldBuffer, int meshIndex, int triangleIndex, in ScreenTile tile, int[]? drawEvents, int meshIdBase)
    {
        var vbx = worldBuffer.VertexBuffers[meshIndex];

        var sourceIndex = vbx.SourceTriangleIndex(triangleIndex);

        if (drawEvents is not null)
        {
            FrameBuffer.SetProbeContext(drawEvents[meshIndex], PixelWriteSource.Triangle, meshIdBase + meshIndex, sourceIndex, vbx);
        }

        painter.DrawTriangle(
            surface,
            meshes[meshIndex].TriangleColors[sourceIndex],
            vbx,
            triangleIndex,
            tile);
    }

    private int SortTransparent(WorldBuffer worldBuffer, bool orderIndependent)
    {
        var count = _transparent.Count;
        if (count == 0)
        {
            return 0;
        }

        if (orderIndependent)
        {
            if (_transparentOrder.Length < count)
            {
                var size = System.Math.Max(count, _transparentOrder.Length * 2);
                _transparentKeys = new float[size];
                _transparentOrder = new (int, int)[size];
            }

            CollectionsMarshal.AsSpan(_transparent).CopyTo(_transparentOrder);
            return count;
        }

        if (_transparentKeys.Length < count)
        {
            var capacity = System.Math.Max(count, _transparentKeys.Length * 2);
            _transparentKeys = new float[capacity];
            _transparentOrder = new (int, int)[capacity];
        }

        for (var i = 0; i < count; i++)
        {
            var (meshIndex, triangleIndex) = _transparent[i];
            var vbx = worldBuffer.VertexBuffers[meshIndex];
            var t = vbx.GetTriangle(triangleIndex);

            _transparentKeys[i] = -(vbx.GetVertex(t.I0).Proj.W + vbx.GetVertex(t.I1).Proj.W + vbx.GetVertex(t.I2).Proj.W);
            _transparentOrder[i] = (meshIndex, triangleIndex);
        }

        Array.Sort(_transparentKeys, _transparentOrder, 0, count);
        return count;
    }
}
