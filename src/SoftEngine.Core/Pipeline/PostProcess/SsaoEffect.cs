using System.Numerics;

namespace SoftEngine.Core.Pipeline.PostProcess;

public sealed class SsaoEffect : IPostEffect
{
    private static readonly Vector3[] _kernel = BuildKernel(16);
    private static readonly Vector2[] _rotations = BuildRotations(16);

    private float[] _occlusion = [];
    private float[] _blurred = [];

    public string Name => "SSAO";

    public bool Enabled { get; set; }

    public bool NeedsDepth => true;

    public float Strength { get; set; } = 0.6f;

    public float Radius { get; set; } = 0.5f;

    public float RangeCutoff { get; set; } = 1f;

    public float Bias { get; set; } = 0.02f;

    public int BlurRadius { get; set; } = 2;

    public void Apply(PostProcessTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

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

                var rotation = rotations[(x & 3) + (y & 3) * 4];

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

                    if (sceneDistance >= sampleDistance - bias)
                    {
                        continue;
                    }

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

            var t = (i + 1) / (float)count;
            kernel[i] = v * (0.1f + 0.9f * t * t);
        }

        return kernel;
    }

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
