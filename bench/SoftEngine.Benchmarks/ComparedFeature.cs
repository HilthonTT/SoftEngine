namespace SoftEngine.Benchmarks;

/// <summary>The optimization a <c>--compare</c> run switches off to measure what it is worth.</summary>
internal enum ComparedFeature
{
    None,

    /// <summary>The tile's coarse depth bound, which drops a binned triangle before its pixels.</summary>
    HierarchicalZ,

    /// <summary>The occlusion pass, which drops a hidden mesh before its vertices.</summary>
    Occlusion,

    /// <summary>Filling a span a vector of pixels at a time rather than one at a time.</summary>
    VectorizedSpans,
}
