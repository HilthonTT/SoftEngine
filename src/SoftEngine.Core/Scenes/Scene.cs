using SoftEngine.Core.Buffers;
using SoftEngine.Core.Scenes.Cameras;
using SoftEngine.Core.Scenes.Projections;

namespace SoftEngine.Core.Scenes;

public sealed class Scene
{
    public ICamera Camera { get; set; } = default!;

    public IWorld World { get; set; } = default!;

    public IProjection Projection { get; set; } = default!;

    public FrameBuffer Surface { get; set; } = default!;
}
