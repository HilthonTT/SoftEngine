using System.Runtime.CompilerServices;

// The GPU backend is a renderer like the ones in this assembly, and needs the same
// renderer-facing surface they do — the counters on RenderStats, the frame bookkeeping on
// RenderDiagnostics. It lives apart only because it drags in a native OpenGL binding that
// nothing else here should have to carry.
[assembly: InternalsVisibleTo("SoftEngine.Gpu")]
