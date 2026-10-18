namespace SoftEngine.Core.Gizmos;

public sealed class GizmoSnap
{
    public bool Enabled { get; set; }

    public float TranslateStep { get; set; } = 1f;

    public float RotateStep { get; set; } = 15f * MathF.PI / 180f;

    public float ScaleStep { get; set; } = 0.1f;

    public float Round(float value, float step) =>
        Enabled && step > 0f && float.IsFinite(step) ? MathF.Round(value / step) * step : value;
}
