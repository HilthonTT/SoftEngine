using SoftEngine.Core.Buffers;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Geometry.Primitives;
using SoftEngine.Core.Pipeline;
using SoftEngine.Core.Pipeline.Shadows;
using SoftEngine.Core.Rasterization.Painters;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Cameras;
using SoftEngine.Core.Scenes.Lights;
using SoftEngine.Core.Scenes.Projections;
using SoftEngine.Core.Shading;
using System.Numerics;

namespace SoftEngine.Core.Tests.Pipeline;

public class CascadedShadowTests
{
    private const float Fov = MathF.PI / 4f;
    private const float Near = 0.5f;
    private const float Far = 200f;

    private sealed class FixedCamera(Vector3 position) : ICamera
    {
        public Vector3 Position { get; set; } = position;

        public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, Position + new Vector3(0, -0.2f, -1f), Vector3.UnitY);
    }

    private static ShadowSettings Settings(int cascades) => new()
    {
        Enabled = true,
        Resolution = 256,
        SoftFilter = false,
        CascadeCount = cascades,
    };

    private static ShadowView View(Vector3 eye) => new(
        new FixedCamera(eye).ViewMatrix,
        Matrix4x4.CreatePerspectiveFieldOfView(Fov, 16f / 9f, Near, Far),
        Near,
        Far);

    private static SimpleWorld StripWorld()
    {
        var world = new SimpleWorld
        {
            Lights = [new DirectionalLight { Direction = new Vector3(0.2f, -1f, 0.1f) }],
        };

        world.Meshes.Add(new Cube { Position = new Vector3(0, -1f, -80f), Scale = new Vector3(40f, 0.5f, 200f) });

        for (var z = -5; z > -160; z -= 10)
        {
            world.Meshes.Add(new Cube { Position = new Vector3(0, 1f, z), Scale = new Vector3(2f, 4f, 2f) });
        }

        return world;
    }

    [Fact]
    public void Render_WithAView_FillsEveryCascade()
    {
        var world = StripWorld();

        var map = new ShadowMapRenderer().Render(world, world.Lights[0], Settings(3), View(new Vector3(0, 3f, 10f)));

        Assert.NotNull(map);
        Assert.Equal(3, map.CascadeCount);

        for (var cascade = 0; cascade < map.CascadeCount; cascade++)
        {
            var filled = 0;
            foreach (var depth in map.DepthOf(cascade))
            {
                if (depth < ShadowMap.Empty)
                {
                    filled++;
                }
            }

            Assert.True(filled > 0, $"cascade {cascade} is empty");
        }
    }

    [Fact]
    public void Render_NearCascade_CoversLessGroundThanTheFarOne()
    {
        var world = StripWorld();

        var map = new ShadowMapRenderer().Render(world, world.Lights[0], Settings(3), View(new Vector3(0, 3f, 10f)));

        Assert.NotNull(map);

        var near = MathF.Abs(map.LightViewProjectionOf(0).M11);
        var middle = MathF.Abs(map.LightViewProjectionOf(1).M11);
        var far = MathF.Abs(map.LightViewProjectionOf(2).M11);

        Assert.True(near > middle, $"cascade 0 should be tighter than cascade 1 ({near} vs {middle})");
        Assert.True(middle > far, $"cascade 1 should be tighter than cascade 2 ({middle} vs {far})");
    }

    [Fact]
    public void CascadeAt_PicksTheNearestCascadeThatCoversThePoint()
    {
        var world = StripWorld();
        var eye = new Vector3(0, 3f, 10f);

        var map = new ShadowMapRenderer().Render(world, world.Lights[0], Settings(3), View(eye));

        Assert.NotNull(map);

        var close = map.CascadeAt(new Vector3(0, 0f, 5f));
        var distant = map.CascadeAt(new Vector3(0, 0f, -120f));

        Assert.Equal(0, close);
        Assert.True(distant > close, $"a point 130 units out should fall in a later cascade than one 5 units out (got {distant})");
    }

    [Fact]
    public void Render_WithCascades_StillShadowsUnderACaster()
    {
        var world = StripWorld();

        var map = new ShadowMapRenderer().Render(world, world.Lights[0], Settings(3), View(new Vector3(0, 3f, 10f)));

        Assert.NotNull(map);

        Assert.True(map.Visibility(new Vector3(0, -0.4f, -5f), 1f) < 1f);

        Assert.Equal(1f, map.Visibility(new Vector3(15f, -0.4f, -5f), 1f));
    }

    [Fact]
    public void Render_WithoutAView_FallsBackToASingleMap()
    {
        var world = StripWorld();

        var map = new ShadowMapRenderer().Render(world, world.Lights[0], Settings(4));

        Assert.NotNull(map);
        Assert.Equal(1, map.CascadeCount);
    }

    [Fact]
    public void Render_CascadeGrid_StaysAlignedAsTheCameraMoves()
    {
        var world = StripWorld();
        var renderer = new ShadowMapRenderer();

        var probe = new Vector3(0f, 0f, -5f);

        var before = TexelOf(renderer.Render(world, world.Lights[0], Settings(3), View(new Vector3(0, 3f, 10f))), probe);

        var after = TexelOf(renderer.Render(world, world.Lights[0], Settings(3), View(new Vector3(0.013f, 3f, 10.007f))), probe);

        Assert.Equal(before - MathF.Floor(before), after - MathF.Floor(after), 3);
    }

    private static float TexelOf(ShadowMap? map, Vector3 world)
    {
        Assert.NotNull(map);

        var light = Vector4.Transform(world, map.LightViewProjectionOf(0));

        return (light.X * 0.5f + 0.5f) * map.Resolution;
    }

    [Fact]
    public void Render_MaxDistance_TightensEveryCascade()
    {
        var world = StripWorld();
        var view = View(new Vector3(0, 3f, 10f));

        var full = new ShadowMapRenderer().Render(world, world.Lights[0], Settings(3), view);

        var capped = Settings(3);
        capped.MaxDistance = 40f;

        var limited = new ShadowMapRenderer().Render(world, world.Lights[0], capped, view);

        Assert.NotNull(full);
        Assert.NotNull(limited);

        Assert.True(
            MathF.Abs(limited.LightViewProjectionOf(0).M11) > MathF.Abs(full.LightViewProjectionOf(0).M11),
            "capping the shadow distance should make the near cascade tighter");
    }

    [Fact]
    public void Render_ReusedRenderer_ProducesTheSameCascadesEveryFrame()
    {
        var world = StripWorld();
        var renderer = new ShadowMapRenderer();
        var view = View(new Vector3(0, 3f, 10f));

        var first = renderer.Render(world, world.Lights[0], Settings(3), view);
        var firstDepth = (float[])first!.Depth.Clone();

        var second = renderer.Render(world, world.Lights[0], Settings(3), view);

        Assert.Equal(firstDepth, second!.Depth);
    }

    [Fact]
    public void Render_EachCascade_OnlyRasterizesTheCastersThatReachIt()
    {
        var world = StripWorld();

        var single = new ShadowMapRenderer();
        single.Render(world, world.Lights[0], Settings(1), View(new Vector3(0, 3f, 10f)));

        var cascaded = new ShadowMapRenderer();
        cascaded.Render(world, world.Lights[0], Settings(3), View(new Vector3(0, 3f, 10f)));

        Assert.True(
            cascaded.TriangleCount < single.TriangleCount * 3,
            $"three cascades rasterized {cascaded.TriangleCount} triangles against {single.TriangleCount} for one map");
    }

    [Fact]
    public void Render_SceneWithCascades_ShadowsThroughTheRenderer()
    {
        var renderer = new Renderer();

        var scene = new Scene
        {
            Surface = new FrameBuffer(160, 90) { Stats = renderer.Stats },
            Camera = new FixedCamera(new Vector3(0, 3f, 10f)),
            Projection = new PerspectiveProjection(Fov, Near, Far),
            World = StripWorld(),
            Shadows = Settings(3),
        };

        renderer.Render(scene, new PhongPainter());

        Assert.NotNull(scene.ShadowMap);
        Assert.Equal(3, scene.ShadowMap.CascadeCount);
        Assert.True(renderer.Stats.DrawnPixelCount > 0);
    }

    [Fact]
    public void Render_OrthographicScene_KeepsASingleMap()
    {
        var renderer = new Renderer();

        var scene = new Scene
        {
            Surface = new FrameBuffer(160, 90) { Stats = renderer.Stats },
            Camera = new FixedCamera(new Vector3(0, 3f, 10f)),
            Projection = new OrthographicProjection(40f, 0.1f, 400f),
            World = StripWorld(),
            Shadows = Settings(3),
        };

        renderer.Render(scene, new PhongPainter());

        Assert.NotNull(scene.ShadowMap);
        Assert.Equal(1, scene.ShadowMap.CascadeCount);
    }

    [Fact]
    public void CascadeCount_IsClampedToWhatTheMapCanHold()
    {
        var settings = new ShadowSettings { CascadeCount = 99 };
        Assert.Equal(ShadowMap.MaxCascades, settings.CascadeCount);

        settings.CascadeCount = 0;
        Assert.Equal(1, settings.CascadeCount);
    }
}
