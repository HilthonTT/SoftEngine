using SoftEngine.Core.Scenes;
using System.Numerics;

namespace SoftEngine.Cli.Loading;

/// <summary>What loading a model produced, and how big it turned out to be.</summary>
internal sealed record LoadedWorld(SimpleWorld World, Vector3 Center, float Radius, int SkippedTextures);
