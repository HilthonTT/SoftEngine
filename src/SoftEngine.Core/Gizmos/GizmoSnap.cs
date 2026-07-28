namespace SoftEngine.Core.Gizmos;

/// <summary>
/// The increments a gizmo drag is quantized to when snapping is on.
///
/// <para>
/// Every step snaps the mesh's <em>resulting</em> transform rather than the distance the cursor
/// travelled, and the difference is the whole reason to have it. Rounding the travel preserves
/// whatever offset the mesh started at, so two meshes dragged onto "the same" gridline end up a
/// fraction apart — which is precisely the thing a person turns snapping on to prevent. Rounding
/// the result means a step of 1 puts every mesh dragged along X on an integer, whatever it was
/// sitting on beforehand, and 15° means 15° from zero rather than from wherever the drag began.
/// </para>
/// </summary>
public sealed class GizmoSnap
{
    /// <summary>Whether drags are quantized at all. Off leaves the gizmo exactly as it was.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Grid spacing for a move, in world units. It is a world distance and so belongs to the
    /// scene rather than to the tool: the demos here span three orders of magnitude, and a
    /// front-end is expected to scale this to whatever it has loaded.
    /// </summary>
    public float TranslateStep { get; set; } = 1f;

    /// <summary>Angle increment for a turn, in radians. 15° divides the circle into 24, and both 45° and 90° fall on it.</summary>
    public float RotateStep { get; set; } = 15f * MathF.PI / 180f;

    /// <summary>Increment for a stretch, as a multiple of the mesh's own unit scale.</summary>
    public float ScaleStep { get; set; } = 0.1f;

    /// <summary>
    /// Rounds a value to the nearest multiple of a step, or returns it untouched when snapping
    /// is off or the step is meaningless. A non-positive step is treated as "no snapping" rather
    /// than as an error: it arrives from a front-end's text box, where empty and zero are things
    /// a person types on the way to typing something else.
    /// </summary>
    public float Round(float value, float step) =>
        Enabled && step > 0f && float.IsFinite(step) ? MathF.Round(value / step) * step : value;
}
