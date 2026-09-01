using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Textures;
using System.Numerics;

namespace SoftEngine.Core.Geometry.Primitives;

public sealed class TexturedCube : Mesh
{
    private static readonly Vector3[] _vertices;
    private static readonly Vector3[] _normals;
    private static readonly Vector2[] _uvs;
    private static readonly Triangle[] _triangles;

    static TexturedCube()
    {
        var h = 0.5f;
        (Vector3 Origin, Vector3 U, Vector3 V)[] faces =
        [
            (new(-h, -h, h), Vector3.UnitX, Vector3.UnitY),
            (new(-h, -h, -h), Vector3.UnitY, Vector3.UnitX),
            (new(h, -h, -h), Vector3.UnitY, Vector3.UnitZ),
            (new(-h, -h, -h), Vector3.UnitZ, Vector3.UnitY),
            (new(-h, h, -h), Vector3.UnitZ, Vector3.UnitX),
            (new(-h, -h, -h), Vector3.UnitX, Vector3.UnitZ),
        ];

        _vertices = new Vector3[faces.Length * 4];
        _normals = new Vector3[faces.Length * 4];
        _uvs = new Vector2[faces.Length * 4];
        _triangles = new Triangle[faces.Length * 2];

        for (var f = 0; f < faces.Length; f++)
        {
            var (origin, u, v) = faces[f];
            var normal = Vector3.Cross(u, v);
            var baseIndex = f * 4;

            _vertices[baseIndex + 0] = origin;
            _vertices[baseIndex + 1] = origin + u;
            _vertices[baseIndex + 2] = origin + u + v;
            _vertices[baseIndex + 3] = origin + v;

            _uvs[baseIndex + 0] = new Vector2(0, 0);
            _uvs[baseIndex + 1] = new Vector2(1, 0);
            _uvs[baseIndex + 2] = new Vector2(1, 1);
            _uvs[baseIndex + 3] = new Vector2(0, 1);

            for (var i = 0; i < 4; i++)
            {
                _normals[baseIndex + i] = normal;
            }

            _triangles[f * 2] = new Triangle(baseIndex, baseIndex + 1, baseIndex + 2);
            _triangles[f * 2 + 1] = new Triangle(baseIndex + 2, baseIndex + 3, baseIndex);
        }
    }

    public TexturedCube(Texture? texture = null)
        : base(_vertices, _triangles, _normals, [.. Enumerable.Repeat(ColorRGB.White, _triangles.Length)])
    {
        TexCoords = _uvs;
        Texture = texture ?? Texture.Checkerboard(256, 8, new ColorRGB(225, 225, 230), new ColorRGB(98, 88, 158));
    }
}
