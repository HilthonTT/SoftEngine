using SoftEngine.Core.Buffers;
using SoftEngine.Core.Pipeline.Culling;
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
    public bool Render(
        FrameBuffer surface,
        IProjection? projection,
        ShadowMap? shadowMap,
        DebugView view,
        OcclusionBuffer? occlusion = null,
        VelocityBuffer? velocity = null)
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
            DebugView.OcclusionBuffer => RenderOcclusion(surface, occlusion),
            DebugView.Velocity => RenderVelocity(surface, velocity),
            DebugView.MipLevel => RenderMipLevel(surface),
            _ => false,
        };
    }

    /// <summary>
    /// The mip level each pixel's texture was sampled at: level 0 red, then orange, yellow,
    /// green, blue and violet as the chain descends, wrapping after six.
    ///
    /// <para>
    /// A ramp rather than a heat map, and the distinction matters. Overdraw is a magnitude and
    /// is read as one — more is worse. A mip level is a <em>category</em>: what you look for
    /// here is where one band ends and the next begins, so the colours are chosen to be told
    /// apart from their neighbours rather than to be ordered by eye.
    /// </para>
    ///
    /// <para>
    /// Untextured geometry is dark grey, not black, and the background is black. A painter that
    /// samples no map made no mip decision to show, and colouring it as though it had sampled
    /// level 0 would fill most scenes in this engine with a confident red.
    /// </para>
    /// </summary>
    private static bool RenderMipLevel(FrameBuffer surface)
    {
        var levels = surface.MipLevels;

        if (levels.IsEmpty)
        {
            return false;
        }

        var screen = surface.Screen;
        var width = surface.Width;

        // Walked in order rather than in parallel, as the overdraw view is and for the same
        // reason: a ref struct cannot be closed over by the loop body.
        for (var y = 0; y < surface.Height; y++)
        {
            var i = y * width;

            for (var x = 0; x < width; x++, i++)
            {
                var level = levels[i];

                if (level < 0)
                {
                    // Two different "no level here": nothing drawn at all, and something drawn
                    // that sampled no texture.
                    screen[i] = surface.IsBackground(x, y) ? Pack(0, 0, 0) : Pack(48, 48, 52);
                    continue;
                }

                var (r, g, b) = MipTint(level);

                screen[i] = Pack(Byte(r), Byte(g), Byte(b));
            }
        }

        return true;
    }

    /// <summary>
    /// One colour per mip level, wrapping after six. Six is more levels than any frame shows
    /// at once in practice — a 1024-texel map has eleven, and a single view of it spans three
    /// or four — so the wrap is cheaper than a gradient nobody could read the steps of.
    /// </summary>
    private static (float R, float G, float B) MipTint(int level) => (level % 6) switch
    {
        0 => (0.90f, 0.20f, 0.20f),
        1 => (0.95f, 0.55f, 0.15f),
        2 => (0.90f, 0.85f, 0.20f),
        3 => (0.30f, 0.80f, 0.35f),
        4 => (0.25f, 0.55f, 0.95f),
        _ => (0.65f, 0.35f, 0.90f),
    };

    /// <summary>
    /// Per-pixel motion: the direction as a hue around the colour wheel, the speed as how far from
    /// grey it is. A still frame is flat grey, which is the reading that matters — anything that is
    /// not grey while nothing is moving is a velocity that should not be there.
    ///
    /// Speed is scaled against the frame's own fastest pixel rather than a fixed ceiling, because a
    /// velocity in pixels means something different at every resolution and frame rate, and the
    /// question being asked of this view is almost always "which way" rather than "how much".
    /// </summary>
    private static bool RenderVelocity(FrameBuffer surface, VelocityBuffer? velocity)
    {
        if (velocity is null ||
            !velocity.IsFilled ||
            velocity.Width != surface.Width ||
            velocity.Height != surface.Height)
        {
            return false;
        }

        var fastest = velocity.MaxSpeed();
        var scale = fastest > 1e-4f ? 1f / fastest : 0f;

        var screen = surface.Screen;
        var width = surface.Width;

        Parallel.For(0, surface.Height, y =>
        {
            for (var x = 0; x < width; x++)
            {
                var i = x + y * width;

                if (!velocity.IsCovered(x, y))
                {
                    screen[i] = Pack(0, 0, 0);
                    continue;
                }

                var motion = velocity.At(x, y);
                var speed = motion.Length() * scale;

                if (speed < 1e-4f)
                {
                    screen[i] = Pack(128, 128, 128);
                    continue;
                }

                // Angle to hue, with grey at the centre so a slow pixel is a pale version of the
                // direction it is going rather than a saturated version of a random one.
                var angle = MathF.Atan2(motion.Y, motion.X) / MathF.Tau + 0.5f;

                var (r, g, b) = Hue(angle);
                var t = System.Math.Clamp(speed, 0f, 1f);

                screen[i] = Pack(
                    Byte(0.5f + (r - 0.5f) * t),
                    Byte(0.5f + (g - 0.5f) * t),
                    Byte(0.5f + (b - 0.5f) * t));
            }
        });

        return true;
    }

    /// <summary>A fully saturated colour at a position around the wheel, in [0, 1).</summary>
    private static (float R, float G, float B) Hue(float position)
    {
        var h = (position - MathF.Floor(position)) * 6f;

        var x = 1f - MathF.Abs(h % 2f - 1f);

        return (int)h switch
        {
            0 => (1f, x, 0f),
            1 => (x, 1f, 0f),
            2 => (0f, 1f, x),
            3 => (0f, x, 1f),
            4 => (x, 0f, 1f),
            _ => (1f, 0f, x),
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
    /// was drawn into are black. Fitted into the viewport with its aspect preserved, because a
    /// square map stretched across a wide frame misrepresents where its resolution is going.
    ///
    /// Every cascade is shown, side by side and left to right from the nearest, each tinted so
    /// they can be told apart at a glance. Showing only the first would hide the thing the
    /// view is for: whether each cascade is covering the range it should, and how much finer
    /// the near one is than the far one.
    /// </summary>
    private static bool RenderShadowMap(FrameBuffer surface, ShadowMap? shadowMap)
    {
        if (shadowMap is null)
        {
            return false;
        }

        var resolution = shadowMap.Resolution;
        var cascades = shadowMap.CascadeCount;
        var texels = shadowMap.Depth;

        var width = surface.Width;
        var height = surface.Height;
        var screen = surface.Screen;

        // The largest row of equal squares that fits, centred. One cascade reduces to the
        // single centred square this view has always drawn.
        var side = System.Math.Min(width / cascades, height);
        var originX = (width - side * cascades) / 2;
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
                var column = x - originX;

                // Outside the maps: a dark surround rather than black, so their own empty
                // texels stay distinguishable from the letterboxing around them.
                if (!insideRow || column < 0 || column >= side * cascades)
                {
                    screen[i] = Pack(24, 24, 28);
                    continue;
                }

                var cascade = System.Math.Min(column / side, cascades - 1);

                // Both are inside a square, so both land in a map — but the rounding is still
                // worth clamping, since the last pixel of a square maps to exactly the
                // resolution when the two are equal.
                var mapX = System.Math.Min((int)((column - cascade * side) * toTexel), resolution - 1);
                var mapY = System.Math.Min((int)((y - originY) * toTexel), resolution - 1);

                var stored = texels[shadowMap.OffsetOf(cascade) + mapX + mapY * resolution];

                if (stored >= ShadowMap.Empty)
                {
                    screen[i] = Pack(0, 0, 0);
                    continue;
                }

                var level = 1f - stored;
                var tint = CascadeTint(cascade);

                screen[i] = Pack(Byte(level * tint.R), Byte(level * tint.G), Byte(level * tint.B));
            }
        });

        return true;
    }

    /// <summary>
    /// The occlusion pyramid, stretched over the frame it was rasterized from.
    ///
    /// <para>
    /// <b>The level shown is the finest one a query is allowed to read</b>
    /// (<see cref="OcclusionBuffer.MinimumQueryLevel"/>), not the level that was rasterized. That
    /// is deliberate, and it is the whole reason the view is worth having. Level 0 is
    /// centre-sampled, so a texel there is written wherever a triangle reached its middle —
    /// which is not the same as covering it. Coverage only appears one level up, where a texel
    /// carries a real depth exactly where all four of its children were sampled inside geometry.
    /// Showing level 0 would therefore paint a confident picture of occlusion the culler cannot
    /// actually use, and the gap between the two is precisely what you are looking at this view
    /// to find.
    /// </para>
    ///
    /// <para>
    /// Filled texels ramp bright-to-dark with distance, auto-ranged over the depths actually in
    /// the buffer — the same treatment, and for the same reason, as the depth view. A perspective
    /// depth buffer spends nearly all of its range in the first few percent of the scene, so
    /// everything an occluder pass ever rasterizes sits within a hair of 1. Presented literally
    /// that is a black rectangle; ranged over what is there, it is the near wall against the far
    /// one, which is the comparison you came for.
    /// </para>
    ///
    /// <para>
    /// Texels nothing covered are drawn in a cold blue-grey rather than in black: "nothing here"
    /// and "something at the far plane" are different answers, and a greyscale ramp gives them
    /// the same colour — which would make an empty buffer look like a fully occluding one.
    /// </para>
    /// </summary>
    private static bool RenderOcclusion(FrameBuffer surface, OcclusionBuffer? occlusion)
    {
        // Nothing to show is the honest answer in three cases the caller cannot tell apart:
        // the pass is switched off, the frame was probed (which switches it off), or it declined
        // this world for having too little to occlude with.
        if (occlusion is null || !occlusion.HasOccluders)
        {
            return false;
        }

        var level = OcclusionBuffer.MinimumQueryLevel;

        if (occlusion.LevelCount <= level)
        {
            return false;
        }

        var (levelWidth, levelHeight) = occlusion.SizeOf(level);

        if (levelWidth <= 0 || levelHeight <= 0)
        {
            return false;
        }

        // The range the filled texels actually span. The cleared value is the far plane, where a
        // texel can hide nothing, so anything at or past it was never covered and takes no part
        // in the ramp.
        var near = float.PositiveInfinity;
        var far = float.NegativeInfinity;

        for (var y = 0; y < levelHeight; y++)
        {
            for (var x = 0; x < levelWidth; x++)
            {
                var depth = occlusion.DepthAt(level, x, y);

                if (depth >= 1f || !float.IsFinite(depth))
                {
                    continue;
                }

                near = MathF.Min(near, depth);
                far = MathF.Max(far, depth);
            }
        }

        if (!float.IsFinite(near))
        {
            // Coverage was claimed but no texel carries a real depth — nothing to draw a ramp
            // over, and a blank frame would be a worse answer than leaving the image alone.
            return false;
        }

        var span = far - near;
        var scale = span > 1e-7f ? 1f / span : 0f;

        var width = surface.Width;
        var height = surface.Height;
        var screen = surface.Screen;

        // The buffer covers the same view as the frame, so it is stretched across it rather than
        // letterboxed the way the shadow map is: the point of this view is which part of the
        // *frame* is covered, and a texel that does not line up with the pixels it is a claim
        // about answers a different question.
        var toTexelX = levelWidth / (float)width;
        var toTexelY = levelHeight / (float)height;

        Parallel.For(0, height, y =>
        {
            var texelY = System.Math.Min((int)(y * toTexelY), levelHeight - 1);
            var i = y * width;

            for (var x = 0; x < width; x++, i++)
            {
                var texelX = System.Math.Min((int)(x * toTexelX), levelWidth - 1);
                var depth = occlusion.DepthAt(level, texelX, texelY);

                if (depth >= 1f || !float.IsFinite(depth))
                {
                    screen[i] = Pack(28, 34, 46);
                    continue;
                }

                // Nearest is brightest. The floor keeps the farthest occluder well clear of the
                // surround's own darkness, so "covered, but a long way off" never reads as
                // "not covered".
                var shade = Byte(1f - (depth - near) * scale * 0.75f);

                screen[i] = Pack(shade, shade, (byte)System.Math.Min(shade + 18, 255));
            }
        });

        return true;
    }

    /// <summary>
    /// A colour per cascade, so a row of grey squares reads as a sequence rather than as one
    /// map repeated. The nearest is left white — it is the one whose detail is being judged,
    /// and tinting it would trade that away for nothing.
    /// </summary>
    private static (float R, float G, float B) CascadeTint(int cascade) => cascade switch
    {
        0 => (1f, 1f, 1f),
        1 => (0.70f, 0.90f, 1f),
        2 => (0.75f, 1f, 0.75f),
        _ => (1f, 0.85f, 0.65f),
    };

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
