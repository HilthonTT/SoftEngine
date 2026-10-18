namespace SoftEngine.WinForms.Worlds;

internal static class WorldScale
{
    public static (float Start, float End) FogRange(float reference) => (reference * 0.5f, reference * 4f);

    public static int ShadowResolution(int viewportWidth) => viewportWidth > 1280 ? 2048 : 1024;

    public static (float Radius, float Bias) AmbientOcclusion(float reference)
    {
        var radius = reference > 0f ? reference * 0.02f : 0.5f;

        return (radius, radius * 0.04f);
    }

    public static (float MaxDistance, float Thickness) Reflections(float reference)
    {
        var maxDistance = reference > 0f ? reference : 40f;

        return (maxDistance, maxDistance * 0.04f);
    }

    public static float? SnapStep(float reference)
    {
        if (reference <= 0f)
        {
            return null;
        }

        // A round number near a fiftieth of the framing distance, so the step is always
        // something a person would have typed: 0.1, 1, 10, 100.
        var rough = reference * 0.02f;
        var magnitude = MathF.Pow(10f, MathF.Round(MathF.Log10(rough)));

        return MathF.Max(magnitude, 0.001f);
    }
}
