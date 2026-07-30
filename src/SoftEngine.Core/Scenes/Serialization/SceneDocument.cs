using System.Numerics;

namespace SoftEngine.Core.Scenes.Serialization;

/// <summary>
/// A scene as a file: everything about the picture that is <em>not</em> the geometry.
///
/// <para>
/// The one thing this deliberately does not contain is vertices. A scene document names the
/// model it was built from and records what was done to it — where the camera stands, where each
/// mesh was dragged to, which lights are in it, how it is shaded and post-processed. Inlining the
/// geometry would turn a file a person can read and edit into a several-megabyte copy of a model
/// that already exists on disk, and would go stale the moment that model was re-exported.
/// </para>
///
/// <para>
/// Every section is nullable, and every reader treats a missing one as "leave this alone". That
/// is what makes the format writable by hand: a file containing nothing but a camera position is
/// a valid scene document, and applying it moves the camera and changes nothing else.
/// </para>
/// </summary>
public sealed class SceneDocument
{
    /// <summary>
    /// The format's version. Present so a future change has something to branch on rather than
    /// having to guess from which fields are missing — which is indistinguishable from a
    /// hand-written file that simply left them out.
    /// </summary>
    public int Version { get; set; } = CurrentVersion;

    public const int CurrentVersion = 1;

    /// <summary>Where the geometry comes from. Resolved by the application, not by the engine.</summary>
    public WorldSource? World { get; set; }

    public CameraState? Camera { get; set; }

    public ProjectionState? Projection { get; set; }

    /// <summary>Per-mesh overrides, addressed by position in the loaded world's mesh list.</summary>
    public List<MeshState>? Meshes { get; set; }

    public List<LightState>? Lights { get; set; }

    public EnvironmentState? Environment { get; set; }

    public FogState? Fog { get; set; }

    public ShadowState? Shadows { get; set; }

    public RenderState? Rendering { get; set; }

    public PostState? Post { get; set; }
}

/// <summary>
/// What was loaded before anything was done to it: one of the application's bundled worlds, or a
/// model file.
/// </summary>
/// <remarks>
/// The engine stores this and never interprets it. "Demo" is a name only the front-end knows the
/// meaning of, and resolving a path is a question about the machine the file is opened on — both
/// of which belong above a rendering library rather than inside one.
/// </remarks>
public sealed class WorldSource
{
    /// <summary>Identifier of a built-in world, when the scene was built on one.</summary>
    public string? Demo { get; set; }

    /// <summary>Path to a model file, when the scene was built on one.</summary>
    public string? File { get; set; }
}

public sealed class CameraState
{
    public Vector3 Position { get; set; }

    /// <summary>
    /// The camera's orientation, when it has one to give. An <c>ICamera</c> is only required to
    /// produce a view matrix, and an orbit camera's is a function of a rotation it holds — so
    /// this is written when the application's camera can supply it and ignored when it cannot.
    /// </summary>
    public Quaternion? Orientation { get; set; }

    /// <summary>
    /// The distance the world was originally framed from, which is what the viewer's zoom
    /// readout calls 100%. Saved because it is not recoverable from the camera afterwards: the
    /// camera has since been moved, and that is the whole point.
    /// </summary>
    public float? ReferenceDistance { get; set; }
}

public sealed class ProjectionState
{
    /// <summary>"perspective" or "orthographic".</summary>
    public string Kind { get; set; } = "perspective";

    /// <summary>Vertical field of view in radians, for a perspective projection.</summary>
    public float FieldOfView { get; set; }

    /// <summary>Vertical extent of the view box in world units, for a parallel one.</summary>
    public float ViewHeight { get; set; }

    public float Near { get; set; } = 0.01f;

    public float Far { get; set; } = 500f;
}

/// <summary>
/// One mesh's transform, addressed by its index in the world's mesh list.
/// </summary>
/// <remarks>
/// An index rather than a name, because <c>IMesh</c> has no name — and an index into a list that
/// an importer rebuilds deterministically from the same file is stable in exactly the cases this
/// format claims to handle. A document applied to a world with fewer meshes than it expects skips
/// the entries that fall off the end rather than throwing: the model was re-exported, which is a
/// scene that has partly gone stale, not a corrupt file.
/// </remarks>
public sealed class MeshState
{
    public int Index { get; set; }

    public Vector3 Position { get; set; }

    /// <summary>Euler angles in radians, as <c>Mesh.Rotation</c> stores them: X pitch, Y yaw, Z roll.</summary>
    public Vector3 Rotation { get; set; }

    public Vector3 Scale { get; set; } = Vector3.One;

    public bool Visible { get; set; } = true;

    public float Opacity { get; set; } = 1f;
}

public sealed class LightState
{
    /// <summary>"directional", "point" or "spot".</summary>
    public string Kind { get; set; } = "point";

    public Vector3 Position { get; set; }

    public Vector3 Direction { get; set; } = -Vector3.UnitY;

    public float Intensity { get; set; } = 1f;

    /// <summary>The light's colour as three bytes, 0–255.</summary>
    public int[] Color { get; set; } = [255, 255, 255];

    /// <summary>
    /// Distance at which the light reaches nothing, or null for no falloff at all.
    ///
    /// Null rather than a large number, because the engine's own default really is infinity —
    /// and JSON has no way to write that. Round-tripping it as, say, 1e38 would turn a light with
    /// no falloff into a light with an enormous one, which is a different thing that happens to
    /// look the same in the scenes it was tested on.
    /// </summary>
    public float? Range { get; set; }

    /// <summary>Half-angle of a spot's full-strength core, in radians.</summary>
    public float InnerAngle { get; set; } = MathF.PI / 9f;

    /// <summary>Half-angle at which a spot has fallen to nothing, in radians.</summary>
    public float OuterAngle { get; set; } = MathF.PI / 6f;
}

public sealed class EnvironmentState
{
    /// <summary>Whether the environment is drawn behind the scene.</summary>
    public bool ShowSky { get; set; } = true;

    /// <summary>
    /// Path to a panorama that surrounds and lights the scene, when one was loaded instead of the
    /// procedural sky. Resolved by the application, not by the engine — an asset on disk, for the
    /// same reason <see cref="WorldSource.File"/> is.
    /// </summary>
    public string? Panorama { get; set; }

    public float SkyIntensity { get; set; } = 1f;

    public bool AmbientFromEnvironment { get; set; } = true;

    public float AmbientIntensity { get; set; } = 0.35f;
}

public sealed class FogState
{
    public bool Enabled { get; set; }

    /// <summary>"linear" or "exponential".</summary>
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

/// <summary>
/// How the frame is drawn: the painter, the toggles, and the target it is rasterized into.
/// </summary>
public sealed class RenderState
{
    /// <summary>
    /// The painter's name — "None", "Classic", "Flat", "Gouraud", "Phong", "Textured",
    /// "Material" or "Pbr". A name rather than a type, so the file stays readable and a
    /// front-end stays free to construct its own configured instance of one.
    /// </summary>
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

    /// <summary>
    /// Whether the frame is jittered and averaged with the previous ones. Off by default, since it
    /// only means anything to a front-end that renders repeatedly — a one-shot render has no
    /// previous frames to average.
    /// </summary>
    public bool TemporalAntiAliasing { get; set; }

    public bool MotionBlur { get; set; }

    /// <summary>The buffer view presented instead of the shaded image, by enum name.</summary>
    public string DebugView { get; set; } = "Off";

    /// <summary>Supersampling factor: 1 renders at display resolution, 2 renders at twice it.</summary>
    public int SuperSampling { get; set; } = 1;

    public bool TextureFiltering { get; set; } = true;

    public bool Animate { get; set; } = true;
}

public sealed class PostState
{
    public SsaoState? Ssao { get; set; }

    public BloomState? Bloom { get; set; }

    public ToneMapState? ToneMap { get; set; }

    public FxaaState? Fxaa { get; set; }

    public VignetteState? Vignette { get; set; }
}

public sealed class SsaoState
{
    public bool Enabled { get; set; }

    public float Strength { get; set; } = 0.6f;

    /// <summary>A world-space distance, and so the one post-process number that must be scaled to the scene.</summary>
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

    /// <summary>"Reinhard" or "Aces", by enum name.</summary>
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
