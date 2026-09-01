using SoftEngine.Core.Geometry;
using SoftEngine.Core.Geometry.Import;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Projections;
using SoftEngine.Core.Textures;
using SoftEngine.WinForms.Cameras;
using SoftEngine.WinForms.Demos;
using SoftEngine.WinForms.Interop;
using SoftEngine.WinForms.Dialogs;
using System.Numerics;

namespace SoftEngine.WinForms;

public sealed partial class MainScreen
{
    private static readonly DemoEntry[] Demos =
        [.. DemoCatalog.All.Select(demo => new DemoEntry(demo.Display, demo.Id))];

    private void CenterLoadingProgress() =>
        prgLoading.Location = new Point(
            (lblLoading.ClientSize.Width - prgLoading.Width) / 2,
            lblLoading.ClientSize.Height / 2 + 40);

    private Task PrepareWorldAsync(string id)
    {
        _currentDemoId = id;
        _modelPath = null;

        string label = Demos.FirstOrDefault(demo => demo.Id == id)?.Display ?? id;

        return PrepareWorldCoreAsync(progress => DemoCatalog.Build(id, progress), label);
    }

    private Task PrepareWorldFromFileAsync(string path)
    {
        _currentDemoId = string.Empty;
        _modelPath = path;

        RememberRecentFile(path);

        return PrepareWorldCoreAsync(progress => BuildWorldFromFile(path, progress), Path.GetFileName(path));
    }

    private bool _loading;

    private async Task PrepareWorldCoreAsync(Func<IProgress<float>?, WorldSetup> build, string label)
    {
        if (_loading)
        {
            return;
        }

        _loading = true;

        btnLoadModel.Enabled = false;
        mnuLoadModel.Enabled = false;
        mnuOpenModel.Enabled = false;
        mnuOpenScene.Enabled = false;
        prgLoading.Value = 0;
        lblLoading.Visible = true;
        lblLoading.BringToFront();
        UseWaitCursor = true;

        try
        {
            var progress = new Progress<float>(f =>
                prgLoading.Value = Math.Clamp((int)(f * prgLoading.Maximum), 0, prgLoading.Maximum));

            var setup = await Task.Run(() => build(progress));

            if (panel3D1.Scene?.Camera is ArcBallCamera arcBall)
            {
                arcBall.Rotation = Quaternion.Identity;
            }
            panel3D1.Scene?.Camera.Position = setup.CameraPosition;

            panel3D1.ReferenceDistance = setup.CameraPosition.Length();

            ApplyFog();
            ApplyShadows();

            ApplySky(setup.World);

            ApplyAmbientOcclusion();
            ApplyReflections();

            ApplySnapScale();

            _history.Clear();

            panel3D1.Scene?.Projection = setup.Projection ?? ProjectionFor(setup);

            panel3D1.ClearPick();

            panel3D1.Scene?.World = setup.World;

            ClearBakedLight();

            panel3D1.RendererSettings.SkeletonTickSize = setup.SkeletonTickSize;

            panel3D1.SyncAnimationTimer();

            lblCurrentModel.Text = label;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to load '{label}': {ex.Message}", "Load error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
            lblLoading.Visible = false;
            btnLoadModel.Enabled = true;
            mnuLoadModel.Enabled = true;
            mnuOpenModel.Enabled = true;
            mnuOpenScene.Enabled = true;

            _loading = false;

            panel3D1.ClearSelectedPixel();
            panel3D1.Invalidate();
        }
    }

    private static PerspectiveProjection ProjectionFor(WorldSetup setup)
    {
        var worldRadius = 0f;
        foreach (var mesh in setup.World.Meshes)
        {
            var scale = Math.Max(Math.Abs(mesh.Scale.X), Math.Max(Math.Abs(mesh.Scale.Y), Math.Abs(mesh.Scale.Z)));
            var reach = mesh.Position.Length() + mesh.BoundingRadius * scale;

            if (!float.IsNaN(reach) && !float.IsInfinity(reach))
            {
                worldRadius = Math.Max(worldRadius, reach);
            }
        }

        var far = Math.Max(500f, (setup.CameraPosition.Length() + worldRadius) * 2f);

        return new PerspectiveProjection(FieldOfView, .01f, far);
    }

    private static WorldSetup BuildWorldFromFile(string path, IProgress<float>? progress)
    {
        var world = ModelFileLoader.Load(path, progress, ImageTexture.Load, ImageTexture.Load);

        var radius = 0f;
        foreach (var mesh in world.Meshes)
        {
            var scaled = mesh.WorldBoundingRadius();

            if (float.IsFinite(scaled))
            {
                radius = Math.Max(radius, mesh.WorldMatrix.Translation.Length() + scaled);
            }
        }

        if (radius <= 0f)
        {
            radius = 1f;
        }

        var cameraPosition = new Vector3(0, 0, -radius * 3f);
        var projection = new PerspectiveProjection(FieldOfView, .01f, Math.Max(500f, radius * 20f));

        return new WorldSetup(world, cameraPosition, projection) { SkeletonTickSize = radius * 0.05f };
    }
}
