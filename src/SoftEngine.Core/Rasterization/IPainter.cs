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

    void DrawTriangle(FrameBuffer surface, ColorRGB color, VertexBuffer vertexBuffer, int triangleIndice);
}
