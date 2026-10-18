using SoftEngine.Core.Buffers;
using SoftEngine.Core.Pipeline.Culling;
using SoftEngine.Core.Scenes.Projections;
using SoftEngine.Core.Shading;
using System.Numerics;

namespace SoftEngine.Core.Pipeline.Debugging;

public sealed class BufferVisualizer
{
    private float[] _depth = [];

    public float OverdrawCeiling { get; set; } = 8f;

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

    private static bool RenderMipLevel(FrameBuffer surface)
    {
        var levels = surface.MipLevels;

        if (levels.IsEmpty)
        {
            return false;
        }

        var screen = surface.Screen;
        var width = surface.Width;

        for (var y = 0; y < surface.Height; y++)
        {
            var i = y * width;

            for (var x = 0; x < width; x++, i++)
            {
                var level = levels[i];

                if (level < 0)
                {
                    screen[i] = surface.IsBackground(x, y) ? Pack(0, 0, 0) : Pack(48, 48, 52);
                    continue;
                }

                var (r, g, b) = MipTint(level);

                screen[i] = Pack(Byte(r), Byte(g), Byte(b));
            }
        }

        return true;
    }

    private static (float R, float G, float B) MipTint(int level) => (level % 6) switch
    {
        0 => (0.90f, 0.20f, 0.20f),
        1 => (0.95f, 0.55f, 0.15f),
        2 => (0.90f, 0.85f, 0.20f),
        3 => (0.30f, 0.80f, 0.35f),
        4 => (0.25f, 0.55f, 0.95f),
        _ => (0.65f, 0.35f, 0.90f),
    };

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

        var side = System.Math.Min(width / cascades, height);
        var originX = (width - side * cascades) / 2;
        var originY = (height - side) / 2;

        var toTexel = resolution / (float)side;

        Parallel.For(0, height, y =>
        {
            var i = y * width;

            var insideRow = (uint)(y - originY) < (uint)side;

            for (var x = 0; x < width; x++, i++)
            {
                var column = x - originX;

                if (!insideRow || column < 0 || column >= side * cascades)
                {
                    screen[i] = Pack(24, 24, 28);
                    continue;
                }

                var cascade = System.Math.Min(column / side, cascades - 1);

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

    private static bool RenderOcclusion(FrameBuffer surface, OcclusionBuffer? occlusion)
    {
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
            return false;
        }

        var span = far - near;
        var scale = span > 1e-7f ? 1f / span : 0f;

        var width = surface.Width;
        var height = surface.Height;
        var screen = surface.Screen;

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

                var shade = Byte(1f - (depth - near) * scale * 0.75f);

                screen[i] = Pack(shade, shade, (byte)System.Math.Min(shade + 18, 255));
            }
        });

        return true;
    }

    private static (float R, float G, float B) CascadeTint(int cascade) => cascade switch
    {
        0 => (1f, 1f, 1f),
        1 => (0.70f, 0.90f, 1f),
        2 => (0.75f, 1f, 0.75f),
        _ => (1f, 0.85f, 0.65f),
    };

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

    private static int Heat(float t)
    {
        t = System.Math.Clamp(t, 0f, 1f) * 4f;

        var stop = System.Math.Min((int)t, 3);
        var blend = t - stop;

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

    private static int Pack(byte r, byte g, byte b) =>
        unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
}
