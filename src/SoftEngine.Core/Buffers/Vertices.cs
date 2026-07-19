using System.Numerics;

namespace SoftEngine.Core.Buffers;

public readonly struct Vertices
{
    public readonly Vector3 View { get; init; }

    public Vertices SetView(Vector3 value) => new() { Norm = Norm, Proj = Proj, World = World, View = value };

    public readonly Vector3 World { get; init; }

    public Vertices SetWorld(Vector3 value) => new() { Norm = Norm, Proj = Proj, World = value, View = View };

    public readonly Vector3 Norm { get; init; }

    public Vertices SetNorm(Vector3 value) => new() { Norm = value, Proj = Proj, World = World, View = View };

    public readonly Vector4 Proj { get; init; }

    public Vertices SetProj(Vector4 value) => new() { Norm = Norm, Proj = value, World = World, View = View };
}