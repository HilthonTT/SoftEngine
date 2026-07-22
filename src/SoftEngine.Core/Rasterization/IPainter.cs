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
    /// Draws one triangle, restricted to the rows owned by <paramref name="slice"/>.
    /// The renderer calls this concurrently with disjoint slices; implementations must
    /// not mutate shared state here.
    /// </summary>
    void DrawTriangle(FrameBuffer surface, ColorRGB color, VertexBuffer vertexBuffer, int triangleIndice, in RowSlice slice);
}
