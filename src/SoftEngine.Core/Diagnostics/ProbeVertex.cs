using System.Numerics;

namespace SoftEngine.Core.Diagnostics;

public readonly record struct ProbeVertex(Vector3 Model, Vector3 World, Vector3 View, Vector4 Projection, Vector3 Normal);
