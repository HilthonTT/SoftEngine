using SoftEngine.Core.Scenes;
using System.Numerics;

namespace SoftEngine.Cli.Loading;

internal sealed record LoadedWorld(SimpleWorld World, Vector3 Center, float Radius, int SkippedTextures);
