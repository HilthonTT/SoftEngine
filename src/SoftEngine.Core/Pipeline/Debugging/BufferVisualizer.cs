using SoftEngine.Core.Buffers;
using SoftEngine.Core.Scenes.Projections;
using SoftEngine.Core.Shading;
using System.Numerics;

namespace SoftEngine.Core.Pipeline.Debugging;

/// <summary>
/// Presents one of the frame's intermediate buffers in place of the shaded image.
///
/// Everything here already exists by the time a frame ends — the depth buffer the test ran
/// against, the per-pixel write counts, the shadow map the light was rendered from. Drawing
/// them is a matter of choosing a mapping to colour that a person can read, which is most of
/// what makes a buffer view useful rather than merely available: a raw perspective depth
/// buffer presented literally is a white screen, because almost all of its range is spent in
/// the first few percent of the scene.
///
/// The pass runs after the post-process stack and overwrites the presented image, so nothing
/// upstream has to know it exists.
/// </summary>
public sealed class BufferVisualizer
{
    // View-space distance per pixel, reused across frames.
    private float[] _depth = [];

    /// <summary>
    /// The write count that reaches the top of the overdraw ramp. Fixed rather than
    /// auto-scaled: a legend that means something different every frame cannot be compared
    /// between two of them, which is the only reason to look at overdraw at all.
    /// </summary>
    public float OverdrawCeiling { get; set; } = 8f;

    /// <summary>
    /// Draws <paramref name="view"/> over <paramref name="surface"/>'s presented pixels.
    /// Returns false when the frame carries nothing to show — normals under a parallel
    /// projection, the shadow map of a scene that casts none — in which case the shaded
    /// image is left exactly as it was.
    /// </summary>
    public bool Render(FrameBuffer surface, IProjection? projection, ShadowMap? shadowMap, DebugView view)
    {
        ArgumentNullException.ThrowIfNull(surface, nameof(surface));

        if (view == DebugView.Off || surface.Width <= 0 || surface.Height <= 0)
        {
            return false;
        }

        return view switch
        {
            DebugView.Depth => RenderDepth(surface),
            DebugView.Normals => RenderNormals(surface, projection),
            DebugView.Overdraw => RenderOverdraw(surface),
            DebugView.ShadowMap => RenderShadowMap(surface, shadowMap),
            _ => false,
        };
    }

    /// <summary>
    /// Distance from the eye, auto-ranged over the geometry actually on screen: the nearest
    /// surface is white and the farthest is nearly black, whatever the projection's clip
    /// planes were set to. Fitting the ramp to the frame rather than to the frustum is what
    /// makes the same view legible on a 2-unit skull and a 1500-unit elephant.
    /// </summary>
    private bool RenderDepth(FrameBuffer surface)
    {
        var count = ReadDepth(surface);
        var depth = _depth;

        var near = float.PositiveInfinity;
        var far = 0f;

        for (var i = 0; i < count; i++)
        {
            var d = depth[i];
            if (!float.IsFinite(d))
            {
                continue;
            }

            near = MathF.Min(near, d);
            far = MathF.Max(far, d);
        }

        // Nothing was drawn: there is no range to fit, and a black frame says so.
        var span = far - near;
        var scale = float.IsFinite(near) && span > 1e-6f ? 1f / span : 0f;

        var screen = surface.Screen;
        var width = surface.Width;

        Parallel.For(0, surface.Height, y =>
        {
            var i = y * width;

            for (var x = 0; x < width; x++, i++)
            {
                var d = depth[i];

                if (!float.IsFinite(d))
                {
                    screen[i] = Pack(0, 0, 0);
                    continue;
                }

                var level = Byte(1f - (d - near) * scale);
                screen[i] = Pack(level, level, level);
            }
        });

        return true;
    }

    /// <summary>
    /// Surface orientation in view space, encoded the way a normal map encodes it — the
    /// vector remapped from [-1, 1] to [0, 1] per channel, so a surface facing the camera is
    /// pale blue and one turning away shifts red or green.
    ///
    /// The normals are reconstructed by differencing the depth buffer rather than recorded
    /// during the fill: a forward renderer has no normal buffer to show, and the differences
    /// carry exactly the information the shading did — including, usefully, the fact that a
    /// flat-shaded facet really is flat.
    /// </summary>
    private bool RenderNormals(FrameBuffer surface, IProjection? projection)
    {
        if (projection is null || !surface.HasRecoverableDepth)
        {
            return false;
        }

        var matrix = projection.ProjectionMatrix(surface.Width, surface.Height);
        if (matrix.M11 == 0f || matrix.M22 == 0f)
        {
            return false;
        }

        ReadDepth(surface);

        var width = surface.Width;
        var height = surface.Height;
        var depth = _depth;
        var screen = surface.Screen;

        var field = new DepthField(depth, width, height, matrix.M11, matrix.M22);

        Parallel.For(0, height, y =>
        {
            var i = y * width;

            for (var x = 0; x < width; x++, i++)
            {
                if (!float.IsFinite(depth[i]))
                {
                    screen[i] = Pack(0, 0, 0);
                    continue;
                }

                var normal = field.NormalAt(x, y);

                if (normal == Vector3.Zero)
                {
                    screen[i] = Pack(0, 0, 0);
                    continue;
                }

                screen[i] = Pack(
                    Byte(normal.X * 0.5f + 0.5f),
                    Byte(normal.Y * 0.5f + 0.5f),
                    Byte(normal.Z * 0.5f + 0.5f));
            }
        });

        return true;
    }

    /// <summary>
    /// How many times each pixel was written, as a heat map: black where nothing was drawn,
    /// then blue, green, yellow and red as the count climbs to <see cref="OverdrawCeiling"/>.
    ///
    /// Red is the frame paying for the same pixel over and over — geometry drawn in the wrong
    /// order, a transparent surface stacked on itself, a tile the depth bound could not
    /// reject. See <see cref="FrameBuffer.SetOverdrawCounting"/> for what does and does not
    /// get counted.
    /// </summary>
    private bool RenderOverdraw(FrameBuffer surface)
    {
        var counts = surface.Overdraw;

        if (counts.IsEmpty)
        {
            return false;
        }

        var screen = surface.Screen;
        var width = surface.Width;
        var ceiling = MathF.Max(1f, OverdrawCeiling);

        // The span is captured by index rather than by reference: a ref struct cannot be
        // closed over, so the rows are walked in order here rather than in parallel. It is
        // one pass over the frame with no work per pixel beyond a table lookup.
        for (var y = 0; y < surface.Height; y++)
        {
            var i = y * width;

            for (var x = 0; x < width; x++, i++)
            {
                var count = counts[i];

                if (count <= 0)
                {
                    screen[i] = Pack(0, 0, 0);
                    continue;
                }

                screen[i] = Heat((count - 1) / MathF.Max(ceiling - 1f, 1f));
            }
        }

        return true;
    }

    /// <summary>
    /// The shadow map as the light sees it: near to the light is bright, and texels nothing
    /// was drawn into are black. Fitted into the viewport with its aspect preserved, because
    /// a square map stretched across a wide frame misrepresents where its resolution is going.
    /// </summary>
    private static bool RenderShadowMap(FrameBuffer surface, ShadowMap? shadowMap)
    {
        if (shadowMap is null)
        {
            return false;
        }

        var resolution = shadowMap.Resolution;
        var texels = shadowMap.Depth;

        var width = surface.Width;
        var height = surface.Height;
        var screen = surface.Screen;

        // The largest square that fits, centred.
        var side = System.Math.Min(width, height);
        var originX = (width - side) / 2;
        var originY = (height - side) / 2;

        var toTexel = resolution / (float)side;

        Parallel.For(0, height, y =>
        {
            var i = y * width;

            // Whether the map covers this row at all, decided in pixels rather than in
            // texels. Truncation toward zero maps the pixel just outside the square onto
            // texel 0 whenever the map is coarser than the square it is drawn into — a
            // fractional step is a step of nothing — which drew a stripe of the map's own
            // first row and column into the letterboxing beside it.
            var insideRow = (uint)(y - originY) < (uint)side;

            for (var x = 0; x < width; x++, i++)
            {
                // Outside the map: a dark surround rather than black, so the map's own empty
                // texels stay distinguishable from the letterboxing around it.
                if (!insideRow || (uint)(x - originX) >= (uint)side)
                {
                    screen[i] = Pack(24, 24, 28);
                    continue;
                }

                // Both are inside the square, so both land in the map — but the rounding is
                // still worth clamping, since the last pixel of the square maps to exactly
                // the resolution when the two are equal.
                var mapX = System.Math.Min((int)((x - originX) * toTexel), resolution - 1);
                var mapY = System.Math.Min((int)((y - originY) * toTexel), resolution - 1);

                var stored = texels[mapX + mapY * resolution];

                if (stored >= ShadowMap.Empty)
                {
                    screen[i] = Pack(0, 0, 0);
                    continue;
                }

                var level = Byte(1f - stored);
                screen[i] = Pack(level, level, level);
            }
        });

        return true;
    }

    /// <summary>
    /// Fills <see cref="_depth"/> with a distance per pixel and returns how many are valid.
    /// Under a perspective projection that is the view-space distance the depth buffer can be
    /// inverted back into; under a parallel one, where there is no w to recover, it is the
    /// stored device depth, which is already linear in distance.
    /// </summary>
    private int ReadDepth(FrameBuffer surface)
    {
        var count = surface.Width * surface.Height;

        if (_depth.Length < count)
        {
            _depth = new float[count];
        }

        if (surface.HasRecoverableDepth)
        {
            surface.ReadViewDepth(_depth);
            return count;
        }

        var depth = _depth;
        var width = surface.Width;

        Parallel.For(0, surface.Height, y =>
        {
            for (var x = 0; x < width; x++)
            {
                var stored = surface.GetDepth(x, y);

                depth[x + y * width] = stored >= FrameBuffer.DepthResolution
                    ? float.PositiveInfinity
                    : stored / (float)FrameBuffer.DepthResolution;
            }
        });

        return count;
    }

    /// <summary>Blue → cyan → green → yellow → red, for a value in [0, 1].</summary>
    private static int Heat(float t)
    {
        t = System.Math.Clamp(t, 0f, 1f) * 4f;

        var stop = System.Math.Min((int)t, 3);
        var blend = t - stop;

        // Stops as (r, g, b) triples: the ramp climbs in luminance as well as in hue, so it
        // still reads as an ordering in a greyscale screenshot.
        (float R, float G, float B) from = stop switch
        {
            0 => (0.10f, 0.15f, 0.55f),
            1 => (0.05f, 0.55f, 0.75f),
            2 => (0.25f, 0.75f, 0.20f),
            _ => (0.95f, 0.85f, 0.15f),
        };

        (float R, float G, float B) to = stop switch
        {
            0 => (0.05f, 0.55f, 0.75f),
            1 => (0.25f, 0.75f, 0.20f),
            2 => (0.95f, 0.85f, 0.15f),
            _ => (0.95f, 0.15f, 0.10f),
        };

        return Pack(
            Byte(from.R + (to.R - from.R) * blend),
            Byte(from.G + (to.G - from.G) * blend),
            Byte(from.B + (to.B - from.B) * blend));
    }

    private static byte Byte(float unit) => (byte)(System.Math.Clamp(unit, 0f, 1f) * 255f + 0.5f);

    /// <summary>
    /// Packed ARGB, opaque. These are not colours in the scene's sense — they are numbers
    /// drawn as light — so they are written straight to the presented buffer without an sRGB
    /// encode: a depth of half the range should read as half of the ramp, not as the light
    /// that would encode to it.
    /// </summary>
    private static int Pack(byte r, byte g, byte b) =>
        unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
}
