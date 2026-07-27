using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Lights;

namespace SoftEngine.Core.Rasterization;

public interface IPainter
{
    /// <summary>
    /// Called once per frame before any triangles are drawn, so a painter can pick up
    /// per-frame state (camera position, scene lights, …).
    /// </summary>
    void Prepare(Scene scene)
    {
    }

    /// <summary>
    /// The light this painter shades by when the world declares none of its own, or null
    /// when it has no opinion.
    ///
    /// The renderer asks before the shadow pass, because that pass runs before
    /// <see cref="Prepare"/> and has to pick the same light the shading will: a scene lit
    /// from one direction and shadowed from another is worse than one with no shadows at all,
    /// and the mismatch only appears on the worlds that declare no lights — which are exactly
    /// the ones nobody sets up carefully.
    /// </summary>
    ILight? FallbackLight => null;

    /// <summary>
    /// Whether <see cref="DrawTriangle"/> honors the tile it is given. Painters that ignore
    /// it (line drawing crosses arbitrary pixels) must return false so the renderer keeps
    /// them on the sequential path instead of racing the z-buffer.
    /// </summary>
    bool SupportsTiles => true;

    /// <summary>
    /// Draws one triangle, restricted to the pixels owned by <paramref name="tile"/>.
    /// The renderer calls this concurrently with disjoint tiles; implementations must
    /// not mutate shared state here.
    /// </summary>
    void DrawTriangle(FrameBuffer surface, ColorRGB color, VertexBuffer vertexBuffer, int triangleIndice, in ScreenTile tile);
}
