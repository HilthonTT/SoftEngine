using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Geometry.Primitives;
using SoftEngine.Core.Math;
using SoftEngine.Core.Pipeline;
using SoftEngine.Core.Pipeline.Debugging;
using SoftEngine.Core.Pipeline.PostProcess;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Cameras;
using SoftEngine.Core.Scenes.Lights;
using SoftEngine.Core.Scenes.Projections;
using SoftEngine.Core.Scenes.Serialization;
using System.Numerics;
using System.Text.Json;

namespace SoftEngine.Core.Tests.Scenes;

public class SceneSerializationTests
{
    private sealed class FixedCamera : ICamera
    {
        public Vector3 Position { get; set; }

        public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, Vector3.Zero, Vector3.UnitY);
    }

    private static Scene NewScene(int meshes = 2)
    {
        var world = new SimpleWorld { Meshes = [], Lights = [] };

        for (var i = 0; i < meshes; i++)
        {
            world.Meshes.Add(new Cube());
        }

        return new Scene
        {
            Surface = new FrameBuffer(64, 64),
            Camera = new FixedCamera(),
            Projection = new PerspectiveProjection(0.7f, 0.01f, 500f),
            World = world,
        };
    }

    private static PostProcessStack NewStack() => PostProcessStack.CreateDefault();

    /// <summary>
    /// The claim the format has to make: a scene set up, written out and read back is the same
    /// scene. Anything this misses is a setting that silently reverts when a file is reopened.
    /// </summary>
    [Fact]
    public void CaptureAndApply_ThroughJson_RoundTripsTheScene()
    {
        var source = NewScene();
        var settings = new RendererSettings();
        var post = NewStack();

        source.GammaCorrect = false;
        source.HighDynamicRange = false;
        source.AmbientIntensity = 0.7f;
        source.SkyIntensity = 2.5f;
        source.ShowSky = false;

        source.Fog.Enabled = true;
        source.Fog.Mode = FogMode.Exponential;
        source.Fog.Density = 0.13f;
        source.Fog.Color = new ColorRGB(10, 20, 30);

        source.Shadows.Enabled = true;
        source.Shadows.CascadeCount = 3;
        source.Shadows.Resolution = 2048;
        source.Shadows.DepthBias = 2.25f;
        source.Shadows.Strength = 0.8f;

        settings.BackFaceCulling = true;
        settings.ShowXZGrid = true;
        settings.OcclusionCulling = false;
        settings.DebugView = DebugView.Overdraw;

        source.Camera.Position = new Vector3(1f, 2f, -3f);

        source.World.Meshes[1].Position = new Vector3(4f, 0f, -1f);
        source.World.Meshes[1].Scale = new Vector3(2f, 0.5f, 1f);
        source.World.Meshes[1].Rotation = new Rotation3D(0.1f, 0.2f, 0.3f);

        source.World.Lights.Add(new DirectionalLight
        {
            Direction = new Vector3(-1f, -2f, -3f),
            Intensity = 0.9f,
            Color = new ColorRGB(255, 200, 150),
        });
        source.World.Lights.Add(new SpotLight
        {
            Position = new Vector3(0f, 10f, 0f),
            Intensity = 3f,
            Range = 40f,
            OuterAngle = 0.6f,
            InnerAngle = 0.3f,
        });

        post.Find<BloomEffect>()!.Enabled = true;
        post.Find<BloomEffect>()!.Threshold = 0.9f;
        post.Find<ToneMapEffect>()!.Operator = ToneMapOperator.Reinhard;
        post.Find<ToneMapEffect>()!.Exposure = 2.1f;

        var json = SceneSerializer.ToJson(SceneSerializer.Capture(source, settings, post));

        var target = NewScene();
        var targetSettings = new RendererSettings();
        var targetPost = NewStack();

        SceneSerializer.Apply(SceneSerializer.FromJson(json), target, targetSettings, targetPost);

        Assert.False(target.GammaCorrect);
        Assert.False(target.HighDynamicRange);
        Assert.Equal(0.7f, target.AmbientIntensity, 5);
        Assert.Equal(2.5f, target.SkyIntensity, 5);
        Assert.False(target.ShowSky);

        Assert.True(target.Fog.Enabled);
        Assert.Equal(FogMode.Exponential, target.Fog.Mode);
        Assert.Equal(0.13f, target.Fog.Density, 5);
        Assert.Equal(30, target.Fog.Color.B);

        Assert.True(target.Shadows.Enabled);
        Assert.Equal(3, target.Shadows.CascadeCount);
        Assert.Equal(2048, target.Shadows.Resolution);
        Assert.Equal(2.25f, target.Shadows.DepthBias, 5);
        Assert.Equal(0.8f, target.Shadows.Strength, 5);

        Assert.True(targetSettings.BackFaceCulling);
        Assert.True(targetSettings.ShowXZGrid);
        Assert.False(targetSettings.OcclusionCulling);
        Assert.Equal(DebugView.Overdraw, targetSettings.DebugView);

        Assert.Equal(new Vector3(1f, 2f, -3f), target.Camera.Position);

        Assert.Equal(new Vector3(4f, 0f, -1f), target.World.Meshes[1].Position);
        Assert.Equal(new Vector3(2f, 0.5f, 1f), target.World.Meshes[1].Scale);
        Assert.Equal(0.2f, target.World.Meshes[1].Rotation.YYaw, 5);

        var directional = Assert.IsType<DirectionalLight>(target.World.Lights[0]);
        Assert.Equal(0.9f, directional.Intensity, 5);
        Assert.Equal(200, directional.Color.G);
        Assert.Equal(Vector3.Normalize(new Vector3(-1f, -2f, -3f)), Vector3.Normalize(directional.Direction));

        var spot = Assert.IsType<SpotLight>(target.World.Lights[1]);
        Assert.Equal(40f, spot.Range, 5);
        Assert.Equal(0.6f, spot.OuterAngle, 5);
        Assert.Equal(0.3f, spot.InnerAngle, 5);

        Assert.True(targetPost.Find<BloomEffect>()!.Enabled);
        Assert.Equal(0.9f, targetPost.Find<BloomEffect>()!.Threshold, 5);
        Assert.Equal(ToneMapOperator.Reinhard, targetPost.Find<ToneMapEffect>()!.Operator);
        Assert.Equal(2.1f, targetPost.Find<ToneMapEffect>()!.Exposure, 5);

        var projection = Assert.IsType<PerspectiveProjection>(target.Projection);
        Assert.Equal(0.7f, projection.FieldOfView, 5);
        Assert.Equal(500f, projection.ZFar, 5);
    }

    [Fact]
    public void CaptureAndApply_OfAnOrthographicProjection_KeepsItParallel()
    {
        var source = NewScene();
        source.Projection = new OrthographicProjection(12f, 0.5f, 80f);

        var target = NewScene();
        SceneSerializer.Apply(SceneSerializer.FromJson(SceneSerializer.ToJson(SceneSerializer.Capture(source))), target);

        var projection = Assert.IsType<OrthographicProjection>(target.Projection);

        Assert.Equal(12f, projection.ViewHeight, 5);
        Assert.Equal(0.5f, projection.ZNear, 5);
        Assert.Equal(80f, projection.ZFar, 5);
    }

    /// <summary>
    /// An omitted section means "leave this alone". Without that rule a hand-written file would
    /// have to restate the whole scene to change one number in it.
    /// </summary>
    [Fact]
    public void Apply_OfADocumentWithOnlyACamera_ChangesNothingElse()
    {
        var scene = NewScene();
        var settings = new RendererSettings { ShowAxes = true, BackFaceCulling = true };

        scene.Fog.Enabled = true;
        scene.World.Meshes[0].Position = new Vector3(9f, 9f, 9f);

        var document = SceneSerializer.FromJson("""
            { "camera": { "position": [5, 0, -2] } }
            """);

        SceneSerializer.Apply(document, scene, settings);

        Assert.Equal(new Vector3(5f, 0f, -2f), scene.Camera.Position);

        Assert.True(scene.Fog.Enabled);
        Assert.True(settings.ShowAxes);
        Assert.True(settings.BackFaceCulling);
        Assert.Equal(new Vector3(9f, 9f, 9f), scene.World.Meshes[0].Position);
    }

    /// <summary>
    /// A light with no falloff has an infinite range, which JSON cannot write. Recording it as a
    /// very large number instead would turn "no falloff" into "an enormous falloff" — a different
    /// thing that happens to look the same in the scene it was tested on.
    /// </summary>
    [Fact]
    public void CaptureAndApply_OfALightWithNoFalloff_KeepsItInfinite()
    {
        var source = NewScene();
        source.World.Lights.Add(new PointLight { Position = Vector3.One });

        var json = SceneSerializer.ToJson(SceneSerializer.Capture(source));

        // The property, not the substring — "highDynamicRange" contains the word.
        Assert.DoesNotContain("\"range\"", json, StringComparison.Ordinal);

        var target = NewScene();
        SceneSerializer.Apply(SceneSerializer.FromJson(json), target);

        Assert.Equal(float.PositiveInfinity, Assert.IsType<PointLight>(target.World.Lights[0]).Range);
    }

    /// <summary>
    /// A document written against a model that has since been re-exported with fewer meshes is a
    /// scene that has partly gone stale, not a file to refuse.
    /// </summary>
    [Fact]
    public void Apply_WithMoreMeshesThanTheWorldHas_SkipsTheOnesThatFallOffTheEnd()
    {
        var source = NewScene(meshes: 4);
        source.World.Meshes[0].Position = new Vector3(1f, 0f, 0f);

        var document = SceneSerializer.Capture(source);

        var target = NewScene(meshes: 2);

        SceneSerializer.Apply(document, target);

        Assert.Equal(new Vector3(1f, 0f, 0f), target.World.Meshes[0].Position);
    }

    [Fact]
    public void Apply_WithAnUnknownBufferViewName_FallsBackToTheShadedImage()
    {
        var scene = NewScene();
        var settings = new RendererSettings { DebugView = DebugView.Depth };

        var document = SceneSerializer.FromJson("""
            { "rendering": { "debugView": "Reflections" } }
            """);

        SceneSerializer.Apply(document, scene, settings);

        Assert.Equal(DebugView.Off, settings.DebugView);
    }

    /// <summary>
    /// Vectors are written as arrays, and on <em>one line</em> — an indented writer breaks an
    /// array across a line per element, which would make the file less readable than the object
    /// form the array is here to replace.
    /// </summary>
    [Fact]
    public void Vectors_AreWrittenAsOneLineArraysAndReadFromEitherForm()
    {
        var scene = NewScene();
        scene.Camera.Position = new Vector3(1.5f, -2f, 3f);

        Assert.Contains("[1.5, -2, 3]", SceneSerializer.ToJson(SceneSerializer.Capture(scene)), StringComparison.Ordinal);

        var fromObject = SceneSerializer.FromJson("""
            { "camera": { "position": { "x": 7, "y": 8, "z": 9 } } }
            """);

        Assert.Equal(new Vector3(7f, 8f, 9f), fromObject.Camera!.Position);
    }

    [Fact]
    public void FromJson_OfMalformedInput_Throws() =>
        Assert.Throws<JsonException>(() => SceneSerializer.FromJson("{ not json"));

    [Fact]
    public void SaveAndLoad_RoundTripThroughAFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"softengine-{Guid.NewGuid():N}.scene.json");

        try
        {
            var scene = NewScene();
            scene.AmbientIntensity = 0.11f;

            SceneSerializer.Save(path, SceneSerializer.Capture(scene));

            var target = NewScene();
            SceneSerializer.Apply(SceneSerializer.Load(path), target);

            Assert.Equal(0.11f, target.AmbientIntensity, 5);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>The front-end's half of the document is data the engine stores and never reads.</summary>
    [Fact]
    public void WorldSource_RoundTripsUntouched()
    {
        var document = SceneSerializer.Capture(NewScene());
        document.World = new WorldSource { Demo = "cascades" };

        var read = SceneSerializer.FromJson(SceneSerializer.ToJson(document));

        Assert.Equal("cascades", read.World?.Demo);
        Assert.Null(read.World?.File);
    }
}
