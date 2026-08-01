using System.Numerics;

namespace SoftEngine.Core.Geometry.Primitives;

/// <summary>
/// A flat grid in the XZ plane facing +Y — the ground every test scene and benchmark used to
/// hand-roll four vertices at a time. Subdivide it (<paramref name="columns"/> /
/// <paramref name="rows"/>) when the surface needs vertices for a shader to interpolate across:
/// a two-triangle floor has nothing for per-vertex lighting or a vertex-lit fog to work with.
/// For a wall or a billboard, rotate it — a plane facing the camera is this one turned -90°
/// about X.
/// <para>
/// Named PlaneMesh rather than Plane because <see cref="System.Numerics"/> already has a Plane,
/// and a file importing both namespaces could not then name either.
/// </para>
/// </summary>
public sealed class PlaneMesh : Mesh
{
    /// <param name="width">Extent along X.</param>
    /// <param name="depth">Extent along Z.</param>
    /// <param name="columns">Quads along X. Clamped to at least one.</param>
    /// <param name="rows">Quads along Z. Clamped to at least one.</param>
    /// <param name="uvScale">
    /// How many times the texture repeats across the plane. The UVs run 0..1 by default; a large
    /// floor usually wants them to tile instead, which needs no second texture, only this.
    /// </param>
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
                // Round the quad the way that puts +Y outward: along +Z first, then +X.
                var corner = (i * (rows + 1)) + j;
                builder.AddQuad(corner, corner + 1, corner + rows + 2, corner + rows + 1);
            }
        }

        return builder.Build();
    }
}
