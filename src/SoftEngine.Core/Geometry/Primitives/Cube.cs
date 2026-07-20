using SoftEngine.Core.Diagnostics;
using System.Numerics;

namespace SoftEngine.Core.Geometry.Primitives;

public sealed class Cube : Mesh
{
    private static readonly int[] _indices = 
    [
        0, 1, 2,
        2, 3, 0,
        0, 3, 4,
        4, 5, 0,
        0, 5, 6,
        6, 1, 0,
        1, 6, 7,
        7, 2, 1,
        7, 4, 3,
        3, 2, 7,
        4, 7, 6,
        6, 5, 4
    ];

    private static readonly Vector3[] _vertices = 
    [
        new Vector3(1, 1, 1) - new Vector3(.5f, .5f, .5f),
        new Vector3(0, 1, 1) - new Vector3(.5f, .5f, .5f),
        new Vector3(0, 0, 1) - new Vector3(.5f, .5f, .5f),
        new Vector3(1, 0, 1) - new Vector3(.5f, .5f, .5f),
        new Vector3(1, 0, 0) - new Vector3(.5f, .5f, .5f),
        new Vector3(1, 1, 0) - new Vector3(.5f, .5f, .5f),
        new Vector3(0, 1, 0) - new Vector3(.5f, .5f, .5f),
        new Vector3(0, 0, 0) - new Vector3(.5f, .5f, .5f),
    ];

    private static readonly ColorRGB[] _triangleColors = 
    [
        ColorRGB.Red,
        ColorRGB.Red,
        ColorRGB.Gray,
        ColorRGB.Gray,
        ColorRGB.Yellow,
        ColorRGB.Yellow,
        ColorRGB.Green,
        ColorRGB.Green,
        ColorRGB.Magenta,
        ColorRGB.Magenta,
        ColorRGB.Blue,
        ColorRGB.Blue,
    ];

    public Cube() : base(_vertices, _indices.BuildTriangleIndices(), null, _triangleColors)
    {
    }
}
