using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry.Primitives;
using SoftEngine.Core.Pipeline;
using SoftEngine.Core.Pipeline.PostProcess;
using SoftEngine.Core.Rasterization.Painters;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Cameras;
using SoftEngine.Core.Scenes.Lights;
using SoftEngine.Core.Scenes.Projections;
using SoftEngine.Core.Shading;
using System.Numerics;

namespace SoftEngine.Core.Tests.Pipeline;

public class AmbientChannelTests
{
    private const int Size = 96;

    private sealed class FixedCamera(Vector3 position) : ICamera
    {
        public Vector3 Position { get; set; } = position;

        public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, Vector3.Zero, Vector3.UnitY);
    }

    private static Scene Corner()
    {
        var world = new SimpleWorld();

        world.Meshes.Add(new Cube { Position = new Vector3(0, -3f, 0), Scale = new Vector3(6f, 0.25f, 6f) });
        world.Meshes.Add(new Cube { Position = new Vector3(0, 0f, 3f), Scale = new Vector3(6f, 6f, 0.25f) });
        world.Lights.Add(new DirectionalLight { Direction = new Vector3(0f, -0.3f, 1f) });

        return new Scene
        {
            World = world,
            Camera = new FixedCamera(new Vector3(0, 0.5f, -12f)),
            Projection = new PerspectiveProjection(MathF.PI / 4f, 0.5f, 100f),
            Surface = new FrameBuffer(Size, Size) { Stats = new RenderStats() },
            GammaCorrect = true,
            HighDynamicRange = true,
        };
    }

    private static Scene Render(bool ssao, float ambient)
    {
        var scene = Corner();

        var renderer = new Renderer
        {
            PostProcess = new PostProcessStack(),
        };

        renderer.PostProcess.Effects.Add(new SsaoEffect
        {
            Enabled = ssao,
            Strength = 0.9f,
            Radius = 1.5f,
            BlurRadius = 2,
        });

        renderer.Render(scene, new PbrPainter(null, ambient));

        return scene;
    }

    private static int Darkened(Scene without, Scene with)
    {
        var count = 0;

        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var plain = ColorRGB.FromPacked(without.Surface.GetColor(x, y));
                var occluded = ColorRGB.FromPacked(with.Surface.GetColor(x, y));

                if (occluded.R < plain.R)
                {
                    count++;
                }
            }
        }

        return count;
    }

    [Fact]
    public void ThePbrPainter_RecordsWhatTheAmbientTermContributed()
    {
        var scene = Render(ssao: true, ambient: 0.5f);

        Assert.True(scene.Surface.IsRecordingAmbient);

        var lit = 0;

        foreach (var channel in scene.Surface.Ambient)
        {
            if (channel > 0f)
            {
                lit++;
            }
        }

        Assert.True(lit > 0, "no ambient was recorded");
    }

    [Fact]
    public void APainterThatCannotSeparateAmbient_RecordsNone()
    {
        var scene = Corner();

        var renderer = new Renderer
        {
            PostProcess = new PostProcessStack(),
        };

        renderer.PostProcess.Effects.Add(new SsaoEffect { Enabled = true });

        renderer.Render(scene, new GouraudPainter());

        Assert.False(scene.Surface.IsRecordingAmbient);
    }

    [Fact]
    public void Ssao_DarkensTheCornerWhenThereIsAmbientToTakeAway()
    {
        var without = Render(ssao: false, ambient: 0.5f);
        var with = Render(ssao: true, ambient: 0.5f);

        Assert.True(Darkened(without, with) > Size * Size / 100, "SSAO darkened almost nothing");
    }

    [Fact]
    public void Ssao_LeavesDirectlyLitPixelsAloneWhenThereIsNoAmbient()
    {
        var without = Render(ssao: false, ambient: 0f);
        var with = Render(ssao: true, ambient: 0f);

        Assert.Equal(0, Darkened(without, with));
    }
}
