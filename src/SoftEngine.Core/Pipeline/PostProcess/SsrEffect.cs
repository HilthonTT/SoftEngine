using SoftEngine.Core.Buffers;
using SoftEngine.Core.Shading;
using System.Numerics;

namespace SoftEngine.Core.Pipeline.PostProcess;

/// <summary>
/// Screen-space reflections: the local scene reflected in the surfaces that reflect it, by
/// marching each pixel's reflected ray through the depth buffer and reading back the colour
/// of whatever it runs into.
///
/// <para>
/// What this adds, and what it does not, is worth being exact about. The physically-based
/// path already reflects the <em>environment</em> — a prefiltered cube map, sampled per
/// roughness — so a metal in this engine has never been black. What it cannot reflect is the
/// scene: the cube map is a picture of the sky, and it does not know there is a red wall two
/// metres away. This pass answers only that, and only where the answer is on screen.
/// </para>
///
/// <para>
/// It therefore <em>replaces</em> rather than adds. A pixel's colour already contains that
/// surface's environment reflection at full Fresnel weight, so adding a scene reflection on
/// top would light the same surface twice; the composite blends toward the reflected colour
/// by the same weight the shader used, which is the closest a forward renderer gets to
/// "the local scene was nearer than the sky, so use it instead". Where the march finds
/// nothing, the weight is zero and the environment reflection is left exactly as the shader
/// wrote it — so the failure mode of every screen-space reflection, the ray that leaves the
/// frame, degrades to the picture this engine drew before there was a reflection pass.
/// </para>
///
/// <para>
/// The three things it cannot see are inherent, not shortcomings of this one. Geometry off
/// screen reflects nothing, which is why the confidence fades toward the frame's edges rather
/// than stopping at them. Nothing hidden behind a nearer surface can be reflected, because
/// the depth buffer records one layer — a floor cannot reflect the underside of the object
/// standing on it. And a surface facing away from the camera has no pixels for the march to
/// find, so a mirror pointed at the back of a model reflects its front.
/// </para>
/// </summary>
public sealed class SsrEffect : IPostEffect
{
    // Reflection colour and its confidence, kept apart from the image so the blur below can
    // smooth the march's own noise without smearing the frame.
    private float[] _reflection = [];
    private float[] _weight = [];
    private float[] _blurredReflection = [];
    private float[] _blurredWeight = [];

    // Schlick's (1 - cos)^5 per pixel, kept from the march so the composite can finish the
    // Fresnel term per channel without deriving the surface normal a second time.
    private float[] _grazing = [];

    public string Name => "Reflections";

    public bool Enabled { get; set; }

    public bool NeedsDepth => true;

    public bool NeedsReflectance => true;

    /// <summary>
    /// Scales the whole effect: 1 reflects as much as the surface's Fresnel says it should,
    /// 0 is off. A dial rather than a fixed physical answer because the technique's errors
    /// scale with it too, and a scene where the march misses often is better understated.
    /// </summary>
    public float Strength { get; set; } = 1f;

    /// <summary>
    /// How many steps a ray takes before giving up. The march is uniform in view space, so
    /// this and <see cref="MaxDistance"/> together set how finely it samples: 64 steps over 40
    /// units steps in units of 0.6, which will walk through anything thinner than that.
    /// </summary>
    public int MaxSteps { get; set; } = 64;

    /// <summary>How far a reflected ray travels, in world units, before the reflection is given up on.</summary>
    public float MaxDistance { get; set; } = 40f;

    /// <summary>
    /// How much nearer than the ray the recorded geometry may be and still count as the thing
    /// the ray hit, in world units.
    ///
    /// <para>
    /// This is the number that decides what a "hit" means, and it cannot be tight. The depth
    /// buffer is a front surface with no thickness: the ray passes behind a foreground object
    /// and the buffer reports it as much nearer, for every remaining step, which is
    /// indistinguishable from a hit if the only test is "is the scene in front of the ray".
    /// The thickness bounds it — beyond this the ray is behind something rather than touching
    /// it — and too large a value reflects things the ray passed metres behind.
    /// </para>
    /// </summary>
    public float Thickness { get; set; } = 1.5f;

    /// <summary>
    /// Roughness above which a surface takes no screen-space reflection.
    ///
    /// <para>
    /// A rough surface reflects a wide cone, and a cone resolved from one ray per pixel is
    /// noise. The prefiltered environment already answers rough reflections properly — it is
    /// convolved for exactly this — so the cut is not a gap: past this roughness the surface
    /// keeps the reflection the shader gave it, which for a rough surface is the better of the
    /// two answers anyway.
    /// </para>
    /// </summary>
    public float MaxRoughness { get; set; } = 0.6f;

    /// <summary>
    /// Widest blur applied to the reflection, in pixels, reached at <see cref="MaxRoughness"/>.
    /// A mirror gets none.
    /// </summary>
    public int BlurRadius { get; set; } = 3;

    /// <summary>
    /// Fraction of the frame over which confidence fades to nothing at the edges. A ray that
    /// leaves the frame finds no geometry, and without the fade the reflection would stop at a
    /// hard line that moves whenever the camera does.
    /// </summary>
    public float EdgeFade { get; set; } = 0.15f;

    public void Apply(PostProcessTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        // Both are needed and neither can be derived from the other: depth says where the
        // surfaces are, reflectance says which of them are mirrors.
        if (!target.HasDepth || !target.HasReflectance || target.Width < 3 || target.Height < 3 || Strength <= 0f)
        {
            return;
        }

        var count = target.Width * target.Height;

        if (_weight.Length < count)
        {
            _reflection = new float[count * 3];
            _blurredReflection = new float[count * 3];
            _weight = new float[count];
            _blurredWeight = new float[count];
            _grazing = new float[count];
        }

        March(target);
        Blur(target);
        Composite(target);
    }

    /// <summary>
    /// Traces one reflected ray per reflective pixel and records what it found, with a
    /// confidence in [0, 1] that is zero wherever it found nothing.
    /// </summary>
    private void March(PostProcessTarget target)
    {
        var width = target.Width;
        var height = target.Height;
        var depth = target.ViewDepth;
        var reflectance = target.Reflectance;
        var field = target.Field;
        var color = target.Color;

        var reflection = _reflection;
        var weight = _weight;
        var grazing = _grazing;

        var steps = System.Math.Clamp(MaxSteps, 1, 512);
        var maxDistance = MathF.Max(MaxDistance, 1e-3f);
        var stepLength = maxDistance / steps;
        var thickness = MathF.Max(Thickness, 1e-4f);
        var maxRoughness = System.Math.Clamp(MaxRoughness, 0f, 1f);

        Parallel.For(0, height, y =>
        {
            for (var x = 0; x < width; x++)
            {
                var index = x + y * width;

                weight[index] = 0f;

                var surface = SurfaceReflectance.FromPacked(reflectance[index]);

                // The overwhelmingly common case, and the reason the reflectance channel is
                // worth its four bytes: most of a frame is not a mirror, and one test drops it
                // before anything expensive happens.
                if (!surface.IsReflective || float.IsPositiveInfinity(depth[index]))
                {
                    continue;
                }

                var roughness = surface.Roughness;

                if (roughness > maxRoughness)
                {
                    continue;
                }

                var origin = field.PositionAt(x, y);
                var normal = field.NormalAt(x, y);

                if (normal == Vector3.Zero)
                {
                    continue;
                }

                // In view space the eye is the origin, so the direction the camera looks along
                // to reach this pixel is the pixel's own position.
                var incident = Vector3.Normalize(origin);
                var direction = Vector3.Reflect(incident, normal);

                // A ray reflected back toward the eye — a mirror facing the camera — can only
                // find what is between the surface and the lens, and mostly finds the near
                // plane a step later. Dropped here rather than marched and thrown away.
                if (direction.Z >= 0f)
                {
                    continue;
                }

                if (!Trace(field, depth, origin, normal, direction, steps, stepLength, thickness,
                        out var hitX, out var hitY, out var travelled))
                {
                    continue;
                }

                // Two ways a reflection loses confidence, multiplied because either being zero
                // means the sample is worthless. How much of it the surface then reflects is
                // the Fresnel term, and that is per channel, so it waits for the composite.
                var confidence = EdgeWeight(hitX, hitY, width, height)
                    * DistanceWeight(travelled, maxDistance);

                if (confidence <= 0f)
                {
                    continue;
                }

                var hit = (hitX + hitY * width) * 3;

                reflection[index * 3] = color[hit];
                reflection[index * 3 + 1] = color[hit + 1];
                reflection[index * 3 + 2] = color[hit + 2];
                weight[index] = confidence;
                grazing[index] = Grazing(incident, normal);
            }
        });
    }

    /// <summary>
    /// Walks the ray in uniform view-space steps until the depth buffer says something is in
    /// the way, then bisects the last step to find where.
    ///
    /// <para>
    /// Uniform in view space rather than in screen space, which is the other way to do this
    /// and the more accurate one — a screen-space march samples every pixel the ray crosses
    /// exactly once, where this one oversamples what is near the camera and can step over what
    /// is far. It is done this way because <see cref="Thickness"/>, the test that decides what
    /// a hit is, is a world-space distance, and against a screen-space march its meaning would
    /// change along the ray. A wrong hit is worse than a missed one here: a missed reflection
    /// leaves the surface as the shader drew it, and a wrong one draws the wrong thing.
    /// </para>
    /// </summary>
    private static bool Trace(
        in DepthField field,
        float[] depth,
        Vector3 origin,
        Vector3 normal,
        Vector3 direction,
        int steps,
        float stepLength,
        float thickness,
        out int hitX,
        out int hitY,
        out float travelled)
    {
        hitX = 0;
        hitY = 0;
        travelled = 0f;

        var width = field.Width;

        // Start one step off the surface along the normal, not along the ray: the first
        // sample of a ray leaving a surface at a grazing angle lands on the surface it left,
        // and the depth buffer answers "yes, something is here" — every glancing reflection
        // would be of the reflector itself.
        var start = origin + normal * (stepLength * 0.5f);

        var previousDistance = 0f;

        for (var step = 1; step <= steps; step++)
        {
            var distance = step * stepLength;
            var point = start + direction * distance;

            if (!field.ProjectToScreen(point, out var sx, out var sy, out var rayDepth))
            {
                // Off the frame, or behind the eye: there is nothing recorded out there to hit.
                return false;
            }

            var sceneDepth = depth[sx + sy * width];

            if (float.IsPositiveInfinity(sceneDepth))
            {
                previousDistance = distance;
                continue;
            }

            var difference = rayDepth - sceneDepth;

            if (difference <= 0f)
            {
                // The ray is still in front of the recorded surface.
                previousDistance = distance;
                continue;
            }

            if (difference > thickness)
            {
                // Past the back of whatever is recorded here: the ray went behind it rather
                // than into it. Keep marching — a wall in the foreground must not swallow a
                // ray that was passing it.
                previousDistance = distance;
                continue;
            }

            Refine(field, depth, start, direction, previousDistance, distance, thickness,
                ref sx, ref sy, ref distance);

            hitX = sx;
            hitY = sy;
            travelled = distance;

            return true;
        }

        return false;
    }

    /// <summary>
    /// Bisects the step that crossed the surface, so the hit lands where the ray met it rather
    /// than up to a whole step past it. Without this a reflection is quantized to the march's
    /// step size, which reads as the reflected image being cut into bands.
    /// </summary>
    private static void Refine(
        in DepthField field,
        float[] depth,
        Vector3 start,
        Vector3 direction,
        float near,
        float far,
        float thickness,
        ref int hitX,
        ref int hitY,
        ref float distance)
    {
        const int Bisections = 6;

        var width = field.Width;

        for (var i = 0; i < Bisections; i++)
        {
            var middle = (near + far) * 0.5f;

            if (!field.ProjectToScreen(start + direction * middle, out var sx, out var sy, out var rayDepth))
            {
                break;
            }

            var sceneDepth = depth[sx + sy * width];
            var difference = rayDepth - sceneDepth;

            if (float.IsPositiveInfinity(sceneDepth) || difference <= 0f || difference > thickness)
            {
                // Not a hit here, either because the ray has not reached the surface yet or
                // because this pixel records something else entirely. Both mean the same thing
                // for the search: narrow toward the far end, which is a hit already known to
                // be good. The interval can only ever close onto that.
                near = middle;
                continue;
            }

            far = middle;
            hitX = sx;
            hitY = sy;
            distance = middle;
        }
    }

    /// <summary>
    /// The <c>(1 - cos θ)^5</c> of Schlick's approximation — the part of the Fresnel term that
    /// depends on the angle rather than on the material. The composite finishes it as
    /// <c>F0 + (1 - F0) · this</c>, per channel, which is what makes a floor reflect a few
    /// percent underfoot and almost everything at the horizon.
    /// </summary>
    private static float Grazing(Vector3 incident, Vector3 normal)
    {
        var cosine = System.Math.Clamp(Vector3.Dot(-incident, normal), 0f, 1f);
        var f = 1f - cosine;
        var f2 = f * f;

        return f2 * f2 * f;
    }

    /// <summary>Fades a hit out as it approaches the edge of the frame it was found in.</summary>
    private float EdgeWeight(int x, int y, int width, int height)
    {
        var fade = System.Math.Clamp(EdgeFade, 0f, 0.5f);

        if (fade <= 0f)
        {
            return 1f;
        }

        var u = (x + 0.5f) / width;
        var v = (y + 0.5f) / height;

        var horizontal = System.Math.Clamp(MathF.Min(u, 1f - u) / fade, 0f, 1f);
        var vertical = System.Math.Clamp(MathF.Min(v, 1f - v) / fade, 0f, 1f);

        return horizontal * vertical;
    }

    /// <summary>
    /// Fades a reflection out over the last quarter of the ray's allowance, so a surface
    /// reflecting something at the limit of the march does not lose it the moment the camera
    /// moves a step back.
    /// </summary>
    private static float DistanceWeight(float travelled, float maxDistance)
    {
        const float FadeFrom = 0.75f;

        var t = travelled / maxDistance;

        return t <= FadeFrom ? 1f : System.Math.Clamp((1f - t) / (1f - FadeFrom), 0f, 1f);
    }

    /// <summary>
    /// Blurs the reflection by the roughness of the surface reflecting it — a variable-radius
    /// box, weighted by confidence so a pixel whose ray missed contributes nothing rather than
    /// pulling its neighbours toward black.
    ///
    /// <para>
    /// Not separable, because the radius is per pixel: two passes with different radii per
    /// pixel do not compose into one box. At a radius of three that is 49 taps, and only on
    /// the pixels that both reflect and are rough enough to need it — which is why the radius
    /// is capped rather than scaled up to <see cref="MaxRoughness"/>'s full cone.
    /// </para>
    /// </summary>
    private void Blur(PostProcessTarget target)
    {
        var maxRadius = System.Math.Clamp(BlurRadius, 0, 8);

        if (maxRadius == 0)
        {
            return;
        }

        var width = target.Width;
        var height = target.Height;
        var reflectance = target.Reflectance;
        var maxRoughness = MathF.Max(MaxRoughness, 1e-4f);

        var source = _reflection;
        var sourceWeight = _weight;
        var destination = _blurredReflection;
        var destinationWeight = _blurredWeight;

        Parallel.For(0, height, y =>
        {
            for (var x = 0; x < width; x++)
            {
                var index = x + y * width;

                destinationWeight[index] = sourceWeight[index];
                destination[index * 3] = source[index * 3];
                destination[index * 3 + 1] = source[index * 3 + 1];
                destination[index * 3 + 2] = source[index * 3 + 2];

                if (sourceWeight[index] <= 0f)
                {
                    continue;
                }

                var roughness = SurfaceReflectance.FromPacked(reflectance[index]).Roughness;
                var radius = (int)(roughness / maxRoughness * maxRadius + 0.5f);

                if (radius <= 0)
                {
                    continue;
                }

                float r = 0f, g = 0f, b = 0f, total = 0f;

                for (var dy = -radius; dy <= radius; dy++)
                {
                    var sy = y + dy;

                    if ((uint)sy >= (uint)height)
                    {
                        continue;
                    }

                    for (var dx = -radius; dx <= radius; dx++)
                    {
                        var sx = x + dx;

                        if ((uint)sx >= (uint)width)
                        {
                            continue;
                        }

                        var neighbour = sx + sy * width;
                        var w = sourceWeight[neighbour];

                        if (w <= 0f)
                        {
                            continue;
                        }

                        r += source[neighbour * 3] * w;
                        g += source[neighbour * 3 + 1] * w;
                        b += source[neighbour * 3 + 2] * w;
                        total += w;
                    }
                }

                if (total <= 0f)
                {
                    continue;
                }

                destination[index * 3] = r / total;
                destination[index * 3 + 1] = g / total;
                destination[index * 3 + 2] = b / total;
            }
        });

        (_reflection, _blurredReflection) = (_blurredReflection, _reflection);
        (_weight, _blurredWeight) = (_blurredWeight, _weight);
    }

    /// <summary>
    /// Blends each pixel toward what its ray found, by the surface's Fresnel reflectance times
    /// the march's confidence — a lerp rather than an addition, because the colour already
    /// there includes this surface's environment reflection and adding would count the same
    /// light twice.
    /// </summary>
    private void Composite(PostProcessTarget target)
    {
        var width = target.Width;
        var color = target.Color;
        var reflectance = target.Reflectance;
        var reflection = _reflection;
        var weight = _weight;
        var grazing = _grazing;
        var strength = MathF.Max(Strength, 0f);

        Parallel.For(0, target.Height, y =>
        {
            for (var x = 0; x < width; x++)
            {
                var index = x + y * width;
                var confidence = weight[index];

                if (confidence <= 0f)
                {
                    continue;
                }

                var f0 = SurfaceReflectance.FromPacked(reflectance[index]).Reflectivity;
                var g = grazing[index];
                var scale = confidence * strength;
                var i = index * 3;

                // Schlick per channel, so a coloured F0 tints what it reflects as well as
                // weighting it: this is the whole reason the reflectance channel carries three
                // bytes rather than one.
                Blend(color, i, reflection[i], Schlick(f0.R, g) * scale);
                Blend(color, i + 1, reflection[i + 1], Schlick(f0.G, g) * scale);
                Blend(color, i + 2, reflection[i + 2], Schlick(f0.B, g) * scale);
            }
        });
    }

    private static float Schlick(float f0, float grazing) => f0 + (1f - f0) * grazing;

    private static void Blend(float[] color, int i, float reflected, float amount) =>
        color[i] = float.Lerp(color[i], reflected, System.Math.Clamp(amount, 0f, 1f));
}
