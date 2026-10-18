using Microsoft.Win32;
using System.Runtime.Versioning;

namespace SoftEngine.Gpu;

public static class GpuPreferences
{
    private const string PreferenceKeyPath = @"Software\Microsoft\DirectX\UserGpuPreferences";

    private const string PreferenceField = "GpuPreference";

    public static bool IsSupported => OperatingSystem.IsWindows();

    public static bool TakesEffectImmediately => IsSupported && !GpuContext.HasCreatedContext;

    public static GpuPreference Current
    {
        get
        {
            if (!OperatingSystem.IsWindows() || ExecutablePath() is not { } executable)
            {
                return GpuPreference.Automatic;
            }

            return Read(executable);
        }
    }

    [SupportedOSPlatform("windows")]
    private static GpuPreference Read(string executable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PreferenceKeyPath);

            return Parse(key?.GetValue(executable) as string);
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return GpuPreference.Automatic;
        }
    }

    public static bool TryApply(GpuPreference preference, out string? error)
    {
        error = null;

        if (!OperatingSystem.IsWindows())
        {
            if (preference == GpuPreference.Automatic)
            {
                return true;
            }

            error = "Choosing between graphics adapters is a Windows setting; this platform has no equivalent.";
            return false;
        }

        if (ExecutablePath() is not { } executable)
        {
            error = "The running program has no file path, so no per-application preference can be recorded for it.";
            return false;
        }

        return Write(executable, preference, out error);
    }

    [SupportedOSPlatform("windows")]
    private static bool Write(string executable, GpuPreference preference, out string? error)
    {
        error = null;

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(PreferenceKeyPath);

            if (key is null)
            {
                error = "The graphics preference could not be opened for writing.";
                return false;
            }

            if (preference == GpuPreference.Automatic)
            {
                key.DeleteValue(executable, throwOnMissingValue: false);
            }
            else
            {
                key.SetValue(executable, $"{PreferenceField}={Field(preference)};", RegistryValueKind.String);
            }

            return true;
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            error = $"The graphics preference could not be saved: {exception.Message}";
            return false;
        }
    }

    public static bool TryParse(string? name, out GpuPreference preference)
    {
        switch (name?.Trim().ToLowerInvariant())
        {
            case "auto" or "automatic" or "default" or null or "":
                preference = GpuPreference.Automatic;
                return true;

            case "high" or "high-performance" or "performance" or "discrete" or "dedicated":
                preference = GpuPreference.HighPerformance;
                return true;

            case "low" or "power-saving" or "power" or "efficient" or "integrated":
                preference = GpuPreference.PowerSaving;
                return true;

            default:
                preference = GpuPreference.Automatic;
                return false;
        }
    }

    public static string Name(GpuPreference preference) => preference switch
    {
        GpuPreference.HighPerformance => "high",
        GpuPreference.PowerSaving => "low",
        _ => "auto",
    };

    public static string Describe(GpuPreference preference)
    {
        var label = preference switch
        {
            GpuPreference.HighPerformance => "High performance",
            GpuPreference.PowerSaving => "Power saving",
            _ => "Automatic",
        };

        return GpuDevices.For(preference) is { } device ? $"{label} — {device.Name}" : label;
    }

    private static string? ExecutablePath()
    {
        var path = Environment.ProcessPath;

        return string.IsNullOrEmpty(path) ? null : path;
    }

    private static int Field(GpuPreference preference) => preference switch
    {
        GpuPreference.HighPerformance => 2,
        GpuPreference.PowerSaving => 1,
        _ => 0,
    };

    private static GpuPreference Parse(string? stored)
    {
        if (string.IsNullOrEmpty(stored))
        {
            return GpuPreference.Automatic;
        }

        foreach (var part in stored.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = part.IndexOf('=', StringComparison.Ordinal);

            if (separator < 0 || !part.AsSpan(0, separator).Trim().Equals(PreferenceField, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return part.AsSpan(separator + 1).Trim() switch
            {
                "2" => GpuPreference.HighPerformance,
                "1" => GpuPreference.PowerSaving,
                _ => GpuPreference.Automatic,
            };
        }

        return GpuPreference.Automatic;
    }
}
