using SoftEngine.Cli.Options;
using SoftEngine.Core.Scenes.Serialization;

namespace SoftEngine.Cli.Loading;

/// <summary>
/// Works out which model to load and which saved scene, if any, to apply over it.
///
/// <para>
/// A scene document may be the input outright, or applied over a model named on the command line.
/// The first is "render this saved setup"; the second is "render this saved setup against that
/// model", which is what makes a document survive its model being re-exported.
/// </para>
/// </summary>
internal static class SceneInput
{
    /// <summary>What <see cref="Resolve"/> worked out, or the reason it could not.</summary>
    /// <param name="ModelPath">The model to load. Null when <paramref name="Error"/> is set.</param>
    /// <param name="Document">The scene to apply over it, or null for none.</param>
    /// <param name="Error">Why there is nothing to render, ready to print.</param>
    internal sealed record Resolution(string? ModelPath, SceneDocument? Document, string? Error);

    public static Resolution Resolve(RenderOptions options)
    {
        var input = options.Input!;

        var document = IsSceneDocument(input) ? SceneSerializer.Load(input) : null;

        if (options.ScenePath is { } scenePath)
        {
            document = SceneSerializer.Load(scenePath);
        }

        var modelPath = document is not null && !IsSceneDocument(input)
            ? input
            : document?.World?.File ?? (document is null ? input : null);

        if (modelPath is null)
        {
            return new Resolution(null, document,
                "the scene names no model file to render — it was saved from one of the " +
                "viewer's built-in worlds, which this program cannot build. Pass a model as well.");
        }

        if (!File.Exists(modelPath))
        {
            return new Resolution(null, document, $"the scene's model is not there: {modelPath}");
        }

        return new Resolution(modelPath, document, null);
    }

    private static bool IsSceneDocument(string path) =>
        Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase);
}
