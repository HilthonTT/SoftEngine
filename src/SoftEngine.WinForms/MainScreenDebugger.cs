using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Gizmos;
using SoftEngine.WinForms.Cameras;
using SoftEngine.WinForms.Debugging;
using System.Numerics;

namespace SoftEngine.WinForms;

/// <summary>
/// The graphics debugger: the three panels that show what the renderer did to the last frame,
/// the frame history that lets one be held still while the viewport carries on, and the status
/// bar that names whichever frame is actually on screen.
///
/// <para>
/// Kept apart from <c>MainScreen.cs</c> because it reads the pipeline rather than driving it.
/// Everything here is downstream of a frame that has already been rendered — which is also why
/// it all runs on a timer rather than per frame: a drag repaints far faster than a list view can
/// usefully be rebuilt.
/// </para>
///
/// <para>
/// Named <c>MainScreenDebugger.cs</c> rather than <c>MainScreen.Debugger.cs</c> for the reason
/// spelled out in <c>MainScreenWorkspace.cs</c>: a dotted partial of a <see cref="Form"/> invites
/// Visual Studio to generate a <c>.resx</c> whose resource name collides with the form's own.
/// </para>
/// </summary>
public sealed partial class MainScreen
{
    /// <summary>Set by every rendered frame, cleared when the debugger panels have caught up.</summary>
    private bool _frameDirty;

    /// <summary>
    /// What the object table is naming, rebuilt only when the scene's own signature says the
    /// catalogue would come out different.
    /// </summary>
    private SceneObjectCatalog _catalog = SceneObjectCatalog.Empty;

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

    /// <summary>
    /// Which frame the panels are showing, by its own number, or -1 to follow whatever was
    /// rendered last.
    ///
    /// <para>
    /// The number rather than a position in the kept list, because the list is a window that
    /// slides: the viewport goes on rendering while a frame is pinned, and every new capture
    /// drops the oldest. An index would quietly come to mean a different frame each time that
    /// happened — the panels would creep forward through history while claiming to stand still,
    /// which is worse than either following or stopping.
    /// </para>
    /// </summary>
    private long _pinnedFrameNumber = -1;

    /// <summary>
    /// Where the pinned frame sits in the kept list, or -1 when the panels are following the
    /// renderer.
    /// </summary>
    /// <remarks>
    /// A pinned frame can age out of the window while it is being looked at. The oldest frame
    /// still kept is the closest thing to what was asked for, and the status bar names whichever
    /// frame is actually on screen — so the slip is visible rather than silent.
    /// </remarks>
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

    /// <summary>
    /// Moves the pin one frame. Stepping back from live starts at the newest kept frame, and
    /// stepping forward past it returns to following the renderer — so the two ends of the
    /// history behave the way a person expects rather than stopping dead.
    /// </summary>
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
            // Following the renderer. Back pins the newest frame captured; forward has nowhere
            // to go, since the newest frame is the one already on screen.
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

        // The panels read the pin on their next tick, and there may not be another rendered
        // frame to raise the flag — a still camera repaints nothing.
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
}
