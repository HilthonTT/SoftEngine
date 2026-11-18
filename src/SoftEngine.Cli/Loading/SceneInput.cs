using SoftEngine.Cli.Options;
using SoftEngine.Core.Scenes.Serialization;

namespace SoftEngine.Cli.Loading;

internal static class SceneInput
{
    internal sealed record Resolution(string? ModelPath, SceneDocument? Document, string? Error)
    {
        /// <summary>The panorama the scene document lights itself with, if it names one.</summary>
        public string? PanoramaPath { get; init; }
    }

    public static Resolution Resolve(RenderOptions options)
    {
        var input = options.Input!;

        var documentPath = options.ScenePath ?? (IsSceneDocument(input) ? input : null);
        var document = documentPath is null ? null : SceneSerializer.Load(documentPath);

        var modelPath = document is not null && !IsSceneDocument(input)
            ? input
            : RelativeTo(documentPath, document?.World?.File) ?? (document is null ? input : null);

        var panoramaPath = RelativeTo(documentPath, document?.Environment?.Panorama);

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

        return new Resolution(modelPath, document, null) { PanoramaPath = panoramaPath };
    }

    /// <summary>
    /// A path a scene document names, read from the document's own folder when it is relative —
    /// the format is written by hand, and "model.glb" beside the scene means that file, not one
    /// wherever the command happens to be run from.
    /// </summary>
    internal static string? RelativeTo(string? documentPath, string? path)
    {
        if (path is not { Length: > 0 } || documentPath is null || Path.IsPathRooted(path))
        {
            return path is { Length: > 0 } ? path : null;
        }

        var folder = Path.GetDirectoryName(Path.GetFullPath(documentPath));

        return folder is null ? path : Path.GetFullPath(Path.Combine(folder, path));
    }

    private static bool IsSceneDocument(string path) =>
        Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase);
}
