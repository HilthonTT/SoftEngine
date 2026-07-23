using System.Numerics;

namespace SoftEngine.Core.Math;

/// <summary>
/// Must replace with a Quaternion
/// </summary>
/// <param name="x"></param>
/// <param name="y"></param>
/// <param name="z"></param>
public sealed class Rotation3D(float x, float y, float z)
{
    private const float PiDeg = (float)System.Math.PI * 1f / 180f;

    public float XPitch { get; set; } = x;

    public float YYaw { get; set; } = y;

    public float ZRoll { get; set; } = z;

    public Rotation3D ToRad() => this * PiDeg;

    public static bool operator ==(Rotation3D? p, Rotation3D? other) =>
        p is null
            ? other is null
            : other is not null && other.XPitch == p.XPitch && other.YYaw == p.YYaw && other.ZRoll == p.ZRoll;

    public static bool operator !=(Rotation3D? p, Rotation3D? other) => !(other == p);

    public static Rotation3D operator +(Rotation3D p, Rotation3D other) => new(other.XPitch + p.XPitch, other.YYaw + p.YYaw, other.ZRoll + p.ZRoll);

    public static Rotation3D operator *(Rotation3D p, float a) => new(p.XPitch * a, p.YYaw * a, p.ZRoll * a);

    public static bool operator ==(Rotation3D p, Vector3 other) => other.X == p.XPitch && other.Y == p.YYaw && other.Z == p.ZRoll;

    public static bool operator !=(Rotation3D p, Vector3 other) => !(p == other);

    public override string ToString()
    {
        return $"<{XPitch}\t{YYaw}\t{ZRoll}>";
    }

    public override bool Equals(object? obj)
    {
        return obj is Rotation3D d &&
               XPitch == d.XPitch &&
               YYaw == d.YYaw &&
               ZRoll == d.ZRoll;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(XPitch, YYaw, ZRoll);
    }
}
