using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Pipeline.PostProcess;
using SoftEngine.Core.Rasterization;
using SoftEngine.Core.Scenes;

namespace SoftEngine.Core.Pipeline;

public interface IRenderer
{
    RendererSettings Settings { get; set; }

    /// <summary>Full-screen effects applied to the finished render target; null skips the pass.</summary>
    PostProcessStack? PostProcess { get; set; }

    RenderStats Stats { get; }

    /// <summary>Graphics event list and pixel probe for the frame just rendered.</summary>
    RenderDiagnostics Diagnostics { get; }

    void Render(Scene scene, IPainter? painter);
}
