using System.Numerics;

namespace SoftEngine.Core.Geometry.Primitives;

/// <summary>
/// A rectangular box centred on the origin, with a hard normal and its own 0..1 UV square per
/// face — six quads that share no corners, because a cube's corner belongs to three surfaces at
/// right angles and one vertex cannot hold three normals or three texture coordinates.
///
/// <para>
/// The third box in this folder, and the only one a caller can size. <see cref="Cube"/> is the
/// fixed unit demo object whose rainbow faces come from a <em>static</em> colour array shared by
/// every instance, and <see cref="TexturedCube"/> always carries a texture. This one takes its
/// dimensions, owns its own colours, and starts out the same neutral grey as every other
/// generated primitive — which is what a box added to somebody else's scene should look like.
/// </para>
/// </summary>
public sealed class Box : Mesh
{
    /// <summary>
    /// Each face as the corner its two edges leave from, in units of the box's half extent, and
    /// those two edge directions. The pair is ordered so that U × V points out of the solid:
    /// <see cref="PrimitiveBuilder.AddQuad"/> winds counter-clockwise seen from outside, and a
    /// face given the other way round is the one that vanishes under back-face culling.
    /// </summary>
    private static readonly (Vector3 Corner, Vector3 U, Vector3 V)[] _faces =
    [
        (new Vector3(-1, -1, 1), Vector3.UnitX, Vector3.UnitY),   // +Z
        (new Vector3(-1, -1, -1), Vector3.UnitY, Vector3.UnitX),  // -Z
        (new Vector3(1, -1, -1), Vector3.UnitY, Vector3.UnitZ),   // +X
        (new Vector3(-1, -1, -1), Vector3.UnitZ, Vector3.UnitY),  // -X
        (new Vector3(-1, 1, -1), Vector3.UnitZ, Vector3.UnitX),   // +Y
        (new Vector3(-1, -1, -1), Vector3.UnitX, Vector3.UnitZ),  // -Y
    ];

    /// <param name="width">Extent along X.</param>
    /// <param name="height">Extent along Y.</param>
    /// <param name="depth">Extent along Z.</param>
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
            // The edge vectors are unit axes, so multiplying them by the size component-wise
            // picks each one's own dimension out of it — the alternative, scaling both by a
            // single number, gives every face the width of the box whichever way it runs.
            var origin = corner * half;
            var edgeU = u * size;
            var edgeV = v * size;

            // From the unscaled axes: an axis-aligned face points the same way whatever the
            // box's proportions, and the cross of two unit axes is already unit length.
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
