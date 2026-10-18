namespace SoftEngine.Benchmarks;

internal readonly record struct BenchmarkResult(
    string Scene,
    double MedianMs,
    double MinMs,
    double P95Ms,
    int Triangles,
    int DrawnTriangles,
    int Pixels,
    int Occluders,
    int HiddenMeshes);
