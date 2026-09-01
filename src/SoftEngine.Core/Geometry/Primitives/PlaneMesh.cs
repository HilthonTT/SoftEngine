using System.Numerics;

namespace SoftEngine.Core.Geometry.Primitives;

public sealed class PlaneMesh : Mesh
{
    public PlaneMesh(float width = 1f, float depth = 1f, int columns = 1, int rows = 1, float uvScale = 1f)
        : this(Build(width, depth, int.Max(1, columns), int.Max(1, rows), uvScale))
    {
    }

    private PlaneMesh(PrimitiveGeometry geometry)
        : base(geometry.Vertices, geometry.Triangles, geometry.Normals)
    {
        TexCoords = geometry.TexCoords;
    }

    private static PrimitiveGeometry Build(float width, float depth, int columns, int rows, float uvScale)
    {
        var builder = new PrimitiveBuilder();

        for (var i = 0; i <= columns; i++)
        {
            for (var j = 0; j <= rows; j++)
            {
                var u = (float)i / columns;
                var v = (float)j / rows;

                builder.Add(
                    new Vector3((u - 0.5f) * width, 0f, (v - 0.5f) * depth),
                    Vector3.UnitY,
                    new Vector2(u * uvScale, v * uvScale));
            }
        }

        for (var i = 0; i < columns; i++)
        {
            for (var j = 0; j < rows; j++)
            {
                var corner = (i * (rows + 1)) + j;
                builder.AddQuad(corner, corner + 1, corner + rows + 2, corner + rows + 1);
            }
        }

        return builder.Build();
    }
}
