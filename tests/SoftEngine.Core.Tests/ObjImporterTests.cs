using SoftEngine.Core.Geometry;
using System.Numerics;

namespace SoftEngine.Core.Tests;

public class ObjImporterTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("objtests").FullName;

    public void Dispose() => Directory.Delete(_directory, true);

    private string WriteObj(string content)
    {
        var path = Path.Combine(_directory, "model.obj");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void ImportObj_SingleTriangle_ImportsVerticesAndFace()
    {
        var path = WriteObj("""
            v 0 0 0
            v 1 0 0
            v 0 1 0
            f 1 2 3
            """);

        var meshes = ObjImporter.ImportObj(path);

        var mesh = Assert.Single(meshes);
        Assert.Equal(3, mesh.Vertices.Length);
        Assert.Single(mesh.Triangles);
        Assert.Equal(new Vector3(1, 0, 0), mesh.Vertices[1]);
    }

    [Fact]
    public void ImportObj_Quad_IsTriangulated()
    {
        var path = WriteObj("""
            v 0 0 0
            v 1 0 0
            v 1 1 0
            v 0 1 0
            f 1 2 3 4
            """);

        var meshes = ObjImporter.ImportObj(path);

        Assert.Equal(2, meshes[0].Triangles.Length);
    }

    [Fact]
    public void ImportObj_NegativeIndices_ResolveFromEnd()
    {
        var path = WriteObj("""
            v 0 0 0
            v 1 0 0
            v 0 1 0
            f -3 -2 -1
            """);

        var meshes = ObjImporter.ImportObj(path);

        var t = meshes[0].Triangles[0];
        Assert.Equal(new Vector3(0, 0, 0), meshes[0].Vertices[t.I0]);
        Assert.Equal(new Vector3(0, 1, 0), meshes[0].Vertices[t.I2]);
    }

    [Fact]
    public void ImportObj_TexCoords_AreImported()
    {
        var path = WriteObj("""
            v 0 0 0
            v 1 0 0
            v 0 1 0
            vt 0 0
            vt 1 0
            vt 0 1
            f 1/1 2/2 3/3
            """);

        var meshes = ObjImporter.ImportObj(path);

        Assert.NotNull(meshes[0].TexCoords);
        Assert.Equal(meshes[0].Vertices.Length, meshes[0].TexCoords!.Length);
    }

    [Fact]
    public void ImportObj_CommentsAndBlankLines_AreIgnored()
    {
        var path = WriteObj("""
            # a comment

            v 0 0 0
            v 1 0 0
            v 0 1 0
            f 1 2 3
            """);

        var meshes = ObjImporter.ImportObj(path);

        Assert.Single(meshes[0].Triangles);
    }

    [Fact]
    public void ImportObj_MissingNormals_AreComputed()
    {
        var path = WriteObj("""
            v 0 0 0
            v 1 0 0
            v 0 1 0
            f 1 2 3
            """);

        var meshes = ObjImporter.ImportObj(path);

        Assert.Equal(meshes[0].Vertices.Length, meshes[0].NormVertices.Length);
        Assert.All(meshes[0].NormVertices, n => Assert.True(n.Length() > 0.99f));
    }
}
