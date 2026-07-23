namespace SoftEngine.Core.Diagnostics;

/// <summary>
/// One attempt to write the probed pixel — including attempts the depth test rejected,
/// which is exactly what makes a pixel history useful.
/// </summary>
public sealed class PixelWrite
{
    /// <summary>Index into the frame's <see cref="GraphicsEventLog"/>, or -1 when events weren't recorded.</summary>
    public required int EventIndex { get; init; }

    public required PixelWriteSource Source { get; init; }

    /// <summary>The <see cref="SceneObjectIds"/> slot of the object that drew, or -1.</summary>
    public required int ObjectId { get; init; }

    public required int TriangleIndex { get; init; }

    /// <summary>The colour the shader produced, packed ARGB.</summary>
    public required int Color { get; init; }

    /// <summary>The colour already in the render target, packed ARGB.</summary>
    public required int PreviousColor { get; init; }

    /// <summary>Incoming depth, in raw buffer units (see <see cref="Normalize"/>).</summary>
    public required int Depth { get; init; }

    /// <summary>The depth already in the z-buffer.</summary>
    public required int PreviousDepth { get; init; }

    /// <summary>False when the depth test rejected this write.</summary>
    public required bool Passed { get; init; }

    /// <summary>The three vertices of the triangle, or null for clears and line gizmos.</summary>
    public ProbeVertex[]? Vertices { get; init; }

    /// <summary>Depth mapped back to the 0 (near plane) … 1 (far plane) range.</summary>
    public static float Normalize(int depth) => depth / (float)int.MaxValue;
}
