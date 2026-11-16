using System.Globalization;

namespace SoftEngine.WinForms;

/// <summary>
/// What the viewer was asked to do from its command line:
/// <c>SoftEngine.WinForms [file] [--workspace name] [--select n] [--time s] [--viewport WxH] [--screenshot out.png]</c>.
/// </summary>
/// <remarks>
/// A file on its own is what Explorer's "Open with" passes, and opens like a dropped file —
/// model, scene or panorama by extension. The rest exist so a window can be put into a known
/// state from a script: the documentation's screenshots are taken that way.
/// </remarks>
internal sealed record ViewerArguments
{
    public static readonly string[] WorkspaceNames = ["Layout", "Shading", "Rendering", "Debugging"];

    /// <summary>A model, scene document or panorama to open instead of the default world.</summary>
    public string? Path { get; init; }

    /// <summary>The workspace to open on, by its tab's name.</summary>
    public string? Workspace { get; init; }

    /// <summary>The index, in the world's mesh list, of a mesh to select once it is loaded.</summary>
    public int? Select { get; init; }

    /// <summary>Size the window so the viewport is exactly this many pixels.</summary>
    public Size? Viewport { get; init; }

    /// <summary>Pose the loaded world's animation at this many seconds and hold it there.</summary>
    public float? Time { get; init; }

    /// <summary>Render the loaded scene, save the viewport here as a PNG, and quit.</summary>
    public string? Screenshot { get; init; }

    public IReadOnlyList<string> Errors { get; init; } = [];

    public static ViewerArguments Parse(IReadOnlyList<string> args)
    {
        string? path = null;
        string? workspace = null;
        int? select = null;
        Size? viewport = null;
        string? screenshot = null;
        float? time = null;
        var errors = new List<string>();

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];

            string? Value()
            {
                if (i + 1 < args.Count)
                {
                    return args[++i];
                }

                errors.Add($"{arg} needs a value.");
                return null;
            }

            switch (arg.ToLowerInvariant())
            {
                case "--workspace":
                    if (Value() is { } name)
                    {
                        workspace = Array.Find(WorkspaceNames, known => string.Equals(known, name, StringComparison.OrdinalIgnoreCase));

                        if (workspace is null)
                        {
                            errors.Add($"There is no \"{name}\" workspace — choose {string.Join(", ", WorkspaceNames)}.");
                        }
                    }

                    break;

                case "--select":
                    if (Value() is { } index)
                    {
                        if (int.TryParse(index, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= 0)
                        {
                            select = n;
                        }
                        else
                        {
                            errors.Add($"--select takes a mesh index of 0 or more, not \"{index}\".");
                        }
                    }

                    break;

                case "--viewport":
                    if (Value() is { } size)
                    {
                        viewport = ParseSize(size);

                        if (viewport is null)
                        {
                            errors.Add($"--viewport takes a size such as 1280x720, not \"{size}\".");
                        }
                    }

                    break;

                case "--time":
                    if (Value() is { } seconds)
                    {
                        if (float.TryParse(seconds, NumberStyles.Float, CultureInfo.InvariantCulture, out var t) && float.IsFinite(t) && t >= 0f)
                        {
                            time = t;
                        }
                        else
                        {
                            errors.Add($"--time takes a number of seconds, 0 or more, not \"{seconds}\".");
                        }
                    }

                    break;

                case "--screenshot":
                    if (Value() is { } output)
                    {
                        screenshot = output;
                    }

                    break;

                default:
                    if (arg.StartsWith("--", StringComparison.Ordinal))
                    {
                        errors.Add($"Unknown option {arg}.");
                    }
                    else if (path is null)
                    {
                        path = arg;
                    }
                    else
                    {
                        errors.Add($"Only one file can be opened at a time; \"{arg}\" is a second.");
                    }

                    break;
            }
        }

        return new ViewerArguments
        {
            Path = path,
            Workspace = workspace,
            Select = select,
            Viewport = viewport,
            Screenshot = screenshot,
            Time = time,
            Errors = errors,
        };
    }

    private static Size? ParseSize(string text)
    {
        var parts = text.Split('x', 'X', '×');

        if (parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var width)
            && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var height)
            && width is >= 16 and <= 8192
            && height is >= 16 and <= 8192)
        {
            return new Size(width, height);
        }

        return null;
    }
}
