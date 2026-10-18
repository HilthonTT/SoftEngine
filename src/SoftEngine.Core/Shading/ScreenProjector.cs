using System.Numerics;

namespace SoftEngine.Core.Shading;

public readonly struct ScreenProjector
{
    private readonly Matrix4x4 _viewProjection;
    private readonly float _widthMinus1By2;
    private readonly float _heightMinus1By2;

    public ScreenProjector(in Matrix4x4 viewProjection, int width, int height)
    {
        _viewProjection = viewProjection;
        _widthMinus1By2 = (width - 1) / 2f;
        _heightMinus1By2 = (height - 1) / 2f;
    }

    public bool IsValid => _widthMinus1By2 > 0f && _heightMinus1By2 > 0f;

    public bool Project(Vector3 world, out float x, out float y)
    {
        var clip = Vector4.Transform(new Vector4(world, 1f), _viewProjection);

        if (clip.W <= 1e-6f)
        {
            x = 0f;
            y = 0f;
            return false;
        }

        x = _widthMinus1By2 * (clip.X / clip.W + 1f);
        y = -_heightMinus1By2 * (clip.Y / clip.W - 1f);

        return true;
    }
}
