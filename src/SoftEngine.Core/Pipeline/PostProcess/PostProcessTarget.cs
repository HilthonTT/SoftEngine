using SoftEngine.Core.Buffers;
using SoftEngine.Core.Shading;
using System.Numerics;

namespace SoftEngine.Core.Pipeline.PostProcess;

public sealed class PostProcessTarget
{
    private float[] _color = [];
    private float[] _scratch = [];
    private float[] _viewDepth = [];
    private uint[] _reflectance = [];

    public int Width { get; private set; }

    public int Height { get; private set; }

    public float[] Color => _color;

    public float[] Scratch => _scratch;

    public int Length => Width * Height * 3;

    public float[] ViewDepth => _viewDepth;

    public bool HasDepth { get; private set; }

    public uint[] Reflectance => _reflectance;

    public bool HasReflectance { get; private set; }

    public SurfaceReflectance ReflectanceAt(int x, int y) =>
        HasReflectance && (uint)x < (uint)Width && (uint)y < (uint)Height
            ? SurfaceReflectance.FromPacked(_reflectance[x + y * Width])
            : SurfaceReflectance.None;

    public float ProjectionScaleX { get; private set; } = 1f;

    public float ProjectionScaleY { get; private set; } = 1f;

    public DepthField Field => new(_viewDepth, Width, Height, ProjectionScaleX, ProjectionScaleY);

    public Vector3 ViewPositionAt(int x, int y) => Field.PositionAt(x, y);

    public bool ProjectToScreen(Vector3 viewPosition, out int x, out int y, out float distance) =>
        Field.ProjectToScreen(viewPosition, out x, out y, out distance);

    internal void Resize(int width, int height)
    {
        Width = width;
        Height = height;
        HasDepth = false;
        HasReflectance = false;

        var length = width * height * 3;
        if (_color.Length >= length)
        {
            return;
        }

        _color = new float[length];
        _scratch = new float[length];
    }

    internal float[] PrepareDepth(float projectionScaleX, float projectionScaleY)
    {
        var count = Width * Height;
        if (_viewDepth.Length < count)
        {
            _viewDepth = new float[count];
        }

        ProjectionScaleX = projectionScaleX;
        ProjectionScaleY = projectionScaleY;
        HasDepth = true;

        return _viewDepth;
    }

    internal uint[] PrepareReflectance()
    {
        var count = Width * Height;
        if (_reflectance.Length < count)
        {
            _reflectance = new uint[count];
        }

        HasReflectance = true;

        return _reflectance;
    }

    public void SnapshotToScratch() => Array.Copy(_color, _scratch, Length);

    public void SwapWithScratch() => (_color, _scratch) = (_scratch, _color);
}
