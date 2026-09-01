using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Lights;
using SoftEngine.Core.Textures;

namespace SoftEngine.Core.Rasterization;

public interface IPainter
{
    void Prepare(Scene scene)
    {
    }

    ILight? FallbackLight => null;

    bool SupportsTiles => true;

    TextureFiltering Filtering => TextureFiltering.Bilinear;

    bool UseMipMaps => true;

    float AmbientLevel => 0f;

    void DrawTriangle(FrameBuffer surface, ColorRGB color, VertexBuffer vertexBuffer, int triangleIndice, in ScreenTile tile);
}
