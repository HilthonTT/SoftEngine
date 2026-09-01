namespace SoftEngine.Core.Diagnostics;

public sealed class PixelWrite
{
    public required int EventIndex { get; init; }

    public required PixelWriteSource Source { get; init; }

    public required int ObjectId { get; init; }

    public required int TriangleIndex { get; init; }

    public required int Color { get; init; }

    public required int PreviousColor { get; init; }

    public required int Depth { get; init; }

    public required int PreviousDepth { get; init; }

    public required bool Passed { get; init; }

    public ProbeVertex[]? Vertices { get; init; }

    public static float Normalize(int depth) => depth / (float)int.MaxValue;
}
