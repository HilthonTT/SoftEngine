# Contributing to SoftEngine

Thanks for taking an interest. SoftEngine is a software 3D rasterizer — the whole pipeline runs on
the CPU — so most changes here are visual, and "it builds" is rarely enough evidence that one is
right. This file is mostly about how to get that evidence.

## Getting set up

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download). `global.json` pins the major
version and rolls forward to whatever 10.x feature band you have installed.

```bash
git clone https://github.com/HilthonTT/SoftEngine.git
cd SoftEngine

dotnet build SoftEngine.slnx
dotnet test tests/SoftEngine.Core.Tests
dotnet run --project src/SoftEngine.WinForms
```

Windows is required for the viewer (WinForms) and the GPU backend's window creation.
`SoftEngine.Core` itself is platform-neutral and its test suite runs anywhere.

### Two build gotchas that are not your change

- **`MSB3021` / `MSB3027` file-lock errors.** The viewer app is running and holding its own
  executable. Close it and build again.
- **Benchmarks must be Release.** `dotnet run -c Release --project bench/SoftEngine.Benchmarks`.
  A Debug build measures the debugger, not the renderer, and the numbers are meaningless.

## Layout

| Path | What it is |
| --- | --- |
| `src/SoftEngine.Core` | The engine. Platform-neutral, no graphics API, where nearly all logic lives. |
| `src/SoftEngine.Gpu` | An OpenGL backend that fills the same frame, for comparison. |
| `src/SoftEngine.WinForms` | The interactive viewer and the graphics debugger. |
| `src/SoftEngine.Cli` | Headless model/scene → PNG rendering. |
| `tests/SoftEngine.Core.Tests` | xUnit. The only test project. |
| `bench/SoftEngine.Benchmarks` | Frame-time harness. |

`SoftEngine.WinForms` and `SoftEngine.Cli` have no tests of their own — anything worth asserting
about should be pushed down into `Core`, where it can be.

## Before you open a pull request

```bash
dotnet format SoftEngine.slnx --verify-no-changes   # CI runs exactly this
dotnet build SoftEngine.slnx
dotnet test tests/SoftEngine.Core.Tests
```

CI additionally builds with `TreatWarningsAsErrors`. The repository is at zero warnings and the
intent is to keep it there; locally warnings stay warnings, so a half-written change still builds.

## Testing a renderer

### Unit tests

Ordinary xUnit. Prefer asserting on a number the pipeline produces over asserting on a pixel where
a number will do.

### Golden images

`tests/SoftEngine.Core.Tests/Golden/` renders 15 small scenes and compares them against committed
PNGs in `Golden/References/`.

- Baselines resolve through `[CallerFilePath]`, **not** the test output directory, so they are
  rewritten in place in your working tree.
- Re-recording is deliberate and explicit:

  ```bash
  SOFTENGINE_UPDATE_GOLDEN=1 dotnet test tests/SoftEngine.Core.Tests
  ```

  A missing baseline is written *and* fails, so a new scene cannot pass on its first run.
- Failures drop `.actual.png` and `.diff.png` into `Golden/Artifacts/` (gitignored). Look at them.

**If your change moves a golden image, say so in the pull request and show the diff.** A re-recorded
baseline is a claim that the new output is more correct than the old one, and that claim is the part
a reviewer needs. Silently updated baselines are the one thing that makes this suite worthless.

### Visual verification

For anything that changes rendered output, load the relevant bundled world in the viewer and say in
the pull request which one and which shading mode you checked. Before/after screenshots are worth
more than any description. The demos exist to exercise specific paths — `transparency`, `shadows`,
`cascades`, `normalmapping`, `pbrspheres`, `primitives` — so use the one that covers your change.

### Performance

If a change touches the fill, the binner, culling or clipping, run the benchmark both ways:

```bash
dotnet run -c Release --project bench/SoftEngine.Benchmarks
dotnet run -c Release --project bench/SoftEngine.Benchmarks -- --compare occlusion
```

Post the numbers. A rasterizer change with no timing is an assertion, not a result.

## Style

`.editorconfig` describes the house style and `dotnet format` enforces the mechanical part of it.
Beyond that:

- **Comments explain *why*, not *what*.** The codebase is full of comments recording the trap a
  piece of code exists to avoid — why the gizmo freezes its grab frame, why a validity pass needs
  its own direction set, why the frame pin is a number and not an index. That is the house
  convention and it is the most valuable thing in the repository. Match it.
- Match the surrounding code's density and idiom. Pattern matching, `is { } x`, switch expressions
  and file-scoped namespaces are used throughout.
- Keep a pull request to a single concern, and keep formatting churn out of it.
- Public API changes get called out in the pull request description.

## Reporting bugs

Use the issue templates. For a rendering bug the two things that matter most are **which world and
shading mode** reproduce it and **a screenshot**. If the graphics debugger says something useful —
a pixel history, an event list entry, a buffer view — include that too; it is what the debugger is
for.

## Security

Please do not open a public issue for a security problem. See [SECURITY.md](SECURITY.md).

## Code of conduct

Participation is governed by the [Code of Conduct](CODE_OF_CONDUCT.md).

## License

By contributing you agree that your contributions are licensed under the [MIT License](LICENSE).
