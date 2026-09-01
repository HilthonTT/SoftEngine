using System.Numerics;

namespace SoftEngine.Core.Pipeline.Temporal;

public static class TemporalJitter
{
    public const int Phases = 8;

    public static Vector2 Offset(long frame)
    {
        var index = (int)(frame % Phases);

        return new Vector2(
            Halton(index + 1, 2) - 0.5f,
            Halton(index + 1, 3) - 0.5f);
    }

    public static Matrix4x4 Apply(Matrix4x4 projection, Vector2 offset, int width, int height)
    {
        if (width <= 1 || height <= 1 || offset == Vector2.Zero)
        {
            return projection;
        }

        var ndcX = 2f * offset.X / (width - 1);
        var ndcY = 2f * offset.Y / (height - 1);

        if (MathF.Abs(projection.M34) < 1e-6f)
        {
            projection.M41 += ndcX;
            projection.M42 += ndcY;

            return projection;
        }

        projection.M31 -= ndcX;
        projection.M32 -= ndcY;

        return projection;
    }

    public static float Halton(int index, int radix)
    {
        var result = 0f;
        var fraction = 1f / radix;

        while (index > 0)
        {
            result += fraction * (index % radix);

            index /= radix;
            fraction /= radix;
        }

        return result;
    }
}
