using System.Numerics;

namespace SoftEngine.Core.Diagnostics;

/// <summary>A snapshot of one vertex of the triangle that produced a pixel write.</summary>
public readonly record struct ProbeVertex(Vector3 Model, Vector3 World, Vector3 View, Vector4 Projection, Vector3 Normal);
