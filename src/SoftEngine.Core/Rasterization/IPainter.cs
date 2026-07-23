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
    /// Whether <see cref="DrawTriangle"/> honors the row slice it is given. Painters that
    /// ignore the slice (line drawing crosses arbitrary rows) must return false so the
    /// renderer keeps them on the sequential path instead of racing the z-buffer.
    /// </summary>
    bool SupportsRowSlices => true;

    /// <summary>
    /// Draws one triangle, restricted to the rows owned by <paramref name="slice"/>.
    /// The renderer calls this concurrently with disjoint slices; implementations must
    /// not mutate shared state here.
    /// </summary>
    void DrawTriangle(FrameBuffer surface, ColorRGB color, VertexBuffer vertexBuffer, int triangleIndice, in RowSlice slice);
}
