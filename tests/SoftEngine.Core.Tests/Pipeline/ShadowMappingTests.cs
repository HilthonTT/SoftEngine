using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry.Primitives;
using SoftEngine.Core.Pipeline;
using SoftEngine.Core.Pipeline.Shadows;
using SoftEngine.Core.Rasterization.Painters;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Cameras;
using SoftEngine.Core.Scenes.Graph;
using SoftEngine.Core.Scenes.Lights;
using SoftEngine.Core.Scenes.Projections;
using System.Numerics;

namespace SoftEngine.Core.Tests.Pipeline;

public class ShadowMappingTests
{
    private sealed class FixedCamera(Vector3 position) : ICamera
    {
        public Vector3 Position { get; set; } = position;

        public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, Vector3.Zero, Vector3.UnitY);
    }

    private static ShadowSettings Settings() => new()
    {
        Enabled = true,
        Resolution = 256,
        SoftFilter = false,
    };

    private static SimpleWorld CubeWorld() => new()
    {
        Meshes = [new Cube()],
        Lights = [new DirectionalLight { Direction = -Vector3.UnitY }],
    };

    [Fact]
    public void Render_PointUnderTheCaster_IsShadowed()
    {
        var world = CubeWorld();

        var map = new ShadowMapRenderer().Render(world, world.Lights[0], Settings());

        Assert.NotNull(map);
        Assert.True(map.Visibility(new Vector3(0, -1.2f, 0), 1f) < 1f);
    }

    [Fact]
    public void Render_PointOnTheCastersLitFace_IsNotSelfShadowed()
    {
        var world = CubeWorld();

        var map = new ShadowMapRenderer().Render(world, world.Lights[0], Settings());

        Assert.Equal(1f, map!.Visibility(new Vector3(0, 0.5f, 0), 1f));
    }

    [Fact]
    public void Visibility_OutsideTheMappedArea_IsFullyLit()
    {
        var world = CubeWorld();

        var map = new ShadowMapRenderer().Render(world, world.Lights[0], Settings());

        Assert.Equal(1f, map!.Visibility(new Vector3(50f, -1.2f, 0), 1f));
    }

    [Fact]
    public void Visibility_Strength_ScalesHowDarkTheShadowGoes()
    {
        var world = CubeWorld();

        var settings = Settings();
        settings.Strength = 0.5f;

        var map = new ShadowMapRenderer().Render(world, world.Lights[0], settings);

        Assert.Equal(0.5f, map!.Visibility(new Vector3(0, -1.2f, 0), 1f), 3);
    }

    [Fact]
    public void Render_EmptyWorld_ProducesNoMap()
    {
        var world = new SimpleWorld { Lights = [new DirectionalLight()] };

        Assert.Null(new ShadowMapRenderer().Render(world, world.Lights[0], Settings()));
    }

    [Fact]
    public void Render_OnlyTransparentMeshes_ProducesNoMap()
    {
        var world = CubeWorld();
        ((Cube)world.Meshes[0]).Opacity = 0.5f;

        Assert.Null(new ShadowMapRenderer().Render(world, world.Lights[0], Settings()));
    }

    [Fact]
    public void Render_HiddenMesh_CastsNoShadow()
    {
        var world = CubeWorld();
        world.Meshes.Add(new Cube { Position = new Vector3(4f, 0, 0) });
        ((Cube)world.Meshes[0]).Visible = false;

        var map = new ShadowMapRenderer().Render(world, world.Lights[0], Settings());

        Assert.NotNull(map);
        Assert.Equal(1f, map.Visibility(new Vector3(0, -1.2f, 0), 1f));
        Assert.True(map.Visibility(new Vector3(4f, -1.2f, 0), 1f) < 1f);
    }

    [Fact]
    public void Render_ReusedRenderer_ProducesTheSameMapEveryFrame()
    {
        var world = CubeWorld();
        var renderer = new ShadowMapRenderer();

        var first = renderer.Render(world, world.Lights[0], Settings());
        var firstDepth = (float[])first!.Depth.Clone();

        var second = renderer.Render(world, world.Lights[0], Settings());

        Assert.Equal(firstDepth, second!.Depth);
    }

    private static (Renderer Renderer, Scene Scene) ShadowScene()
    {
        var renderer = new Renderer();
        var surface = new FrameBuffer(128, 128) { Stats = renderer.Stats };

        var floor = new Cube { Position = new Vector3(0, -2f, 0), Scale = new Vector3(10f, 0.2f, 10f) };
        var blocker = new Cube { Position = new Vector3(0, 1f, 0), Scale = new Vector3(3f, 0.2f, 3f) };

        var scene = new Scene
        {
            Surface = surface,
            Camera = new FixedCamera(new Vector3(0, 4f, 14f)),
            Projection = new PerspectiveProjection(MathF.PI / 4f, 0.1f, 100f),
            World = new SimpleWorld
            {
                Meshes = [floor, blocker],
                Lights = [new DirectionalLight { Direction = new Vector3(0, -1f, -0.15f) }],
            },
            Shadows = Settings(),
        };

        return (renderer, scene);
    }

    private static long Luminance(FrameBuffer surface)
    {
        long total = 0;
        foreach (var packed in surface.Screen)
        {
            var color = ColorRGB.FromPacked(packed);
            total += color.R + color.G + color.B;
        }
        return total;
    }

    [Fact]
    public void Render_WithShadows_DarkensTheSceneComparedToWithout()
    {
        var (renderer, scene) = ShadowScene();

        scene.Shadows.Enabled = false;
        renderer.Render(scene, new PhongPainter());
        var unshadowed = Luminance(scene.Surface);

        scene.Shadows.Enabled = true;
        renderer.Render(scene, new PhongPainter());
        var shadowed = Luminance(scene.Surface);

        Assert.True(renderer.Stats.DrawnPixelCount > 0);
        Assert.True(shadowed < unshadowed, $"expected the shadowed frame to be darker ({shadowed} vs {unshadowed})");
    }

    [Fact]
    public void Render_WithShadows_PublishesTheMapOnTheScene()
    {
        var (renderer, scene) = ShadowScene();

        renderer.Render(scene, new PhongPainter());

        Assert.NotNull(scene.ShadowMap);
        Assert.Equal(scene.Shadows.Resolution, scene.ShadowMap.Resolution);
    }

    [Fact]
    public void Render_WithShadowsDisabled_ClearsTheScenesMap()
    {
        var (renderer, scene) = ShadowScene();

        renderer.Render(scene, new PhongPainter());
        Assert.NotNull(scene.ShadowMap);

        scene.Shadows.Enabled = false;
        renderer.Render(scene, new PhongPainter());

        Assert.Null(scene.ShadowMap);
    }

    [Fact]
    public void Render_WithShadows_WorksForEveryLitPainter()
    {
        foreach (var painter in new Func<SoftEngine.Core.Rasterization.IPainter>[]
        {
            () => new FlatPainter(),
            () => new GouraudPainter(),
            () => new PhongPainter(),
            () => new TexturedPainter(),
            () => new MaterialPainter(),
        })
        {
            var (renderer, scene) = ShadowScene();

            renderer.Render(scene, painter());

            Assert.True(renderer.Stats.DrawnPixelCount > 0);
            Assert.NotNull(scene.ShadowMap);
        }
    }

    [Fact]
    public void Render_WorldWithoutLights_StillShadowsFromTheFallbackLight()
    {
        var (renderer, scene) = ShadowScene();
        scene.World.Lights.Clear();

        renderer.Render(scene, new PhongPainter());

        Assert.NotNull(scene.ShadowMap);
    }

    [Fact]
    public void Render_WorldWithoutLights_ShadowsFromThePaintersOwnLightNotTheSharedDefault()
    {
        var renderer = new Renderer();

        var scene = new Scene
        {
            Surface = new FrameBuffer(64, 64) { Stats = renderer.Stats },
            Camera = new FixedCamera(new Vector3(0, 0, 6f)),
            Projection = new PerspectiveProjection(MathF.PI / 4f, 0.1f, 100f),
            World = new SimpleWorld { Meshes = [new Cube()], Lights = [] },
            Shadows = Settings(),
        };

        renderer.Render(scene, new PhongPainter(new DirectionalLight { Direction = -Vector3.UnitX }));

        Assert.NotNull(scene.ShadowMap);
        Assert.True(
            scene.ShadowMap.Visibility(new Vector3(-1.2f, 0, 0), 1f) < 1f,
            "the point behind the cube, as the painter's light sees it, should be shadowed");
    }

    [Fact]
    public void Render_CasterScaledByItsParentNode_IsCoveredByTheMap()
    {
        var node = new SceneNode("rig") { Scale = new Vector3(8f, 8f, 8f) };
        node.UpdateWorldMatrices();

        var world = new SimpleWorld
        {
            Meshes = [new Cube { Parent = node }],
            Lights = [new DirectionalLight { Direction = -Vector3.UnitY }],
        };

        var map = new ShadowMapRenderer().Render(world, world.Lights[0], Settings());

        Assert.NotNull(map);

        Assert.True(map.Visibility(new Vector3(3f, -5f, 3f), 1f) < 1f);
    }
}
