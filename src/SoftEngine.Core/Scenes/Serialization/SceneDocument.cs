using System.Numerics;

namespace SoftEngine.Core.Scenes.Serialization;

public sealed class SceneDocument
{
    public int Version { get; set; } = CurrentVersion;

    public const int CurrentVersion = 1;

    public WorldSource? World { get; set; }

    public CameraState? Camera { get; set; }

    public ProjectionState? Projection { get; set; }

    public List<MeshState>? Meshes { get; set; }

    public List<LightState>? Lights { get; set; }

    public EnvironmentState? Environment { get; set; }

    public FogState? Fog { get; set; }

    public ShadowState? Shadows { get; set; }

    public RenderState? Rendering { get; set; }

    public PostState? Post { get; set; }
}

public sealed class WorldSource
{
    public string? Demo { get; set; }

    public string? File { get; set; }
}

public sealed class CameraState
{
    public Vector3 Position { get; set; }

    public Quaternion? Orientation { get; set; }

    public float? ReferenceDistance { get; set; }
}

public sealed class ProjectionState
{
    public string Kind { get; set; } = "perspective";

    public float FieldOfView { get; set; }

    public float ViewHeight { get; set; }

    public float Near { get; set; } = 0.01f;

    public float Far { get; set; } = 500f;
}

public sealed class MeshState
{
    public int Index { get; set; }

    public Vector3 Position { get; set; }

    public Vector3 Rotation { get; set; }

    public Vector3 Scale { get; set; } = Vector3.One;

    public bool Visible { get; set; } = true;

    public float Opacity { get; set; } = 1f;
}

public sealed class LightState
{
    public string Kind { get; set; } = "point";

    public Vector3 Position { get; set; }

    public Vector3 Direction { get; set; } = -Vector3.UnitY;

    public float Intensity { get; set; } = 1f;

    public int[] Color { get; set; } = [255, 255, 255];

    public float? Range { get; set; }

    public float InnerAngle { get; set; } = MathF.PI / 9f;

    public float OuterAngle { get; set; } = MathF.PI / 6f;
}

public sealed class EnvironmentState
{
    public bool ShowSky { get; set; } = true;

    public string? Panorama { get; set; }

    public float SkyIntensity { get; set; } = 1f;

    public bool AmbientFromEnvironment { get; set; } = true;

    public float AmbientIntensity { get; set; } = 0.35f;
}

public sealed class FogState
{
    public bool Enabled { get; set; }

    public string Mode { get; set; } = "linear";

    public int[] Color { get; set; } = [0, 0, 0];

    public float Start { get; set; } = 10f;

    public float End { get; set; } = 100f;

    public float Density { get; set; } = 0.02f;
}

public sealed class ShadowState
{
    public bool Enabled { get; set; }

    public int Resolution { get; set; } = 1024;

    public float DepthBias { get; set; } = 1.5f;

    public float SlopeBias { get; set; } = 2.5f;

    public bool SoftFilter { get; set; } = true;

    public int CascadeCount { get; set; } = 1;

    public float SplitBlend { get; set; } = 0.8f;

    public float MaxDistance { get; set; }

    public float Strength { get; set; } = 1f;
}

public sealed class RenderState
{
    public string Painter { get; set; } = "Gouraud";

    public bool GammaCorrect { get; set; } = true;

    public bool HighDynamicRange { get; set; } = true;

    public bool BackFaceCulling { get; set; }

    public bool ShowTriangles { get; set; }

    public bool ShowXZGrid { get; set; }

    public bool ShowAxes { get; set; }

    public bool ShowSkeleton { get; set; }

    public bool HierarchicalZ { get; set; } = true;

    public bool OcclusionCulling { get; set; } = true;

    public bool TemporalAntiAliasing { get; set; }

    public bool MotionBlur { get; set; }

    public bool OrderIndependentTransparency { get; set; }

    public string DebugView { get; set; } = "Off";

    public int SuperSampling { get; set; } = 1;

    public bool TextureFiltering { get; set; } = true;

    public bool TrilinearFiltering { get; set; }

    public bool Animate { get; set; } = true;
}

public sealed class PostState
{
    public SsrState? Ssr { get; set; }

    public SsaoState? Ssao { get; set; }

    public BloomState? Bloom { get; set; }

    public ToneMapState? ToneMap { get; set; }

    public FxaaState? Fxaa { get; set; }

    public VignetteState? Vignette { get; set; }
}

public sealed class SsrState
{
    public bool Enabled { get; set; }

    public float Strength { get; set; } = 1f;

    public int MaxSteps { get; set; } = 64;

    public float MaxDistance { get; set; } = 40f;

    public float Thickness { get; set; } = 1.5f;

    public float MaxRoughness { get; set; } = 0.6f;

    public int BlurRadius { get; set; } = 3;

    public float EdgeFade { get; set; } = 0.15f;
}

public sealed class SsaoState
{
    public bool Enabled { get; set; }

    public float Strength { get; set; } = 0.6f;

    public float Radius { get; set; } = 0.5f;

    public float RangeCutoff { get; set; } = 1f;

    public float Bias { get; set; } = 0.02f;

    public int BlurRadius { get; set; } = 2;
}

public sealed class BloomState
{
    public bool Enabled { get; set; }

    public float Threshold { get; set; } = 0.65f;

    public float Intensity { get; set; } = 0.55f;

    public int Downsample { get; set; } = 4;

    public int Radius { get; set; } = 5;

    public int Passes { get; set; } = 2;
}

public sealed class ToneMapState
{
    public bool Enabled { get; set; }

    public float Exposure { get; set; } = 1.4f;

    public string Operator { get; set; } = "Aces";
}

public sealed class FxaaState
{
    public bool Enabled { get; set; }

    public float EdgeThreshold { get; set; } = 0.125f;

    public float EdgeThresholdMin { get; set; } = 0.0312f;

    public float Strength { get; set; } = 0.75f;
}

public sealed class VignetteState
{
    public bool Enabled { get; set; }

    public float Intensity { get; set; } = 0.45f;

    public float Radius { get; set; } = 0.55f;

    public float Softness { get; set; } = 0.45f;
}
