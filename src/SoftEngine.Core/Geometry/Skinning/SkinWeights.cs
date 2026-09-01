namespace SoftEngine.Core.Geometry.Skinning;

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

    public int[] JointIndices { get; }

    public float[] Weights { get; }

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

                influences.Sort(static (a, b) => b.Weight.CompareTo(a.Weight));

                var kept = System.Math.Min(influences.Count, InfluencesPerVertex);
                var total = 0f;

                for (var i = 0; i < kept; i++)
                {
                    total += influences[i].Weight;
                }

                var slot = vertex * InfluencesPerVertex;

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
