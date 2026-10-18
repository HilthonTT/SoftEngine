using SoftEngine.Core.Geometry.Primitives;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Graph;
using SoftEngine.WinForms.Worlds;
using System.Numerics;

namespace SoftEngine.WinForms.Tests.Worlds;

public class DemoWorldsTests
{
    private static readonly HashSet<string> SlowImports = ["Juliet", "julietskin", "elefant"];

    public static TheoryData<string> DemoIds()
    {
        var ids = new TheoryData<string>();

        foreach (var demo in DemoWorlds.Demos.Where(demo => !SlowImports.Contains(demo.Id)))
        {
            ids.Add(demo.Id);
        }

        return ids;
    }

    [Theory]
    [InlineData("elefant.dae")]
    [InlineData("juliet.dae")]
    [InlineData("parrot.dae")]
    [InlineData("skull.dae")]
    [InlineData("teapot.dae")]
    public void BundledModels_AreCopiedNextToTheViewer(string fileName)
    {
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "Models", fileName)));
    }

    [Fact]
    public void Demos_HaveUniqueIds()
    {
        var ids = DemoWorlds.Demos.Select(demo => demo.Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(DemoIds))]
    public void BuildWorld_ProducesTheDemoItIsCataloguedUnder(string id)
    {
        var setup = DemoWorlds.BuildWorld(id, progress: null);

        if (id == "empty")
        {
            Assert.Empty(setup.World.Meshes);
        }
        else
        {
            Assert.NotEmpty(setup.World.Meshes);
        }

        Assert.True(setup.CameraPosition.Length() > 0f);
        Assert.True(setup.SkeletonTickSize > 0f);
    }

    [Fact]
    public void ProjectionFor_ReachesMeshesPlacedByTheirParentNode()
    {
        var node = new SceneNode("far") { Position = new Vector3(0, 0, 2000), Scale = Vector3.One, Rotation = Quaternion.Identity };
        node.UpdateWorldMatrices();

        var world = new SimpleWorld { Root = node };
        world.Meshes.Add(new Cube { Parent = node });

        var setup = new WorldSetup(world, new Vector3(0, 0, -5), null);

        var projection = DemoWorlds.ProjectionFor(setup);

        Assert.True(projection.ZFar > 2005f, $"far plane {projection.ZFar}");
    }

    [Fact]
    public void ProjectionFor_KeepsTheMinimumFarPlaneForSmallWorlds()
    {
        var world = new SimpleWorld();
        world.Meshes.Add(new Cube());

        var projection = DemoWorlds.ProjectionFor(new WorldSetup(world, new Vector3(0, 0, -5), null));

        Assert.Equal(500f, projection.ZFar);
    }

    [Fact]
    public void BuildWorldFromFile_FramesTheModelFromOutsideIt()
    {
        var path = Path.Combine(Path.GetTempPath(), $"softengine-{Guid.NewGuid():N}.obj");

        File.WriteAllText(path, """
            v -40 -40 0
            v 40 -40 0
            v 0 40 0
            f 1 2 3
            """);

        try
        {
            var setup = DemoWorlds.BuildWorldFromFile(path, progress: null);

            Assert.Single(setup.World.Meshes);
            Assert.Single(setup.World.Lights);
            Assert.True(setup.CameraPosition.Z < -40f, $"camera at {setup.CameraPosition}");
            Assert.True(setup.Projection!.ZFar >= 500f);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void BuildWorldFromFile_RejectsFormatsItCannotRead()
    {
        Assert.Throws<NotSupportedException>(() => DemoWorlds.BuildWorldFromFile("model.fbx", progress: null));
    }
}
