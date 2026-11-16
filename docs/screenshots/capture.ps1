<#
.SYNOPSIS
    Regenerates every image in docs/screenshots from the scene documents in scenes/.

.DESCRIPTION
    Two kinds of picture come out of this:

    * Renders - the viewport alone, written by the viewer's own --screenshot option, at a fixed
      viewport size. These are what DESIGN.md illustrates techniques with.
    * Window shots - the whole editor, captured from the screen once a scene has loaded. These
      are what README.md shows the application with.

    The viewer runs against a throwaway settings file (SOFTENGINE_VIEWER_SETTINGS), so the
    screenshots start from defaults - the CPU rasterizer, the default layout - and leave your
    own recent files, window placement and render engine alone.

    Windows only, since the viewer is. Run from anywhere:

        pwsh docs/screenshots/capture.ps1            # everything
        pwsh docs/screenshots/capture.ps1 -Only shadows,skull
#>
param(
    [string[]] $Only = @(),
    [switch] $NoBuild
)

$ErrorActionPreference = 'Stop'

$here = $PSScriptRoot
$root = Resolve-Path (Join-Path $here '..\..')
$scenes = Join-Path $here 'scenes'
$project = Join-Path $root 'src\SoftEngine.WinForms\SoftEngine.WinForms.csproj'

if (-not $NoBuild) {
    # Release, or the path tracer and TAA shots take minutes instead of seconds.
    dotnet build $project -c Release --nologo -v quiet | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
}

# Ask MSBuild where the build went rather than guessing at the target framework's folder.
$target = dotnet msbuild $project -getProperty:TargetPath -p:Configuration=Release
$exe = [IO.Path]::ChangeExtension($target.Trim(), '.exe')

if (-not (Test-Path $exe)) { throw "SoftEngine.WinForms.exe was not built: $exe" }

$settings = Join-Path ([IO.Path]::GetTempPath()) "softengine-screenshots-$PID.json"
$env:SOFTENGINE_VIEWER_SETTINGS = $settings

Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class ScreenshotWindow
{
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect { public int Left, Top, Right, Bottom; }
}
'@

# Measure the window in physical pixels, as the per-monitor-aware viewer lays itself out in.
[ScreenshotWindow]::SetProcessDPIAware() | Out-Null

function Test-Wanted([string] $name) {
    return $Only.Count -eq 0 -or $Only -contains $name
}

# Renders: name, viewport size, and any extra viewer arguments.
$renders = @(
    @{ Name = 'pbr-spheres';      Size = '1280x720' },
    @{ Name = 'shadows';          Size = '1280x720' },
    @{ Name = 'post-processing';  Size = '1280x720' },
    @{ Name = 'reflections';      Size = '1280x720' },
    @{ Name = 'normal-mapping';   Size = '1280x720' },
    @{ Name = 'primitives';       Size = '1280x560' },
    @{ Name = 'skinning';         Size = '1280x720'; Args = @('--time', '1.1') },
    @{ Name = 'rig';              Size = '1280x720'; Args = @('--time', '0.6') },
    @{ Name = 'picking';          Size = '1280x720'; Args = @('--select', '3') },
    @{ Name = 'buffer-shaded';    Size = '1024x576' },
    @{ Name = 'buffer-depth';     Size = '1024x576' },
    @{ Name = 'buffer-normals';   Size = '1024x576' },
    @{ Name = 'buffer-overdraw';  Size = '1024x576' },
    @{ Name = 'buffer-shadowmap'; Size = '1024x576' }
)

foreach ($render in $renders) {
    if (-not (Test-Wanted $render.Name)) { continue }

    $scene = Join-Path $scenes "$($render.Name).scene.json"
    $output = Join-Path $here "$($render.Name).png"
    $arguments = @("`"$scene`"", '--viewport', $render.Size, '--screenshot', "`"$output`"") + @($render.Args | Where-Object { $_ })

    # Every shot starts from defaults, not from whatever the previous one left behind.
    Remove-Item $settings -ErrorAction SilentlyContinue

    Write-Host "render  $($render.Name)"
    $process = Start-Process -FilePath $exe -ArgumentList $arguments -PassThru
    if (-not $process.WaitForExit(180000)) {
        Stop-Process -Id $process.Id -Force
        throw "$($render.Name) did not finish."
    }
    if ($process.ExitCode -ne 0) { throw "$($render.Name) failed with exit code $($process.ExitCode)." }
}

# Window shots: name, scene (or none for the default world), extra arguments, and how long to
# let the scene settle - the Rendering workspace path-traces, which refines for a while.
$windows = @(
    @{ Name = 'skull';              Scene = 'skull';       Args = @();                                   Wait = 8 },
    @{ Name = 'elephant';           Scene = 'elephant';    Args = @('--select', '0');                    Wait = 12 },
    @{ Name = 'parrot';             Scene = 'parrot';      Args = @('--workspace', 'Shading');           Wait = 10 },
    @{ Name = 'workspace-debugging'; Scene = 'shadows';    Args = @('--workspace', 'Debugging');         Wait = 10 },
    @{ Name = 'workspace-rendering'; Scene = 'pbr-spheres'; Args = @('--workspace', 'Rendering');        Wait = 30 }
)

foreach ($window in $windows) {
    if (-not (Test-Wanted $window.Name)) { continue }

    $scene = Join-Path $scenes "$($window.Scene).scene.json"
    $output = Join-Path $here "$($window.Name).png"
    $arguments = @("`"$scene`"", '--viewport', '1280x720') + $window.Args

    Remove-Item $settings -ErrorAction SilentlyContinue

    Write-Host "window  $($window.Name)"
    $process = Start-Process -FilePath $exe -ArgumentList $arguments -PassThru

    try {
        Start-Sleep -Seconds $window.Wait
        $process.Refresh()
        if ($process.HasExited) { throw "$($window.Name): the viewer exited with code $($process.ExitCode)." }

        $handle = $process.MainWindowHandle
        $rect = New-Object ScreenshotWindow+Rect
        [ScreenshotWindow]::GetWindowRect($handle, [ref] $rect) | Out-Null

        $bitmap = New-Object System.Drawing.Bitmap ($rect.Right - $rect.Left), ($rect.Bottom - $rect.Top)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $dc = $graphics.GetHdc()
        # 2 = PW_RENDERFULLCONTENT: capture what is drawn, even under another window.
        [ScreenshotWindow]::PrintWindow($handle, $dc, 2) | Out-Null
        $graphics.ReleaseHdc($dc)
        $graphics.Dispose()

        # Docs don't need a high-DPI screen's full resolution; keep them at most 1920 wide.
        if ($bitmap.Width -gt 1920) {
            $height = [int]($bitmap.Height * 1920 / $bitmap.Width)
            $scaled = New-Object System.Drawing.Bitmap 1920, $height
            $g = [System.Drawing.Graphics]::FromImage($scaled)
            $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $g.DrawImage($bitmap, 0, 0, 1920, $height)
            $g.Dispose()
            $bitmap.Dispose()
            $bitmap = $scaled
        }

        $bitmap.Save($output, [System.Drawing.Imaging.ImageFormat]::Png)
        $bitmap.Dispose()
    }
    finally {
        if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    }
}

Remove-Item $settings -ErrorAction SilentlyContinue
Write-Host 'done'
