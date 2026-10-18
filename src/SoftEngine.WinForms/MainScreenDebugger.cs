using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Gizmos;
using SoftEngine.WinForms.Cameras;
using SoftEngine.WinForms.Debugging;
using System.Numerics;

namespace SoftEngine.WinForms;

public sealed partial class MainScreen
{
    private bool _frameDirty;

    private SceneObjectCatalog _catalog = SceneObjectCatalog.Empty;

    private void InitializeDebugger()
    {
        panel3D1.Diagnostics.CaptureEvents = mnuRecordEvents.Checked;
        panel3D1.ShowStatsOverlay = mnuStatsOverlay.Checked;

        panel3D1.FrameRendered += (s, e) => _frameDirty = true;
        panel3D1.ZoomChanged += (s, e) => UpdateStatus();
        panel3D1.SelectedPixelChanged += (s, e) => UpdateStatus();

        panel3D1.PickedChanged += (s, e) =>
        {
            if (panel3D1.Picked is { } hit && panel3D1.Scene?.World is { } world)
            {
                objectTablePanel.SelectObject(SceneObjectIds.Mesh(world.Lights.Count, hit.MeshIndex));
            }

            UpdateStatus();
        };

        tmrDebugRefresh.Tick += (s, e) => RefreshDebugPanels();
        tmrDebugRefresh.Start();

        objectTablePanel.ActiveChanged += (s, e) => panel3D1.Invalidate();

        pixelHistoryPanel.WriteSelected += (s, write) =>
        {
            eventListPanel.SelectEvent(write.EventIndex);

            if (write.ObjectId >= 0)
            {
                objectTablePanel.SelectObject(write.ObjectId);
            }
        };

        eventListPanel.EventSelected += (s, graphicsEvent) =>
        {
            if (graphicsEvent.ObjectId >= 0)
            {
                objectTablePanel.SelectObject(graphicsEvent.ObjectId);
            }
        };

        mnuLoadModel.Click += async (s, e) => await ShowModelPickerAsync();
        mnuOpenModel.Click += async (s, e) => await OpenModelAsync();
        mnuOpenScene.Click += async (s, e) => await OpenSceneAsync();
        mnuSaveScene.Click += (s, e) => SaveScene();
        mnuScreenshot.Click += (s, e) => SaveScreenshot();
        lblScreenshotHint.Click += (s, e) => SaveScreenshot();
        mnuExit.Click += (s, e) => Close();

        mnuPixelHistory.CheckedChanged += (s, e) => splitLeft.Panel2Collapsed = !mnuPixelHistory.Checked;
        mnuObjectTable.CheckedChanged += (s, e) => splitCenter.Panel2Collapsed = !mnuObjectTable.Checked;
        mnuEventList.CheckedChanged += (s, e) => splitRight.Panel2Collapsed = !mnuEventList.Checked;

        mnuStatsOverlay.CheckedChanged += (s, e) =>
        {
            panel3D1.ShowStatsOverlay = mnuStatsOverlay.Checked;
            panel3D1.Invalidate();
        };

        mnuRecordEvents.CheckedChanged += (s, e) =>
        {
            panel3D1.Diagnostics.CaptureEvents = mnuRecordEvents.Checked;
            panel3D1.Invalidate();
        };

        InitializeBackendMenu();

        mnuViewFront.Click += (s, e) => panel3D1.LookAlong(AxisView.Front);
        mnuViewBack.Click += (s, e) => panel3D1.LookAlong(AxisView.Back);
        mnuViewRight.Click += (s, e) => panel3D1.LookAlong(AxisView.Right);
        mnuViewLeft.Click += (s, e) => panel3D1.LookAlong(AxisView.Left);
        mnuViewTop.Click += (s, e) => panel3D1.LookAlong(AxisView.Top);
        mnuViewBottom.Click += (s, e) => panel3D1.LookAlong(AxisView.Bottom);
        mnuViewOpposite.Click += (s, e) => panel3D1.FlipView();

        mnuTurnX.Click += (s, e) => panel3D1.RotateAroundWorldAxis(Vector3.UnitX, Panel3D.RotationStep);
        mnuTurnY.Click += (s, e) => panel3D1.RotateAroundWorldAxis(Vector3.UnitY, Panel3D.RotationStep);
        mnuTurnZ.Click += (s, e) => panel3D1.RotateAroundWorldAxis(Vector3.UnitZ, Panel3D.RotationStep);

        mnuZoomIn.Click += (s, e) => panel3D1.ZoomIn();
        mnuZoomOut.Click += (s, e) => panel3D1.ZoomOut();
        mnuZoomActual.Click += (s, e) => panel3D1.ZoomActualSize();
        mnuClearPixel.Click += (s, e) => panel3D1.ClearSelectedPixel();

        InitializeFrameHistory();

        UpdateStatus();
    }

    private void RefreshDebugPanels()
    {
        if (!_frameDirty)
        {
            return;
        }

        _frameDirty = false;

        var scene = panel3D1.Scene;
        var signature = SceneObjectCatalog.SignatureOf(scene, panel3D1.Painter, panel3D1.PostProcess);

        if (_catalog.Signature != signature)
        {
            _catalog = SceneObjectCatalog.Build(scene, panel3D1.Painter, panel3D1.PostProcess);
            objectTablePanel.SetCatalog(_catalog);
        }

        var pinned = PinnedFrame();

        if (!splitRight.Panel2Collapsed)
        {
            if (pinned is { } capture)
            {
                eventListPanel.SetEvents(capture.Events);
            }
            else
            {
                eventListPanel.SetEvents(panel3D1.Diagnostics.Events);
            }
        }

        if (!splitLeft.Panel2Collapsed)
        {
            pixelHistoryPanel.SetHistory(pinned?.PixelHistory ?? panel3D1.Diagnostics.PixelHistory, _catalog);
        }

        UpdateFrameHistoryMenu();

        UpdateStatus();
    }

    #region Frame history

    private const int FrameHistoryDepth = 60;

    private long _pinnedFrameNumber = -1;

    private int PinnedIndex()
    {
        if (_pinnedFrameNumber < 0)
        {
            return -1;
        }

        var frames = panel3D1.Diagnostics.Frames;

        if (frames.Count == 0)
        {
            return -1;
        }

        for (var i = frames.Count - 1; i >= 0; i--)
        {
            if (frames[i].FrameNumber == _pinnedFrameNumber)
            {
                return i;
            }
        }

        return 0;
    }

    private FrameCapture? PinnedFrame()
    {
        var index = PinnedIndex();

        return index >= 0 ? panel3D1.Diagnostics.Frames[index] : null;
    }

    private void InitializeFrameHistory()
    {
        mnuKeepFrames.CheckedChanged += (s, e) =>
        {
            panel3D1.Diagnostics.HistoryCapacity = mnuKeepFrames.Checked ? FrameHistoryDepth : 0;

            if (!mnuKeepFrames.Checked)
            {
                panel3D1.Diagnostics.ClearHistory();
                GoLive();
            }

            UpdateFrameHistoryMenu();
        };

        mnuPreviousFrame.Click += (s, e) => StepFrame(-1);
        mnuNextFrame.Click += (s, e) => StepFrame(+1);
        mnuLatestFrame.Click += (s, e) => GoLive();

        UpdateFrameHistoryMenu();
    }

    private void StepFrame(int direction)
    {
        var frames = panel3D1.Diagnostics.Frames;

        if (frames.Count == 0)
        {
            return;
        }

        var index = PinnedIndex();

        if (index < 0)
        {
            if (direction < 0)
            {
                PinFrame(frames[^1].FrameNumber);
            }

            return;
        }

        var next = index + direction;

        if (next >= frames.Count)
        {
            GoLive();
            return;
        }

        PinFrame(frames[Math.Max(next, 0)].FrameNumber);
    }

    private void PinFrame(long frameNumber)
    {
        _pinnedFrameNumber = frameNumber;

        _frameDirty = true;

        UpdateFrameHistoryMenu();
        RefreshDebugPanels();
    }

    private void GoLive()
    {
        if (_pinnedFrameNumber < 0)
        {
            return;
        }

        _pinnedFrameNumber = -1;
        _frameDirty = true;

        UpdateFrameHistoryMenu();
        RefreshDebugPanels();
    }

    private void UpdateFrameHistoryMenu()
    {
        var keeping = mnuKeepFrames.Checked;
        var frames = panel3D1.Diagnostics.Frames.Count;
        var index = PinnedIndex();

        mnuPreviousFrame.Enabled = keeping && frames > 0 && index != 0;
        mnuNextFrame.Enabled = keeping && index >= 0;
        mnuLatestFrame.Enabled = _pinnedFrameNumber >= 0;

        mnuKeepFrames.Text = keeping
            ? $"&Keep recent frames ({frames}/{FrameHistoryDepth})"
            : "&Keep recent frames";
    }

    #endregion

    private void UpdateStatus()
    {
        var buffer = panel3D1.BufferSize;
        lblZoomStatus.Text = $"Zoom: {panel3D1.Zoom * 100f:0}%  ·  {buffer.Width} × {buffer.Height}";

        if (panel3D1.SelectedPixel is { } pixel && panel3D1.SelectedPixelNormalized is { } normalized)
        {
            var picked = string.Empty;

            if (panel3D1.Picked is { } hit && panel3D1.Scene?.World is { } world)
            {
                var objectId = SceneObjectIds.Mesh(world.Lights.Count, hit.MeshIndex);

                var where = hit.TriangleIndex >= 0 ? $" tri:{hit.TriangleIndex} at {hit.Distance:0.##}" : string.Empty;

                picked = $"  ·  picked obj:{objectId} ({hit.Mesh.GetType().Name}){where}";
            }

            lblPixelStatus.Text =
                $"Selected pixel X: {pixel.X} ({normalized.X:0.000}) Y: {pixel.Y} ({normalized.Y:0.000}){picked}";
        }
        else
        {
            lblPixelStatus.Text = "Selected pixel: none — click the viewport to probe and pick one";
        }

        mnuDelete.Enabled = panel3D1.Picked is not null;

        if (_transform is { IsActive: true })
        {
            lblPixelStatus.Text =
                $"{_transform.Describe()}  ·  X / Y / Z to constrain  ·  click or Enter to confirm, Esc to cancel";
        }

        else if (_gizmo is { IsActive: true, Target: { } target })
        {
            var what = _gizmo.Mode switch
            {
                GizmoMode.Rotate => $"rotation ({Degrees(target.Rotation.XPitch)}, {Degrees(target.Rotation.YYaw)}, {Degrees(target.Rotation.ZRoll)})",
                GizmoMode.Scale => $"scale ({target.Scale.X:0.###}, {target.Scale.Y:0.###}, {target.Scale.Z:0.###})",
                _ => $"position ({target.Position.X:0.###}, {target.Position.Y:0.###}, {target.Position.Z:0.###})",
            };

            lblPixelStatus.Text += $"  ·  {what}";

            if (_gizmo.Snap.Enabled)
            {
                var step = _gizmo.Mode switch
                {
                    GizmoMode.Rotate => Degrees(_gizmo.Snap.RotateStep),
                    GizmoMode.Scale => $"{_gizmo.Snap.ScaleStep:0.###}×",
                    _ => $"{_gizmo.Snap.TranslateStep:0.###}",
                };

                lblPixelStatus.Text += $"  ·  snap {step}";
            }
        }

        if (panel3D1.Scene?.Camera is { } camera)
        {
            var position = camera.Position;

            var view = camera is ArcBallCamera { CurrentAxisView: { } axisView } ? $" · {axisView}" : string.Empty;

            lblCameraStatus.Text = $"Camera: ({position.X:0.##}, {position.Y:0.##}, {position.Z:0.##}){view}";
        }

        if (PinnedFrame() is { } pinned)
        {
            lblFrameStatus.Text =
                $"Frame #{pinned.FrameNumber} · {pinned.Stats.TotalTimeMs} ms · pinned (live is #{panel3D1.Diagnostics.FrameNumber})";
        }
        else
        {
            var stats = panel3D1.Stats;
            lblFrameStatus.Text = $"Frame #{panel3D1.Diagnostics.FrameNumber} · {stats.CalculationTimeMs + stats.PainterTimeMs} ms";
        }
    }

    private static string Degrees(float radians) => $"{radians * 180f / MathF.PI:0.#}°";
}
