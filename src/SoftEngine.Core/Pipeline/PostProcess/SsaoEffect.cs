using System.Numerics;

namespace SoftEngine.Core.Pipeline.PostProcess;

/// <summary>
/// Screen-space ambient occlusion: darkens the creases and contact points a shadow map
/// cannot resolve, using nothing but the depth buffer the frame already has.
///
/// The idea is that the depth buffer is a partial model of the scene — for every pixel, one
/// point in space and, by differencing its neighbours, the surface's orientation there.
/// Given those, a point is occluded to the extent that other points sit above its own
/// tangent plane nearby: sample a hemisphere around the normal, project each sample back to
/// the screen, and ask whether the geometry recorded there is closer to the eye than the
/// sample was. The fraction that are is the occlusion.
///
/// Two things about it are worth being clear on:
///
/// It only knows what is on screen. Geometry outside the frame, or hidden behind something
/// nearer, occludes nothing, so the effect weakens toward the frame's edges and can change
/// as the camera moves. That is inherent to the technique, not a shortcoming of this one.
///
/// It multiplies the finished image, which darkens direct light along with ambient. Strictly
/// only the ambient term should be occluded — a surface lit head-on by a lamp is not made
/// darker by a wall behind it — but separating the two in a forward renderer means carrying
/// the ambient in a buffer of its own through the whole frame. Multiplying the composite is
/// the usual compromise, and <see cref="Strength"/> is the dial for how far to take it.
/// </summary>
public sealed class SsaoEffect : IPostEffect
{
    // Fixed sample kernel and per-pixel rotations, generated once. Fixed so a frame is
    // reproducible: an effect that used a live random source would shimmer between two
    // renders of an identical scene, and could not be tested.
    private static readonly Vector3[] _kernel = BuildKernel(16);
    private static readonly Vector2[] _rotations = BuildRotations(16);

    private float[] _occlusion = [];
    private float[] _blurred = [];

    public string Name => "SSAO";

    public bool Enabled { get; set; }

    public bool NeedsDepth => true;

    /// <summary>How dark a fully occluded pixel goes: 0 is no effect, 1 is black.</summary>
    public float Strength { get; set; } = 0.6f;

    /// <summary>
    /// The world-space distance occlusion is gathered over. This is the single most
    /// scene-dependent number here: a radius that finds the creases in a 2-unit skull sees
    /// nothing at all on a 1500-unit elephant. Scale it with the world.
    /// </summary>
    public float Radius { get; set; } = 0.5f;

    /// <summary>
    /// How far behind a sample the recorded geometry may be and still count as occluding
    /// it, as a multiple of <see cref="Radius"/>. Without it, a distant wall seen just past
    /// a silhouette would be treated as pressed against it, drawing a dark halo around
    /// every foreground object.
    /// </summary>
    public float RangeCutoff { get; set; } = 1f;

    /// <summary>
    /// Depth difference below which a sample is ignored, in world units. Absorbs the
    /// self-occlusion a flat surface would otherwise report from its own quantized depth.
    /// </summary>
    public float Bias { get; set; } = 0.02f;

    /// <summary>Half-width of the box blur that removes the sampling noise, in pixels.</summary>
    public int BlurRadius { get; set; } = 2;

    public void Apply(PostProcessTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        // Without depth there is no scene to occlude — only an image.
        if (!target.HasDepth || target.Width < 3 || target.Height < 3 || Strength <= 0f)
        {
            return;
        }

        var count = target.Width * target.Height;
        if (_occlusion.Length < count)
        {
            _occlusion = new float[count];
            _blurred = new float[count];
        }

        Gather(target);
        Blur(target);
        Composite(target);
    }

    private void Gather(PostProcessTarget target)
    {
        var width = target.Width;
        var height = target.Height;
        var depth = target.ViewDepth;
        var field = target.Field;

        var radius = MathF.Max(1e-4f, Radius);
        var cutoff = radius * MathF.Max(0f, RangeCutoff);
        var bias = MathF.Max(0f, Bias);

        var occlusion = _occlusion;
        var kernel = _kernel;
        var rotations = _rotations;

        Parallel.For(0, height, y =>
        {
            for (var x = 0; x < width; x++)
            {
                var index = x + y * width;

                if (float.IsPositiveInfinity(depth[index]))
                {
                    // Background is not occluded by anything.
                    occlusion[index] = 0f;
                    continue;
                }

                var origin = field.PositionAt(x, y);
                var normal = field.NormalAt(x, y);

                if (normal == Vector3.Zero)
                {
                    occlusion[index] = 0f;
                    continue;
                }

                // A per-pixel rotation of the kernel turns the banding a shared sample set
                // would produce into noise, which the blur then removes. Four by four is
                // enough that the pattern is finer than the blur is wide.
                var rotation = rotations[(x & 3) + (y & 3) * 4];

                // Gram-Schmidt against the normal, using the rotation as the seed direction,
                // gives an orthonormal frame whose Z is the normal — so a kernel sample in
                // the +Z hemisphere lands in the hemisphere above the surface.
                var seed = new Vector3(rotation.X, rotation.Y, 0f);
                var tangent = seed - normal * Vector3.Dot(seed, normal);

                if (tangent.LengthSquared() < 1e-8f)
                {
                    tangent = Vector3.Cross(normal, Vector3.UnitY);
                    if (tangent.LengthSquared() < 1e-8f)
                    {
                        tangent = Vector3.Cross(normal, Vector3.UnitX);
                    }
                }

                tangent = Vector3.Normalize(tangent);
                var bitangent = Vector3.Cross(normal, tangent);

                var occluded = 0f;

                foreach (var sample in kernel)
                {
                    var offset = tangent * sample.X + bitangent * sample.Y + normal * sample.Z;
                    var point = origin + offset * radius;

                    if (!field.ProjectToScreen(point, out var sx, out var sy, out var sampleDistance))
                    {
                        continue;
                    }

                    var sceneDistance = depth[sx + sy * width];

                    if (float.IsPositiveInfinity(sceneDistance))
                    {
                        continue;
                    }

                    // The surface recorded at the sample's pixel is nearer than the sample
                    // itself: something is in the way, so the sample is occluded.
                    if (sceneDistance >= sampleDistance - bias)
                    {
                        continue;
                    }

                    // ...unless it is much nearer, in which case it is a different surface
                    // altogether rather than something pressed against this one.
                    var separation = sampleDistance - sceneDistance;
                    if (cutoff > 0f && separation > cutoff)
                    {
                        continue;
                    }

                    occluded += 1f;
                }

                occlusion[index] = occluded / kernel.Length;
            }
        });
    }

    /// <summary>
    /// Separable box blur over the occlusion buffer. The kernel rotation deliberately made
    /// the result noisy; this is the half of that trade that removes the noise again.
    /// </summary>
    private void Blur(PostProcessTarget target)
    {
        var radius = System.Math.Clamp(BlurRadius, 0, 16);
        if (radius == 0)
        {
            return;
        }

        var width = target.Width;
        var height = target.Height;

        var source = _occlusion;
        var destination = _blurred;

        Parallel.For(0, height, y =>
        {
            var row = y * width;

            for (var x = 0; x < width; x++)
            {
                var sum = 0f;
                var taken = 0;

                for (var k = -radius; k <= radius; k++)
                {
                    var sx = x + k;
                    if ((uint)sx >= (uint)width)
                    {
                        continue;
                    }

                    sum += source[row + sx];
                    taken++;
                }

                destination[row + x] = sum / taken;
            }
        });

        Parallel.For(0, height, y =>
        {
            for (var x = 0; x < width; x++)
            {
                var sum = 0f;
                var taken = 0;

                for (var k = -radius; k <= radius; k++)
                {
                    var sy = y + k;
                    if ((uint)sy >= (uint)height)
                    {
                        continue;
                    }

                    sum += destination[x + sy * width];
                    taken++;
                }

                source[x + y * width] = sum / taken;
            }
        });
    }

    private void Composite(PostProcessTarget target)
    {
        var color = target.Color;
        var occlusion = _occlusion;
        var width = target.Width;
        var strength = System.Math.Clamp(Strength, 0f, 1f);

        Parallel.For(0, target.Height, y =>
        {
            var pixel = y * width;
            var i = pixel * 3;

            for (var x = 0; x < width; x++, pixel++, i += 3)
            {
                var factor = 1f - occlusion[pixel] * strength;

                color[i] *= factor;
                color[i + 1] *= factor;
                color[i + 2] *= factor;
            }
        });
    }

    /// <summary>
    /// Sample offsets in the +Z hemisphere, pulled toward the origin so most of them probe
    /// close to the shaded point. Occlusion falls off with distance, so an evenly spread
    /// kernel spends most of its samples where they can contribute least.
    /// </summary>
    private static Vector3[] BuildKernel(int count)
    {
        var kernel = new Vector3[count];
        var random = new DeterministicRandom(0x5EED);

        for (var i = 0; i < count; i++)
        {
            Vector3 v;
            do
            {
                v = new Vector3(
                    random.NextFloat() * 2f - 1f,
                    random.NextFloat() * 2f - 1f,
                    random.NextFloat());
            }
            while (v.LengthSquared() is < 1e-4f or > 1f);

            v = Vector3.Normalize(v);

            // Quadratic in the sample's index: the first samples sit almost on the surface,
            // the last reach the full radius.
            var t = (i + 1) / (float)count;
            kernel[i] = v * (0.1f + 0.9f * t * t);
        }

        return kernel;
    }

    /// <summary>A 4×4 tile of unit vectors used to rotate the kernel per pixel.</summary>
    private static Vector2[] BuildRotations(int count)
    {
        var rotations = new Vector2[count];
        var random = new DeterministicRandom(0xC0FFEE);

        for (var i = 0; i < count; i++)
        {
            var angle = random.NextFloat() * MathF.Tau;
            rotations[i] = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        }

        return rotations;
    }

    /// <summary>
    /// A tiny xorshift, so the kernel is the same on every machine and every run. Nothing
    /// here needs statistical quality — only that two renders of the same scene agree.
    /// </summary>
    private struct DeterministicRandom(uint seed)
    {
        private uint _state = seed | 1u;

        public float NextFloat()
        {
            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;

            return (_state >> 8) * (1f / 16777216f);
        }
    }
}
