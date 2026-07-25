using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Scenes;

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
