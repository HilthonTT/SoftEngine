using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Math;
using SoftEngine.Core.Scenes.Graph;
using SoftEngine.Core.Textures;
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

    bool Visible => true;

    float Opacity => 1f;

    Vector2[]? TexCoords => null;

    Texture? Texture => null;

    Material? Material => null;

    Vector4[]? Tangents => null;

    void EnsureTangents()
    {
    }

    float BoundingRadius => float.PositiveInfinity;

    SceneNode? Parent => null;

    public Matrix4x4 LocalMatrix =>
          Matrix4x4.CreateScale(Scale) *
          Matrix4x4.CreateFromYawPitchRoll(Rotation.YYaw, Rotation.XPitch, Rotation.ZRoll) *
          Matrix4x4.CreateTranslation(Position);

    public Matrix4x4 WorldMatrix =>
        Parent is { } parent ? LocalMatrix * parent.WorldMatrix : LocalMatrix;
}
