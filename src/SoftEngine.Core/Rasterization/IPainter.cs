using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;

namespace SoftEngine.Core.Rasterization;

public interface IPainter
{
    void DrawTriangle(FrameBuffer surface, ColorRGB color, VertexBuffer vertexBuffer, int triangleIndice);
}
