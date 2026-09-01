using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Math;
using SoftEngine.Core.Scenes.Graph;
using SoftEngine.Core.Textures;
using System.Numerics;

namespace SoftEngine.Core.Geometry;

public class Mesh : IMesh
{
    public Mesh(
        Vector3[] vertices,
        Triangle[] triangleIndices,
        Vector3[]? vertexNormals = null,
        ColorRGB[]? triangleColors = null)
    {
        Vertices = vertices;
        Triangles = triangleIndices;

        NormVertices = vertexNormals is null ? [.. this.CalculateVertexNormals()] : vertexNormals;
        TriangleColors = triangleColors ?? [.. Enumerable.Repeat(ColorRGB.Gray, Triangles.Length)];

        float maxLengthSquared = 0f;
        foreach (var vertex in vertices)
        {
            maxLengthSquared = MathF.Max(maxLengthSquared, vertex.LengthSquared());
        }
        BoundingRadius = MathF.Sqrt(maxLengthSquared);

        Scale = Vector3.One;

        Rotation = new Rotation3D(0, 0, 0);
    }

    public Rotation3D Rotation { get; set; }

    public Vector3 Position { get; set; }

    public SceneNode? Parent { get; set; }

    public Vector3 Scale { get; set; }

    public ColorRGB[] TriangleColors { get; }

    public Triangle[] Triangles { get; }

    public Vector3[] Vertices { get; }

    public Vector3[] NormVertices { get; }

    public bool Visible { get; set; } = true;

    public float Opacity { get; set; } = 1f;

    public Vector2[]? TexCoords { get; set; }

    public Material Material { get; set; } = new();

    public Texture? Texture
    {
        get => Material.DiffuseMap;
        set => Material.DiffuseMap = value;
    }

    public Vector4[]? Tangents { get; set; }

    public virtual float BoundingRadius { get; }

    public virtual void EnsureTangents()
    {
        if (Tangents is not null || TexCoords is null)
        {
            return;
        }

        Tangents = TangentBuilder.Build(Vertices, NormVertices, TexCoords, Triangles);
    }
}
