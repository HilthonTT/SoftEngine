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

/// <summary>
/// The reference renderer: the same scene, answered by tracing light through it instead of by
/// filling triangles.
///
/// <para>
/// Everything the rasterizer does about light that does not arrive straight from a lamp is an
/// approximation standing in for this. Ambient light is a constant, or six of them. Occlusion is a
/// screen-space guess made from a depth buffer. A reflection is a prefiltered cube looked up along
/// one direction, and shadows are a depth map with a bias that has to be tuned. Each of those is
/// defensible on its own and none of them can be checked against anything — which is the problem
/// this solves. A path tracer computes the same integral by following actual paths, so the
/// approximations have something to be wrong *against*: render both, subtract, and the difference
/// is the error rather than an opinion.
/// </para>
///
/// <para>
/// It is an <see cref="IRenderer"/>, so it drops into the same slot the rasterizer and the GPU
/// backend occupy — same scene, same <see cref="FrameBuffer"/>, same post-process stack — and it
/// ignores the <see cref="IPainter"/> it is handed, because choosing a shading model per mesh is
/// exactly the thing it is here to not have to do.
/// </para>
///
/// <para>
/// What it is not: a production renderer. There is no bidirectional path tracing, no multiple
/// importance sampling and no light hierarchy, so a scene lit through a keyhole will be noise for a
/// very long time. Lights are the engine's own delta lights, sampled directly and shadowed with one
/// ray, so they cast hard shadows and no caustics. What it does have is unbiased diffuse and
/// specular interreflection, true ambient occlusion, and shadows with no bias to tune — the three
/// things nothing else here can produce.
/// </para>
///
/// <para>
/// The path walk itself is <see cref="PathIntegrator"/>, which <see cref="Baking.IrradianceBaker"/>
/// also asks — so what a bake stores as ambient light is what this renderer would have found there.
/// A baked <see cref="Shading.IrradianceVolume"/> on the scene is ignored here: this computes the
/// thing the volume is an approximation of.
/// </para>
/// </summary>
public sealed class PathTracer : IRenderer
{
    private Bvh? _accelerator;
    private int _geometryStamp;

    /// <summary>Running sum of radiance per pixel, three floats each, when accumulating.</summary>
    private float[] _accumulator = [];
    private float[] _depth = [];

    private int _width;
    private int _height;

    public RendererSettings Settings { get; set; } = new();

    public PostProcessStack? PostProcess { get; set; }

    public RenderStats Stats { get; } = new();

    public RenderDiagnostics Diagnostics { get; } = new();

    /// <summary>How many paths per pixel to spend, and what they are allowed to do.</summary>
    public TraceSettings Trace { get; } = new();

    /// <summary>
    /// The tree the last render built, or null before the first one. Exposed because building it is
    /// the expensive half of a first frame and a caller may want to know what it got — how many
    /// nodes, how deep — or to reuse it for something else that casts rays.
    /// </summary>
    public Bvh? Accelerator => _accelerator;

    /// <summary>Paths per pixel averaged into the image so far. Always <see cref="TraceSettings.SamplesPerPixel"/> unless accumulating.</summary>
    public int AccumulatedSamples { get; private set; }

    /// <summary>Throws away the accumulated image, so the next render starts from nothing.</summary>
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

        // The walk itself lives in PathIntegrator, which the irradiance bake asks the same question
        // of from points that have no pixel. Sharing it is what keeps a baked ambient term and a
        // traced one from being two different opinions about the same light.
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

        // The view-projection, for turning a hit position into the normalized depth the buffer
        // holds — so the depth view and the depth-reading post effects have the frame's geometry
        // even though no triangle was ever projected.
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

                    // Jittered inside the pixel: the sample position *is* the antialiasing, since
                    // there is no coverage to compute and nothing to supersample afterwards.
                    var ray = camera.Through(
                        x + sampler.Next(),
                        y + sampler.Next());

                    sum += integrator.Radiance(ray, ref sampler, out var distance);

                    // The depth buffer is one number per pixel and this is a distribution, so it
                    // records the first sample's hit rather than an average of positions that may
                    // be on different surfaces. Accumulating leaves the first frame's, which is the
                    // one the geometry has not moved since.
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

    /// <summary>
    /// Rebuilds the acceleration structure when the world has moved, and the accumulation buffers
    /// when the frame has changed size. Either invalidates whatever was accumulated: an average of
    /// samples taken against different geometry is not an image of anything.
    /// </summary>
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

    /// <summary>
    /// Where a world position lands in the depth buffer's [0, 1]. The same divide the rasterizer's
    /// clip-space z goes through, so a traced depth buffer and a rasterized one hold the same
    /// numbers for the same surface.
    /// </summary>
    private static float NormalizedDepth(Vector3 point, in Matrix4x4 viewProjection)
    {
        var clip = Vector4.Transform(new Vector4(point, 1f), viewProjection);

        if (clip.W <= 0f)
        {
            return 1f;
        }

        return System.Math.Clamp(clip.Z / clip.W, 0f, 1f);
    }

    /// <summary>
    /// The same resolve the rasterizer ends a frame with: the post-process stack when there is one,
    /// and otherwise the encode an HDR target still needs.
    /// </summary>
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

    /// <summary>
    /// The camera, reduced to what firing a ray through a pixel needs.
    ///
    /// <see cref="ScenePicker.RayThrough"/> answers the same question and inverts the projection
    /// every time it is asked, which is right for one click a second and wrong for a million rays a
    /// frame. The arithmetic is deliberately identical, so a traced pixel and a picked one look
    /// along the same line.
    /// </summary>
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

            // A parallel projection fires every ray the same way and moves the origin instead; a
            // perspective one fires them all from the eye.
            var (origin, direction) = _orthographic
                ? (new Vector3(ndcX * _invScaleX, ndcY * _invScaleY, 0f), -Vector3.UnitZ)
                : (Vector3.Zero, new Vector3(ndcX * _invScaleX, ndcY * _invScaleY, -1f));

            return new Ray(
                Vector3.Transform(origin, _inverseView),
                Vector3.Normalize(Vector3.TransformNormal(direction, _inverseView)));
        }

    }
}
