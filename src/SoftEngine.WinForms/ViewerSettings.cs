using SoftEngine.Gpu;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SoftEngine.WinForms;

internal sealed class ViewerSettings
{
    private const string FolderName = "SoftEngine";
    private const string FileName = "viewer.json";

    private static readonly JsonSerializerOptions Format = new()
    {
        WriteIndented = true,

        Converters =
        {
            new JsonStringEnumConverter()
        },
    };

    public RenderBackend Backend { get; set; } = RenderBackend.Cpu;

    public GpuPreference GpuPreference { get; set; } = GpuPreference.Automatic;

    public WindowPlacement? Window { get; set; }

    public WorkspaceLayout? Workspace { get; set; }

    public List<string> RecentFiles
    {
        get => _recentFiles;
        set => _recentFiles = value ?? [];
    }

    private List<string> _recentFiles = [];

    public const int MaxRecentFiles = 10;

    public bool RememberRecentFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var wasFirst = RecentFiles.Count > 0 &&
                       string.Equals(RecentFiles[0], path, StringComparison.OrdinalIgnoreCase);

        if (wasFirst)
        {
            return false;
        }

        RecentFiles.RemoveAll(existing => string.Equals(existing, path, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, path);

        if (RecentFiles.Count > MaxRecentFiles)
        {
            RecentFiles.RemoveRange(MaxRecentFiles, RecentFiles.Count - MaxRecentFiles);
        }

        return true;
    }

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

    private static string? Path()
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        return roaming is { Length: > 0 }
            ? System.IO.Path.Combine(roaming, FolderName, FileName)
            : null;
    }
}
