using System.Numerics;

namespace SoftEngine.Core.Pipeline.Clipping;

public sealed class LiangBarskyClippingHomogeneous : IClippingHomogeneous
{
    public bool Clip(ref Vector4 p0, ref Vector4 p1)
    {
        if (p0.W < 0 && p1.W < 0)
        {
            return false;
        }

        float t0 = 0f;
        float t1 = 1f;

        var delta = p1 - p0;

        if (!Clip(p0.W - p0.X, -delta.W + delta.X, ref t0, ref t1))
        {
            return false;
        }
        if (!Clip(p0.W + p0.X, -delta.W - delta.X, ref t0, ref t1))
        {
            return false;
        }

        if (!Clip(p0.W - p0.Y, -delta.W + delta.Y, ref t0, ref t1))
        {
            return false;
        }

        if (!Clip(p0.W + p0.Y, -delta.W - delta.Y, ref t0, ref t1))
        {
            return false;
        }

        if (!Clip(p0.W - p0.Z, -delta.W + delta.Z, ref t0, ref t1))
        {
            return false;
        }

        if (!Clip(p0.W + p0.Z, -delta.W - delta.Z, ref t0, ref t1))
        {
            return false;
        }

        if (t1 < 1)
        {
            p1 = p0 + t1 * delta;
        }

        if (t0 > 0)
        {
            p0 += t0 * delta;
        }

        return true;
    }

    private static bool Clip(float q, float p, ref float t0, ref float t1)
    {
        // Parallel to this plane: the line never crosses it, so it lies wholly on one side
        // and q alone says which. It has to return here rather than fall through, and not
        // only to avoid dividing by zero — the divisor arrives as negative zero whenever the
        // edge is exactly axis-aligned and its endpoints share a w, which `p < 0` reads as
        // positive while `q / p` comes out at negative infinity. The two disagree, the
        // infinity lands in the branch that treats it as an entry point past the end of the
        // segment, and a line that was fully inside the frustum is discarded.
        //
        // Every edge of a surface square-on to the camera is exactly that line, which is why
        // this showed up as a wireframe with its front face missing rather than as anything
        // resembling a clipping fault.
        if (System.Math.Abs(p) < float.Epsilon)
        {
            return q >= 0;
        }

        float r = q / p;

        if (p < 0)
        {
            if (r > t1)
            {
                return false;
            }

            if (r > t0)
            {
                t0 = r;
            }
        }
        else
        {
            if (r < t0)
            {
                return false;
            }

            if (r < t1)
            {
                t1 = r;
            }
        }

        return true;
    }
}
