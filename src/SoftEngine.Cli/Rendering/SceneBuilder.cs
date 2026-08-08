using SoftEngine.Cli.Loading;
using SoftEngine.Cli.Options;
using SoftEngine.Core.Buffers;
using SoftEngine.Core.Pipeline;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Lights;
using SoftEngine.Core.Scenes.Projections;
using SoftEngine.Core.Textures;
using System.Numerics;

namespace SoftEngine.Cli.Rendering;

/// <summary>
/// Frames the model and builds the <see cref="Scene"/> that gets rendered.
///
/// The framing is the part with no second attempt at it: a person in the viewer can orbit and
/// dolly until the model looks right, and a command line writes the frame it was asked for.
/// </summary>
internal static class SceneBuilder
{
    /// <summary>The vertical field of view every framed render uses.</summary>
    public const float FieldOfView = 40f * MathF.PI / 180f;

    /// <summary>The scene, the camera in it, and the distance the camera was framed at.</summary>
    /// <param name="Scene">Ready to render.</param>
    /// <param name="Camera">The same camera the scene holds, typed so a turntable can re-orbit it.</param>
    /// <param name="Distance">What framing solved for, which a turntable must keep constant.</param>
    internal sealed record Framed(Scene Scene, OrbitCamera Camera, float Distance);

    public static Framed Build(RenderOptions options, LoadedWorld loaded, IRenderer renderer, int factor)
    {
        var camera = new OrbitCamera { Target = loaded.Center };

        // The distance at which a sphere of that radius exactly fills the frame's vertical extent is
        // r / sin(fov/2) — solved rather than guessed at with a multiplier, because the multiplier
        // that frames one model crops the next. The margin is the air around it.
        const float margin = 1.08f;

        var distance = MathF.Max(loaded.Radius / MathF.Sin(FieldOfView * 0.5f) * margin * options.Zoom, 1e-3f);

        camera.Orbit(options.Yaw, options.Pitch, distance);

        if (options.Camera is { } position)
        {
            camera.Position = position;
        }

        var scene = new Scene
        {
            Surface = new FrameBuffer(options.Width * factor, options.Height * factor) { Stats = renderer.Stats },
            Camera = camera,
            World = loaded.World,

            // The far plane contains the model from wherever the camera ended up, with headroom: a
            // far plane closer than the geometry slices the model visibly, and nothing about a
            // one-shot render gives the user a chance to notice and fix it.
            Projection = new PerspectiveProjection(
                FieldOfView,
                MathF.Max(loaded.Radius * 0.001f, 1e-4f),
                MathF.Max((camera.Position - loaded.Center).Length() + loaded.Radius * 4f, 100f)),

            GammaCorrect = true,
            HighDynamicRange = true,
        };

        scene.Shadows.Enabled = options.Shadows;
        scene.Shadows.CascadeCount = options.Cascades;
        scene.Shadows.Resolution = options.Width > 1280 ? 2048 : 1024;

        ApplyEnvironment(scene, options, loaded);

        return new Framed(scene, camera, distance);
    }

    private static void ApplyEnvironment(Scene scene, RenderOptions options, LoadedWorld loaded)
    {
        if (options.EnvironmentPath is { } environmentPath)
        {
            try
            {
                scene.Environment = EnvironmentLoader.Load(environmentPath, options.EnvironmentSize);
            }
            catch (Exception error) when (error is IOException or InvalidDataException)
            {
                // A panorama that will not decode is worth saying out loud and worth continuing past:
                // the frame is still renderable, it is just lit by nothing but its lights.
                Console.Error.WriteLine($"softengine: could not read '{environmentPath}': {error.Message}");
            }

            return;
        }

        if (!options.Sky)
        {
            return;
        }

        // The sun goes where the world's key light points. A sky whose sun is somewhere other
        // than where the shadows come from is the one thing that reads as obviously wrong.
        var sun = loaded.World.Lights.OfType<DirectionalLight>().FirstOrDefault()?.Direction
            ?? new Vector3(-0.35f, -0.6f, -1f);

        scene.Environment = options.HighDynamicRangeSky
            ? SkyBox.HighDynamicRangeGradient(sun)
            : SkyBox.Gradient(sun);
    }
}
