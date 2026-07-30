using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Shading;
using System.Numerics;

namespace SoftEngine.Core.Pipeline;

/// <summary>
/// Fills the frame's untouched pixels with the environment seen along the ray through each
/// of them.
///
/// There is no geometry involved and no cube to rasterize. A skybox drawn as a cube is
/// really just a way of getting the GPU to interpolate a direction per pixel; here the
/// direction is computed directly from the pixel's position, which is both simpler and
/// exact — no seams where the cube's own triangles meet, and nothing that can be clipped
/// by the near plane.
///
/// It runs after the opaque pass and before the transparent one. After, so it only has to
/// shade pixels no surface covered — which it finds by asking the depth buffer what is
/// still at its cleared value. Before, because transparent geometry is blended without
/// writing depth: run the sky last and it would paint straight over a pane of glass that
/// had nothing but background behind it.
/// </summary>
public static class SkyRenderer
{
    /// <summary>
    /// Draws the scene's environment into every pixel nothing has written yet. Does nothing
    /// without an environment, or under a parallel projection — an orthographic view has no
    /// eye point, so there is no single ray through a pixel to look along.
    /// </summary>
    /// <param name="probeEvent">Index of the event a probed pixel's write is attributed to.</param>
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

        // The projection scales view X and Y into clip space by these; going the other way
        // turns a normalized device coordinate back into a direction in view space.
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

        // Matching FrameBuffer.ToScreen3, which maps NDC ±1 onto pixel 0 and pixel n - 1.
        var toNdcX = 2f / MathF.Max(width - 1, 1);
        var toNdcY = 2f / MathF.Max(height - 1, 1);

        var intensity = MathF.Max(0f, scene.SkyIntensity);

        var probing = surface.IsProbing;

        Parallel.For(0, height, y =>
        {
            // The probe context is thread-static and the rows are spread across workers,
            // so it has to be set on whichever one ends up owning the probed row.
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

                // The view looks down -Z: the ray through a pixel is the point one unit
                // ahead whose projection lands on it.
                var viewDirection = new Vector3(ndcX * invScaleX, viewY, -1f);
                var worldDirection = Vector3.TransformNormal(viewDirection, inverseView);

                var color = environment.SampleRadiance(worldDirection);

                surface.PutBackground(x, y, intensity * color);
            }
        });
    }
}
