using Microsoft.Win32;
using System.Runtime.Versioning;

namespace SoftEngine.Gpu;

/// <summary>
/// Reads and writes the adapter preference the graphics driver will honour.
///
/// <para>
/// There is no OpenGL call for "give me the other GPU". The extensions that come closest —
/// <c>WGL_NV_gpu_affinity</c>, <c>WGL_AMD_gpu_association</c> — are one vendor each and one of
/// them is workstation-only, and the trick native applications use, exporting
/// <c>NvOptimusEnablement</c> from the executable, needs an executable this project does not
/// build: a managed application's entry point is a generic host, and the driver reads the
/// exports of the <c>.exe</c> rather than of anything loaded into it.
/// </para>
///
/// <para>
/// What is left is the setting Windows itself exposes under Settings ▸ Display ▸ Graphics: a
/// per-application preference, stored under the application's own path, that the driver reads
/// when it hands out a device. Writing it is not a trick — it is the same value, in the same
/// place, that the operating system's own user interface writes, and clearing the preference
/// removes it again rather than leaving "Automatic" behind as a setting.
/// </para>
///
/// <para>
/// <strong>It is read once per process.</strong> The preference decides which driver's OpenGL
/// implementation gets loaded, and that happens the first time a context is created and never
/// again — so a change made after that is real, saved, and does not take effect until the next
/// launch. <see cref="TakesEffectImmediately"/> is how a caller knows which of the two it is
/// about to do, and it is the whole reason this is not simply a setter.
/// </para>
/// </summary>
public static class GpuPreferences
{
    /// <summary>
    /// Where Windows keeps per-application graphics preferences. Values are named by the full
    /// path of the executable and hold a small <c>key=value;</c> string.
    /// </summary>
    private const string PreferenceKeyPath = @"Software\Microsoft\DirectX\UserGpuPreferences";

    private const string PreferenceField = "GpuPreference";

    /// <summary>
    /// Whether the preference can be expressed on this machine at all. False everywhere but
    /// Windows, where a caller should offer nothing rather than offer a control that cannot
    /// do anything.
    /// </summary>
    public static bool IsSupported => OperatingSystem.IsWindows();

    /// <summary>
    /// Whether applying a preference now would change the adapter this process renders on, or
    /// only the one the next launch will. False as soon as anything has created an OpenGL
    /// context, because that is the moment the driver's implementation is bound to the process.
    /// </summary>
    public static bool TakesEffectImmediately => IsSupported && !GpuContext.HasCreatedContext;

    /// <summary>
    /// The preference currently recorded for this executable, or
    /// <see cref="GpuPreference.Automatic"/> when there is none — which is also the answer on a
    /// platform that has no such setting.
    /// </summary>
    public static GpuPreference Current
    {
        get
        {
            // Written as an inline platform check rather than through IsSupported so the
            // analyzer can see it: a Windows-only call behind a property it cannot follow is a
            // warning, and the warning is right that nothing proves the guard holds.
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
            // A policy-locked or unreadable hive means the preference cannot be known, which
            // is indistinguishable from there not being one.
            return GpuPreference.Automatic;
        }
    }

    /// <summary>
    /// Records <paramref name="preference"/> for this executable, so the driver hands this
    /// application the adapter it names.
    ///
    /// <para>
    /// <see cref="GpuPreference.Automatic"/> deletes the setting rather than writing a value
    /// meaning "no preference": the application put it there, and asking for the default back
    /// should leave the machine as it was found.
    /// </para>
    /// </summary>
    /// <param name="error">
    /// Null on success. On failure, a sentence fit to show a person — a locked-down registry
    /// and a platform with no such concept are both ordinary situations here, not bugs.
    /// </param>
    /// <returns>Whether the preference was recorded.</returns>
    public static bool TryApply(GpuPreference preference, out string? error)
    {
        error = null;

        if (!OperatingSystem.IsWindows())
        {
            // Asking for the default is always satisfiable — a platform with no such setting is
            // already doing it — so only a real choice has anything to fail at.
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

    /// <summary>The name a command line or a settings file uses.</summary>
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

    /// <summary>The name <see cref="TryParse"/> reads back, for a settings file to hold.</summary>
    public static string Name(GpuPreference preference) => preference switch
    {
        GpuPreference.HighPerformance => "high",
        GpuPreference.PowerSaving => "low",
        _ => "auto",
    };

    /// <summary>
    /// What a preference means, said in terms of the machine it is on: the adapter it will
    /// actually select when that can be worked out, and what it asks for when it cannot.
    /// </summary>
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

    /// <summary>
    /// The path the preference is keyed by. Null when the process has none, which is the case
    /// for a single-file host that has been launched from memory.
    /// </summary>
    private static string? ExecutablePath()
    {
        var path = Environment.ProcessPath;

        return string.IsNullOrEmpty(path) ? null : path;
    }

    /// <summary>The number Windows stores. 0 is "let Windows decide", and is never written.</summary>
    private static int Field(GpuPreference preference) => preference switch
    {
        GpuPreference.HighPerformance => 2,
        GpuPreference.PowerSaving => 1,
        _ => 0,
    };

    /// <summary>
    /// Reads a stored value back. The format is a semicolon-separated list of <c>key=value</c>
    /// pairs, of which this is one — parsed as a list rather than matched whole so that a value
    /// Windows has added another field to still reads correctly.
    /// </summary>
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
