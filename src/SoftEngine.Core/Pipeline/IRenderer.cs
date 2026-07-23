using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Rasterization;
using SoftEngine.Core.Scenes;

namespace SoftEngine.Core.Pipeline;

public interface IRenderer
{
    RendererSettings Settings { get; set; }

    RenderStats Stats { get; }

    /// <summary>Graphics event list and pixel probe for the frame just rendered.</summary>
    RenderDiagnostics Diagnostics { get; }

    void Render(Scene scene, IPainter? painter);
}
