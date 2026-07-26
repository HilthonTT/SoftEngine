namespace SoftEngine.Core.Geometry.Skinning;

/// <summary>
/// Which joints move each vertex, and by how much: a fixed four influences per vertex, stored
/// flat rather than as an array of small arrays.
///
/// Four is the standard budget, and it is a budget rather than a limit of the format —
/// riggers routinely paint six or eight influences onto a vertex, and the fifth is almost
/// always worth a fraction of a percent. <see cref="Builder"/> keeps the four heaviest and
/// renormalizes, so the vertex still receives exactly one unit of influence.
/// </summary>
public sealed class SkinWeights
{
    public const int InfluencesPerVertex = 4;

    public SkinWeights(int vertexCount, int[] jointIndices, float[] weights)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(vertexCount);
        ArgumentNullException.ThrowIfNull(jointIndices);
        ArgumentNullException.ThrowIfNull(weights);

        var expected = vertexCount * InfluencesPerVertex;
        if (jointIndices.Length != expected || weights.Length != expected)
        {
            throw new ArgumentException(
                $"A skin needs {InfluencesPerVertex} joint indices and weights per vertex.",
                nameof(jointIndices));
        }

        VertexCount = vertexCount;
        JointIndices = jointIndices;
        Weights = weights;
    }

    public int VertexCount { get; }

    /// <summary>Four joint indices per vertex; an unused slot is -1.</summary>
    public int[] JointIndices { get; }

    /// <summary>Four weights per vertex, summing to 1 for any vertex with an influence.</summary>
    public float[] Weights { get; }

    /// <summary>
    /// Accumulates any number of influences per vertex and reduces them to the fixed four.
    /// Importers hand over what the file says and let this decide what fits.
    /// </summary>
    public sealed class Builder(int vertexCount)
    {
        private readonly List<(int Joint, float Weight)>[] _influences =
            [.. Enumerable.Range(0, vertexCount).Select(_ => new List<(int, float)>())];

        public int VertexCount { get; } = vertexCount;

        public void Add(int vertexIndex, int jointIndex, float weight)
        {
            if (vertexIndex < 0 || vertexIndex >= _influences.Length || weight <= 0f || jointIndex < 0)
            {
                return;
            }

            _influences[vertexIndex].Add((jointIndex, weight));
        }

        public SkinWeights Build()
        {
            var jointIndices = new int[VertexCount * InfluencesPerVertex];
            var weights = new float[VertexCount * InfluencesPerVertex];

            Array.Fill(jointIndices, -1);

            for (var vertex = 0; vertex < VertexCount; vertex++)
            {
                var influences = _influences[vertex];

                // Descending, so truncating to four drops the least important ones.
                influences.Sort(static (a, b) => b.Weight.CompareTo(a.Weight));

                var kept = System.Math.Min(influences.Count, InfluencesPerVertex);
                var total = 0f;

                for (var i = 0; i < kept; i++)
                {
                    total += influences[i].Weight;
                }

                var slot = vertex * InfluencesPerVertex;

                // A vertex nothing painted keeps its -1 slots and zero weights, which the
                // deformer reads as "leave this one where the modeller put it".
                if (total <= 0f)
                {
                    continue;
                }

                for (var i = 0; i < kept; i++)
                {
                    jointIndices[slot + i] = influences[i].Joint;
                    weights[slot + i] = influences[i].Weight / total;
                }
            }

            return new SkinWeights(VertexCount, jointIndices, weights);
        }
    }
}
