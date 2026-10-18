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

internal static class SceneBuilder
{
    public const float FieldOfView = 40f * MathF.PI / 180f;

    internal sealed record Framed(Scene Scene, OrbitCamera Camera, float Distance);

    public static Framed Build(RenderOptions options, LoadedWorld loaded, IRenderer renderer, int factor)
    {
        var camera = new OrbitCamera { Target = loaded.Center };

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
                Console.Error.WriteLine($"softengine: could not read '{environmentPath}': {error.Message}");
            }

            return;
        }

        if (!options.Sky)
        {
            return;
        }

        var sun = loaded.World.Lights.OfType<DirectionalLight>().FirstOrDefault()?.Direction
            ?? new Vector3(-0.35f, -0.6f, -1f);

        scene.Environment = options.HighDynamicRangeSky
            ? SkyBox.HighDynamicRangeGradient(sun)
            : SkyBox.Gradient(sun);
    }
}
