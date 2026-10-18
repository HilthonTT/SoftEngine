using SoftEngine.Core.Shading;

namespace SoftEngine.Core.Scenes;

public sealed class ShadowSettings
{
    private int _resolution = 1024;

    public bool Enabled { get; set; }

    public int Resolution
    {
        get => _resolution;
        set => _resolution = System.Math.Clamp(value, 64, 8192);
    }

    public float DepthBias { get; set; } = 1.5f;

    public float SlopeBias { get; set; } = 2.5f;

    public bool SoftFilter { get; set; } = true;

    public int CascadeCount
    {
        get => _cascadeCount;
        set => _cascadeCount = System.Math.Clamp(value, 1, ShadowMap.MaxCascades);
    }

    private int _cascadeCount = 1;

    public float SplitBlend
    {
        get => _splitBlend;
        set => _splitBlend = System.Math.Clamp(value, 0f, 1f);
    }

    private float _splitBlend = 0.8f;

    public float MaxDistance { get; set; }

    public float Strength { get; set; } = 1f;
}
