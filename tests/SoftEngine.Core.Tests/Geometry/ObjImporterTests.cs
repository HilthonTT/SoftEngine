using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Geometry.Import;
using SoftEngine.Core.Textures;
using System.Numerics;

namespace SoftEngine.Core.Tests.Geometry;

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

        var meshes = ObjImporter.Import(path);

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

        var meshes = ObjImporter.Import(path);

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

        var meshes = ObjImporter.Import(path);

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

        var meshes = ObjImporter.Import(path);

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

        var meshes = ObjImporter.Import(path);

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

        var meshes = ObjImporter.Import(path);

        Assert.Equal(meshes[0].Vertices.Length, meshes[0].NormVertices.Length);
        Assert.All(meshes[0].NormVertices, n => Assert.True(n.Length() > 0.99f));
    }

    private string WriteMaterialModel(string materialLibrary)
    {
        File.WriteAllText(Path.Combine(_directory, "model.mtl"), materialLibrary);

        return WriteObj("""
            mtllib model.mtl
            usemtl surface
            v 0 0 0
            v 1 0 0
            v 0 1 0
            vt 0 0
            vt 1 0
            vt 0 1
            f 1/1 2/2 3/3
            """);
    }

    [Fact]
    public void ImportObj_MaterialHighlight_IsCarriedOntoTheMesh()
    {
        var path = WriteMaterialModel("""
            newmtl surface
            Kd 1 0 0
            Ks 1 1 1
            Ns 64
            """);

        var mesh = Assert.Single(ObjImporter.Import(path));

        Assert.NotNull(mesh.Material);
        Assert.Equal(64f, mesh.Material.Shininess);
        Assert.Equal(1f, mesh.Material.SpecularStrength, 3);
        Assert.Equal(255, mesh.Material.Diffuse.R);
    }

    [Fact]
    public void ImportObj_MaterialOpacity_MakesTheMeshTransparent()
    {
        var path = WriteMaterialModel("""
            newmtl surface
            d 0.25
            """);

        Assert.Equal(0.25f, Assert.Single(ObjImporter.Import(path)).Opacity, 3);
    }

    [Fact]
    public void ImportObj_TrIsTheInverseOfD()
    {
        var path = WriteMaterialModel("""
            newmtl surface
            Tr 0.25
            """);

        Assert.Equal(0.75f, Assert.Single(ObjImporter.Import(path)).Opacity, 3);
    }

    [Theory]
    [InlineData("map_Bump")]
    [InlineData("map_bump")]
    [InlineData("bump")]
    [InlineData("norm")]
    public void ImportObj_NormalMap_IsRecognizedUnderEverySpelling(string keyword)
    {
        var path = WriteMaterialModel($"""
            newmtl surface
            {keyword} surface_normal.png
            """);

        var loaded = new List<string>();

        var mesh = Assert.Single(ObjImporter.Import(path, null, file =>
        {
            loaded.Add(Path.GetFileName(file));
            return Texture.Checkerboard(4, 2, ColorRGB.White, ColorRGB.Gray);
        }));

        Assert.Equal(["surface_normal.png"], loaded);
        Assert.NotNull(mesh.Material?.NormalMap);
    }

    [Fact]
    public void ImportObj_EveryMapKind_IsLoadedOnce()
    {
        var path = WriteMaterialModel("""
            newmtl surface
            map_Kd albedo.png
            map_Bump normal.png
            map_Ks gloss.png
            """);

        var loaded = new List<string>();

        var mesh = Assert.Single(ObjImporter.Import(path, null, file =>
        {
            loaded.Add(Path.GetFileName(file));
            return Texture.Checkerboard(4, 2, ColorRGB.White, ColorRGB.Gray);
        }));

        Assert.Equal(3, loaded.Count);
        Assert.NotNull(mesh.Material?.DiffuseMap);
        Assert.NotNull(mesh.Material.NormalMap);
        Assert.NotNull(mesh.Material.SpecularMap);

        Assert.Same(mesh.Material.DiffuseMap, mesh.Texture);
    }

    [Fact]
    public void ImportObj_TextureOptionsBeforeTheFilename_AreIgnored()
    {
        var path = WriteMaterialModel("""
            newmtl surface
            map_Bump -bm 0.5 normal.png
            """);

        var loaded = new List<string>();

        ObjImporter.Import(path, null, file =>
        {
            loaded.Add(Path.GetFileName(file));
            return null;
        });

        Assert.Equal(["normal.png"], loaded);
    }
}
