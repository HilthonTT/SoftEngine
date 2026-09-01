using System.Numerics;

namespace SoftEngine.Core.Geometry.Primitives;

public sealed class Box : Mesh
{
    private static readonly (Vector3 Corner, Vector3 U, Vector3 V)[] _faces =
    [
        (new Vector3(-1, -1, 1), Vector3.UnitX, Vector3.UnitY),
        (new Vector3(-1, -1, -1), Vector3.UnitY, Vector3.UnitX),
        (new Vector3(1, -1, -1), Vector3.UnitY, Vector3.UnitZ),
        (new Vector3(-1, -1, -1), Vector3.UnitZ, Vector3.UnitY),
        (new Vector3(-1, 1, -1), Vector3.UnitZ, Vector3.UnitX),
        (new Vector3(-1, -1, -1), Vector3.UnitX, Vector3.UnitZ),
    ];

    public Box(float width = 1f, float height = 1f, float depth = 1f)
        : this(Build(new Vector3(width, height, depth)))
    {
    }

    private Box(PrimitiveGeometry geometry)
        : base(geometry.Vertices, geometry.Triangles, geometry.Normals)
    {
        TexCoords = geometry.TexCoords;
    }

    private static PrimitiveGeometry Build(Vector3 size)
    {
        var builder = new PrimitiveBuilder();
        var half = size * 0.5f;

        foreach (var (corner, u, v) in _faces)
        {
            var origin = corner * half;
            var edgeU = u * size;
            var edgeV = v * size;

            var normal = Vector3.Cross(u, v);

            var a = builder.Add(origin, normal, new Vector2(0, 0));
            var b = builder.Add(origin + edgeU, normal, new Vector2(1, 0));
            var c = builder.Add(origin + edgeU + edgeV, normal, new Vector2(1, 1));
            var d = builder.Add(origin + edgeV, normal, new Vector2(0, 1));

            builder.AddQuad(a, b, c, d);
        }

        return builder.Build();
    }
}
