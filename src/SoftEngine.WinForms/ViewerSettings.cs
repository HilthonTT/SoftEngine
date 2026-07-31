using SoftEngine.Gpu;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SoftEngine.WinForms;

/// <summary>
/// The few choices that outlive a session, kept in <c>%APPDATA%\SoftEngine\viewer.json</c>.
///
/// <para>
/// Deliberately not the same thing as a <see cref="Core.Scenes.Serialization.SceneDocument"/>. A
/// scene document describes a <em>scene</em> — where the camera is, what the lights do — and is
/// written when somebody asks for it, to a path they choose. This describes the <em>application</em>:
/// which of its switches were left where, saved without being asked and reloaded without being
/// mentioned. Mixing the two would mean a scene file carrying somebody's window preferences into
/// everyone else's copy of the viewer.
/// </para>
///
/// <para>
/// <b>Nothing here may throw.</b> A preferences file is not worth failing to start over: a corrupt
/// one, a read-only profile or a roaming folder that is not there all mean "use the defaults", and
/// the alternative is an application that cannot open because of a file nobody knew existed.
/// </para>
/// </summary>
internal sealed class ViewerSettings
{
    private const string FolderName = "SoftEngine";
    private const string FileName = "viewer.json";

    private static readonly JsonSerializerOptions Format = new()
    {
        WriteIndented = true,

        // Names rather than numbers. The file is small enough to read, and somebody who opens it
        // should find "Gpu" instead of a 1 whose meaning lives in an enum's declaration order —
        // which then cannot be reordered without silently changing what old files mean.
        Converters = 
        { 
            new JsonStringEnumConverter() 
        },
    };

    /// <summary>
    /// Which rasterizer the viewport was left on.
    ///
    /// The default is the CPU, and stays the CPU on a machine that has never chosen: the viewer is a
    /// demonstration of a software rasterizer, and opening on the graphics card by default would
    /// quietly show you something else. Opening on it because that is what was picked last time is a
    /// different statement, and it is the one this file makes.
    /// </summary>
    public RenderBackend Backend { get; set; } = RenderBackend.Cpu;

    /// <summary>The settings on disk, or fresh defaults when there are none to be had.</summary>
    public static ViewerSettings Load()
    {
        try
        {
            if (Path() is not { } path || !File.Exists(path))
            {
                return new ViewerSettings();
            }

            return JsonSerializer.Deserialize<ViewerSettings>(File.ReadAllText(path), Format)
                ?? new ViewerSettings();
        }
        catch (Exception exception) when (exception is IOException or JsonException or
                                              UnauthorizedAccessException or NotSupportedException)
        {
            return new ViewerSettings();
        }
    }

    /// <summary>Writes them back, and says nothing at all if it cannot.</summary>
    public void Save()
    {
        try
        {
            if (Path() is not { } path)
            {
                return;
            }

            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, Format));
        }
        catch (Exception exception) when (exception is IOException or JsonException or
                                              UnauthorizedAccessException or NotSupportedException)
        {
        }
    }

    /// <summary>Where the file lives, or null on a system with no roaming profile to put it in.</summary>
    private static string? Path()
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        return roaming is { Length: > 0 }
            ? System.IO.Path.Combine(roaming, FolderName, FileName)
            : null;
    }
}
