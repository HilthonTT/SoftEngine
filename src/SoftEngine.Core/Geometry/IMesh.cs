using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Math;
using System.Numerics;

namespace SoftEngine.Core.Geometry;

public interface IMesh
{
    Rotation3D Rotation { get; set; }

    Vector3 Position { get; set; }

    Vector3 Scale { get; set; }

    ColorRGB[] TriangleColors { get; }

    Triangle[] Triangles { get; }

    Vector3[] Vertices { get; }

    Vector3[] NormVertices { get; }

    public Matrix4x4 WorldMatrix =>
          Matrix4x4.CreateFromYawPitchRoll(Rotation.YYaw, Rotation.XPitch, Rotation.ZRoll) *
          Matrix4x4.CreateTranslation(Position) *
          Matrix4x4.CreateScale(Scale);
}
