using System.Numerics;

namespace SoftEngine.Core.Buffers;

/// <summary>
/// Where each pixel's surface was in the previous frame, as an offset in pixels — the one thing a
/// still image cannot tell you about itself, and the input every temporal technique is built on.
///
/// <para>
/// The colour buffer says what is at a pixel. The depth buffer says how far away it is. Neither says
/// whether it is the <em>same thing</em> that was there last frame, which is exactly what has to be
/// known before last frame's answer can be reused. Reprojecting by the camera's own motion gets a
/// static scene right and everything that moved inside it wrong; a per-pixel velocity gets both,
/// because it is measured from where each surface actually was.
/// </para>
///
/// <para>
/// Velocities point <em>backwards</em>: subtracting one from a pixel's position gives the pixel to
/// read the history from. That is the direction temporal reprojection needs, and — negated — the
/// direction a motion blur smears along.
/// </para>
/// </summary>
public sealed class VelocityBuffer
{
    /// <summary>Two floats per pixel: how far the surface at it moved across the screen since the last frame.</summary>
    private float[] _velocity = [];

    /// <summary>
    /// The pass's own normalized depth, so the nearest surface wins a pixel. Separate from the
    /// frame's depth buffer because this pass runs before shading, on its own geometry, and must
    /// not disturb the buffer the shading pass is about to fill.
    /// </summary>
    private float[] _depth = [];

    public int Width { get; private set; }

    public int Height { get; private set; }

    /// <summary>
    /// Whether the pass ran and had a previous frame to compare against. False on the first frame
    /// after a reset, when every velocity is zero because nothing is known rather than because
    /// nothing moved — a distinction every consumer has to make.
    /// </summary>
    public bool IsFilled { get; internal set; }

    /// <summary>The raw velocities, two floats per pixel, for a caller that wants the bulk.</summary>
    public float[] Velocities => _velocity;

    /// <summary>Sized to the render target, keeping the arrays when they are already big enough.</summary>
    public void Resize(int width, int height)
    {
        Width = System.Math.Max(0, width);
        Height = System.Math.Max(0, height);

        var pixels = Width * Height;

        if (_depth.Length >= pixels && _velocity.Length >= pixels * 2)
        {
            return;
        }

        _velocity = new float[pixels * 2];
        _depth = new float[pixels];
    }

    /// <summary>
    /// Resets every pixel to "nothing moved here, and nothing covered it". Depth is cleared to 1,
    /// which is past the far plane, so the first triangle to reach a pixel always wins it.
    /// </summary>
    public void Clear()
    {
        var pixels = Width * Height;

        _velocity.AsSpan(0, pixels * 2).Clear();
        _depth.AsSpan(0, pixels).Fill(1f);

        IsFilled = false;
    }

    /// <summary>How far the surface at a pixel moved since the previous frame, in pixels.</summary>
    public Vector2 At(int x, int y)
    {
        var i = (x + y * Width) * 2;
        return new Vector2(_velocity[i], _velocity[i + 1]);
    }

    /// <summary>Whether any geometry covered a pixel. Where nothing did, its velocity is meaningless rather than zero.</summary>
    public bool IsCovered(int x, int y) => _depth[x + y * Width] < 1f;

    /// <summary>The largest motion in the frame, in pixels — what a blur has to size its kernel to.</summary>
    public float MaxSpeed()
    {
        var pixels = Width * Height;
        var max = 0f;

        for (var i = 0; i < pixels; i++)
        {
            var speed = MathF.Abs(_velocity[i * 2]) + MathF.Abs(_velocity[i * 2 + 1]);

            if (speed > max)
            {
                max = speed;
            }
        }

        return max;
    }

    /// <summary>
    /// Writes a pixel if it is nearer than what is already there. Called from the fill, which
    /// partitions the frame into bands of rows — so two workers never contend for a pixel and this
    /// needs no interlock.
    /// </summary>
    internal void Write(int x, int y, float depth, float dx, float dy)
    {
        var pixel = x + y * Width;

        if (depth >= _depth[pixel])
        {
            return;
        }

        _depth[pixel] = depth;

        _velocity[pixel * 2] = dx;
        _velocity[pixel * 2 + 1] = dy;
    }
}
