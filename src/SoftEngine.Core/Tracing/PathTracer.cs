using SoftEngine.Core.Acceleration;
using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Picking;
using SoftEngine.Core.Pipeline;
using SoftEngine.Core.Pipeline.PostProcess;
using SoftEngine.Core.Rasterization;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Shading;
using System.Numerics;

namespace SoftEngine.Core.Tracing;

public sealed class PathTracer : IRenderer
{
    private Bvh? _accelerator;
    private int _geometryStamp;

    private float[] _accumulator = [];
    private float[] _depth = [];

    private int _width;
    private int _height;

    public RendererSettings Settings { get; set; } = new();

    public PostProcessStack? PostProcess { get; set; }

    public RenderStats Stats { get; } = new();

    public RenderDiagnostics Diagnostics { get; } = new();

    public TraceSettings Trace { get; } = new();

    public Bvh? Accelerator => _accelerator;

    public int AccumulatedSamples { get; private set; }

    public void Reset() => AccumulatedSamples = 0;

    public void Render(Scene scene, IPainter? painter)
    {
        ArgumentNullException.ThrowIfNull(scene, nameof(scene));

        var surface = scene.Surface;
        var world = scene.World;

        Stats.Clear();
        Stats.PaintTime();

        Diagnostics.FrameNumber++;

        var events = Diagnostics.Events;
        events.Clear();
        events.Add(GraphicsEventKind.FrameBegin, -1, Diagnostics.FrameNumber);
        events.Add(GraphicsEventKind.RendererSetViewport, SceneObjectIds.RenderTarget, surface.Width, surface.Height);

        surface.SetHighDynamicRange(scene.HighDynamicRange);
        surface.Clear();

        Refresh(world, surface.Width, surface.Height);

        var accelerator = _accelerator!;
        var geometry = accelerator.Geometry;

        Stats.TotalTriangleCount = geometry.TriangleCount;

        if (surface.Width <= 0 || surface.Height <= 0)
        {
            Stats.StopTime();
            return;
        }

        var camera = new CameraFrame(scene);

        var integrator = new PathIntegrator(
            accelerator,
            PathIntegrator.Lights(world),
            Trace.LightFromEnvironment ? scene.Environment : null,
            MathF.Max(0f, scene.SkyIntensity),
            scene.ShowSky,
            Trace);

        var samples = System.Math.Max(1, Trace.SamplesPerPixel);
        var previous = Trace.Accumulate ? AccumulatedSamples : 0;
        var total = previous + samples;

        if (previous == 0)
        {
            Array.Clear(_accumulator);
        }

        var viewProjection = scene.Camera.ViewMatrix *
            scene.Projection.ProjectionMatrix(surface.Width, surface.Height);

        var width = surface.Width;
        var height = surface.Height;

        Parallel.For(0, height, y =>
        {
            for (var x = 0; x < width; x++)
            {
                var pixel = x + y * width;

                var sum = LinearColor.Black;
                var depth = 1f;

                for (var s = 0; s < samples; s++)
                {
                    var sampler = new Sampler(Trace.Seed, pixel, previous + s);

                    var ray = camera.Through(
                        x + sampler.Next(),
                        y + sampler.Next());

                    sum += integrator.Radiance(ray, ref sampler, out var distance);

                    if (s == 0 && previous == 0 && !float.IsPositiveInfinity(distance))
                    {
                        depth = NormalizedDepth(ray.At(distance), viewProjection);
                    }
                }

                var slot = pixel * 3;

                _accumulator[slot] += sum.R;
                _accumulator[slot + 1] += sum.G;
                _accumulator[slot + 2] += sum.B;

                var scale = 1f / total;

                surface.PutBackground(x, y, new LinearColor(
                    _accumulator[slot] * scale,
                    _accumulator[slot + 1] * scale,
                    _accumulator[slot + 2] * scale));

                if (previous == 0)
                {
                    _depth[pixel] = depth;
                }
            }
        });

        AccumulatedSamples = total;

        Stats.AddPixelCounts(width * height, 0);
        Stats.CalculationTime();

        surface.WriteNormalizedDepth(_depth);

        events.Add(GraphicsEventKind.FramePresent, SceneObjectIds.RenderTarget, Stats.DrawnPixelCount, 0);

        Resolve(surface, scene);

        Stats.StopTime();
    }

    private void Refresh(IWorld world, int width, int height)
    {
        var stamp = SceneGeometry.Stamp(world);

        if (_accelerator is null || stamp != _geometryStamp)
        {
            _accelerator = Bvh.Build(SceneGeometry.Build(world));
            _geometryStamp = stamp;

            AccumulatedSamples = 0;
        }

        if (_width == width && _height == height && _accumulator.Length == width * height * 3)
        {
            return;
        }

        _width = width;
        _height = height;

        _accumulator = new float[System.Math.Max(0, width * height * 3)];
        _depth = new float[System.Math.Max(0, width * height)];

        AccumulatedSamples = 0;
    }

    private static float NormalizedDepth(Vector3 point, in Matrix4x4 viewProjection)
    {
        var clip = Vector4.Transform(new Vector4(point, 1f), viewProjection);

        if (clip.W <= 0f)
        {
            return 1f;
        }

        return System.Math.Clamp(clip.Z / clip.W, 0f, 1f);
    }

    private void Resolve(FrameBuffer surface, Scene scene)
    {
        var stack = PostProcess is { HasEffects: true } candidate ? candidate : null;

        if (stack is not null)
        {
            stack.Apply(surface, scene.Projection);
        }
        else if (surface.IsHighDynamicRange)
        {
            surface.ResolveToScreen();
        }
    }

    private readonly struct CameraFrame
    {
        private readonly Matrix4x4 _inverseView;
        private readonly float _invScaleX;
        private readonly float _invScaleY;
        private readonly float _toNdcX;
        private readonly float _toNdcY;
        private readonly bool _orthographic;

        public CameraFrame(Scene scene)
        {
            var surface = scene.Surface;
            var projection = scene.Projection;

            var matrix = projection.ProjectionMatrix(surface.Width, surface.Height);

            _invScaleX = 1f / (matrix.M11 == 0f ? 1f : matrix.M11);
            _invScaleY = 1f / (matrix.M22 == 0f ? 1f : matrix.M22);

            _toNdcX = 2f / MathF.Max(surface.Width - 1, 1);
            _toNdcY = 2f / MathF.Max(surface.Height - 1, 1);

            _orthographic = projection.IsOrthographic;

            _inverseView = Matrix4x4.Invert(scene.Camera.ViewMatrix, out var inverse)
                ? inverse
                : Matrix4x4.Identity;
        }

        public Ray Through(float x, float y)
        {
            var ndcX = x * _toNdcX - 1f;
            var ndcY = 1f - y * _toNdcY;

            var (origin, direction) = _orthographic
                ? (new Vector3(ndcX * _invScaleX, ndcY * _invScaleY, 0f), -Vector3.UnitZ)
                : (Vector3.Zero, new Vector3(ndcX * _invScaleX, ndcY * _invScaleY, -1f));

            return new Ray(
                Vector3.Transform(origin, _inverseView),
                Vector3.Normalize(Vector3.TransformNormal(direction, _inverseView)));
        }
    }
}
