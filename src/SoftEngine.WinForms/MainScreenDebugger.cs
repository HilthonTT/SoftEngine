using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Gizmos;
using SoftEngine.Gpu;
using SoftEngine.WinForms.Cameras;
using SoftEngine.WinForms.Debugging;
using System.Numerics;

namespace SoftEngine.WinForms;

public sealed partial class MainScreen
{
    /// <summary>Set by every rendered frame, cleared when the debugger panels have caught up.</summary>
    private bool _frameDirty;

    private SceneObjectCatalog _catalog = SceneObjectCatalog.Empty;

    /// <summary>
    /// The switches that outlive the session. Read once, here, because a field initializer runs
    /// before the constructor body — and the backend has to be restored while the menu is being
    /// wired, not after somebody has already seen the wrong item ticked.
    /// </summary>
    private readonly ViewerSettings _settings = ViewerSettings.Load();

    #region Render backend

    /// <summary>
    /// Wires View → Rendered by, and the status readout that says which one won.
    ///
    /// <para>
    /// The GPU item is offered whether or not there is a GPU, and reports what happened
    /// afterwards rather than being greyed out beforehand. Finding out costs an OpenGL
    /// context, and a menu item that is simply missing on a machine with a graphics card in
    /// it — because a driver is out of date, say — tells you nothing about why.
    /// </para>
    /// </summary>
    private void InitializeBackendMenu()
    {
        mnuRenderCpu.Click += (s, e) => SelectBackend(RenderBackend.Cpu);
        mnuRenderGpu.Click += (s, e) => SelectBackend(RenderBackend.Gpu);
        mnuRenderTrace.Click += (s, e) => SelectBackend(RenderBackend.Trace);

        panel3D1.BackendChanged += (s, e) => UpdateBackendMenu();

        // The tracer's sample count climbs frame by frame while it refines, and the status bar is
        // where anyone would look to see whether it is still working.
        panel3D1.FrameRendered += (s, e) =>
        {
            if (panel3D1.Backend == RenderBackend.Trace)
            {
                lblBackendStatus.Text = panel3D1.BackendDescription;
            }
        };

        RestoreBackend();

        UpdateBackendMenu();
    }

    private void SelectBackend(RenderBackend backend)
    {
        if (panel3D1.Backend == backend && panel3D1.BackendFallback is null)
        {
            return;
        }

        using (new WaitCursorScope(this))
        {
            panel3D1.Backend = backend;
        }

        RememberBackend();

        if (panel3D1.BackendFallback is { } fallback)
        {
            MessageBox.Show(
                this,
                $"{fallback}\n\nThe viewport is still being rendered on the CPU.",
                "No graphics adapter",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }

    /// <summary>
    /// Puts the viewport back on the backend it was left on.
    ///
    /// <para>
    /// A request that falls back says so through the status bar rather than through a dialog. A
    /// modal box in front of a window that has not appeared yet is a poor way to find out that a
    /// driver is missing, and unlike a menu click nobody has just asked a question that is waiting
    /// for an answer.
    /// </para>
    /// </summary>
    private void RestoreBackend()
    {
        // The panel is already on the CPU, and building the renderer it is already using would cost
        // a rebuild to arrive back where it started.
        if (_settings.Backend == RenderBackend.Cpu)
        {
            return;
        }

        using (new WaitCursorScope(this))
        {
            panel3D1.Backend = _settings.Backend;
        }

        RememberBackend();
    }

    /// <summary>
    /// Records the backend that <em>settled</em>, which is not always the one that was asked for.
    ///
    /// Saving the request instead would leave a machine whose graphics driver has gone missing
    /// probing for an OpenGL context on every launch, and the file claiming a backend the menu is
    /// not showing as ticked. What is remembered is what is on screen.
    /// </summary>
    private void RememberBackend()
    {
        if (_settings.Backend == panel3D1.Backend)
        {
            return;
        }

        _settings.Backend = panel3D1.Backend;
        _settings.Save();
    }

    private void UpdateBackendMenu()
    {
        var backend = panel3D1.Backend;

        mnuRenderCpu.Checked = backend == RenderBackend.Cpu;
        mnuRenderGpu.Checked = backend == RenderBackend.Gpu;
        mnuRenderTrace.Checked = backend == RenderBackend.Trace;

        lblBackendStatus.Text = panel3D1.BackendDescription;

        // The adapter's own name, which is the only way to tell an integrated part from the
        // discrete one a laptop may also have.
        lblBackendStatus.ToolTipText = backend switch
        {
            RenderBackend.Gpu when panel3D1.Adapter is { } adapter =>
                $"{adapter.Vendor} · {adapter.Renderer}\nOpenGL {adapter.Version}",
            RenderBackend.Trace =>
                "Light traced through the scene on the CPU, refining for as long as nothing moves.",
            _ => "Every triangle rasterized on the CPU by this engine's own scanline filler.",
        };

        // A request that fell back explains itself here. It is the only account a restored choice
        // gets — nobody clicked anything at startup, so there is no dialog to have answered.
        if (panel3D1.BackendFallback is { } fallback)
        {
            lblBackendStatus.ToolTipText = $"{fallback}\n\n{lblBackendStatus.ToolTipText}";
        }
    }

    /// <summary>An hourglass for as long as it is held. Building a GL context is not instant.</summary>
    private readonly struct WaitCursorScope : IDisposable
    {
        private readonly Control _control;
        private readonly Cursor _previous;

        public WaitCursorScope(Control control)
        {
            _control = control;
            _previous = control.Cursor;
            control.Cursor = Cursors.WaitCursor;
        }

        public void Dispose() => _control.Cursor = _previous;
    }

    #endregion

    #region Graphics debugger

    /// <summary>
    /// Wires the debugger panels to the viewport. The renderer records its event list every
    /// frame, but the panels only pull from it on a timer: a drag repaints far faster than a
    /// list view can usefully be rebuilt.
    /// </summary>
    private void InitializeDebugger()
    {
        panel3D1.Diagnostics.CaptureEvents = mnuRecordEvents.Checked;
        panel3D1.ShowStatsOverlay = mnuStatsOverlay.Checked;

        panel3D1.FrameRendered += (s, e) => _frameDirty = true;
        panel3D1.ZoomChanged += (s, e) => UpdateStatus();
        panel3D1.SelectedPixelChanged += (s, e) => UpdateStatus();

        // A click asks two questions of the same pixel: the probe records what the renderer
        // did there, and the ray says which mesh is actually under it. The second is what
        // selects the row in the object table — the same obj:N the event list would name.
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

        // Clicking a write in the pixel history reveals the event and the object behind it.
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

    /// <summary>Pulls the last frame's capture into the three panels — at most once per timer tick.</summary>
    private void RefreshDebugPanels()
    {
        // A pinned frame is not going to change, so there is nothing to pull until the pin moves
        // — but the panels still have to be filled the first time it is set, which is what the
        // dirty flag is raised for there too.
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

        // The kept-frame count climbs as frames arrive, and the step items become reachable the
        // moment there is a first one — both belong on the timer rather than on every frame.
        UpdateFrameHistoryMenu();

        UpdateStatus();
    }

    #region Frame history

    /// <summary>How many finished frames are kept when the history is switched on.</summary>
    private const int FrameHistoryDepth = 60;

    private readonly FrameHistoryCursor _frameHistory = new();

    private FrameCapture? PinnedFrame() => _frameHistory.PinnedIn(panel3D1.Diagnostics.Frames);

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
        if (_frameHistory.Step(panel3D1.Diagnostics.Frames, direction))
        {
            ShowPinnedFrame();
        }
    }

    private void GoLive()
    {
        if (_frameHistory.Release())
        {
            ShowPinnedFrame();
        }
    }

    private void ShowPinnedFrame()
    {
        // The panels read the pin on their next tick, and there may not be another rendered
        // frame to raise the flag — a still camera repaints nothing.
        _frameDirty = true;

        UpdateFrameHistoryMenu();
        RefreshDebugPanels();
    }

    private void UpdateFrameHistoryMenu()
    {
        var keeping = mnuKeepFrames.Checked;
        var frames = panel3D1.Diagnostics.Frames;

        mnuPreviousFrame.Enabled = keeping && _frameHistory.CanStepBack(frames);
        mnuNextFrame.Enabled = keeping && _frameHistory.CanStepForward(frames);
        mnuLatestFrame.Enabled = _frameHistory.IsPinned;

        mnuKeepFrames.Text = keeping
            ? $"&Keep recent frames ({frames.Count}/{FrameHistoryDepth})"
            : "&Keep recent frames";
    }

    #endregion

    private void UpdateStatus()
    {
        // 100% is the framing the world loaded with; the wheel and W/S move away from it.
        var buffer = panel3D1.BufferSize;
        lblZoomStatus.Text = $"Zoom: {panel3D1.Zoom * 100f:0}%  ·  {buffer.Width} × {buffer.Height}";

        if (panel3D1.SelectedPixel is { } pixel && panel3D1.SelectedPixelNormalized is { } normalized)
        {
            // What the ray found under the same pixel, when it found anything — the mesh's
            // own identifier, so it can be looked up in the object table and the event list.
            var picked = string.Empty;

            if (panel3D1.Picked is { } hit && panel3D1.Scene?.World is { } world)
            {
                var objectId = SceneObjectIds.Mesh(world.Lights.Count, hit.MeshIndex);

                // A selection made without a ray — adding a primitive, or an undo putting one
                // back — names no triangle, and reporting "tri:-1 at 0" would read as a pick that
                // went wrong rather than as one that was never cast.
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

        // Only ever offered for something that is there to delete.
        mnuDelete.Enabled = panel3D1.Picked is not null;

        // A modal gesture has no handle on screen to show what it is doing, so the status bar is
        // the whole of its feedback — what it is, which axis it is pressed against, and the two
        // keys that end it. Ahead of the gizmo's own line because only one of them ever runs.
        if (_transform is { IsActive: true })
        {
            lblPixelStatus.Text =
                $"{_transform.Describe()}  ·  X / Y / Z to constrain  ·  click or Enter to confirm, Esc to cancel";
        }

        // A drag has to say what it did in numbers as well as in pixels: eyeballing a mesh
        // into place is exactly the case where you then want to know where "place" was.
        else if (_gizmo is { IsActive: true, Target: { } target })
        {
            var what = _gizmo.Mode switch
            {
                GizmoMode.Rotate => $"rotation ({Degrees(target.Rotation.XPitch)}, {Degrees(target.Rotation.YYaw)}, {Degrees(target.Rotation.ZRoll)})",
                GizmoMode.Scale => $"scale ({target.Scale.X:0.###}, {target.Scale.Y:0.###}, {target.Scale.Z:0.###})",
                _ => $"position ({target.Position.X:0.###}, {target.Position.Y:0.###}, {target.Position.Z:0.###})",
            };

            lblPixelStatus.Text += $"  ·  {what}";

            // The increment has to be visible, or a drag that lands on a round number reads as
            // the renderer having quietly rounded it.
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

            // The named view, when the camera is lined up with one: worth saying, because
            // that is the difference between a view you can reason about and one that is
            // merely close to it.
            var view = camera is ArcBallCamera { CurrentAxisView: { } axisView } ? $" · {axisView}" : string.Empty;

            lblCameraStatus.Text = $"Camera: ({position.X:0.##}, {position.Y:0.##}, {position.Z:0.##}){view}";
        }

        // A pinned frame reports its own numbers. Showing the live ones beside a pinned event
        // list would put two different frames on the same status bar, which is the one thing a
        // history must not do.
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

    /// <summary>A mesh's Euler angles are stored in radians; nobody reads a pose in radians.</summary>
    private static string Degrees(float radians) => $"{radians * 180f / MathF.PI:0.#}°";

    #endregion
}
