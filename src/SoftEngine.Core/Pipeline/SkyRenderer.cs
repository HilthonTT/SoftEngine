using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Shading;
using SoftEngine.Core.Textures;
using System.Numerics;

namespace SoftEngine.Core.Pipeline;

public static class SkyRenderer
{
    public static void Render(Scene scene, CubeMap environment, int probeEvent = -1)
    {
        ArgumentNullException.ThrowIfNull(scene, nameof(scene));
        ArgumentNullException.ThrowIfNull(environment, nameof(environment));

        var surface = scene.Surface;
        var projection = scene.Projection;

        if (projection.IsOrthographic || surface.Width <= 0 || surface.Height <= 0)
        {
            return;
        }

        var projectionMatrix = projection.ProjectionMatrix(surface.Width, surface.Height);

        var scaleX = projectionMatrix.M11;
        var scaleY = projectionMatrix.M22;

        if (MathF.Abs(scaleX) < 1e-9f || MathF.Abs(scaleY) < 1e-9f ||
            !Matrix4x4.Invert(scene.Camera.ViewMatrix, out var inverseView))
        {
            return;
        }

        var invScaleX = 1f / scaleX;
        var invScaleY = 1f / scaleY;

        var width = surface.Width;
        var height = surface.Height;

        var toNdcX = 2f / MathF.Max(width - 1, 1);
        var toNdcY = 2f / MathF.Max(height - 1, 1);

        var intensity = MathF.Max(0f, scene.SkyIntensity);

        var probing = surface.IsProbing;

        Parallel.For(0, height, y =>
        {
            if (probing)
            {
                FrameBuffer.SetProbeContext(probeEvent, PixelWriteSource.Sky, SceneObjectIds.RenderTarget, -1, null);
            }

            var ndcY = 1f - y * toNdcY;
            var viewY = ndcY * invScaleY;

            for (var x = 0; x < width; x++)
            {
                if (!surface.IsBackground(x, y))
                {
                    continue;
                }

                var ndcX = x * toNdcX - 1f;

                var viewDirection = new Vector3(ndcX * invScaleX, viewY, -1f);
                var worldDirection = Vector3.TransformNormal(viewDirection, inverseView);

                var color = environment.SampleRadiance(worldDirection);

                surface.PutBackground(x, y, intensity * color);
            }
        });
    }
}
