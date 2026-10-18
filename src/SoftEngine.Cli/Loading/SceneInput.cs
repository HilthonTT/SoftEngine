using SoftEngine.Cli.Options;
using SoftEngine.Core.Scenes.Serialization;

namespace SoftEngine.Cli.Loading;

internal static class SceneInput
{
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
