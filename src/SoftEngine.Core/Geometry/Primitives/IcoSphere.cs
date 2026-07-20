using System.Numerics;

namespace SoftEngine.Core.Geometry.Primitives;

public sealed class IcoSphere : Mesh
{
    private IcoSphere(Sphere sphere)
        : base([.. sphere.Points], [.. sphere.Faces])
    {
    }

    public IcoSphere(int recursionLevel) 
        : this(new Sphere(recursionLevel))
    {
    }

    public sealed class Sphere
    {
        private int _index = 0;
        private readonly Dictionary<long, int> _middlePointIndexCache = [];

        public Sphere(int recursionLevel)
        {
            Create(recursionLevel);
        }

        public List<Vector3> Points { get; set; } = [];

        public List<Triangle> Faces { get; set; } = [];

        /// <summary>
        /// Return index of point in the middle of p1 and p2
        /// </summary>
        /// <returns>The index.</returns>
        private int GetMiddlePoint(int p1, int p2)
        {
            // first check if we have it already
            var firstIsSmaller = p1 < p2;
            long smallerIndex = firstIsSmaller ? p1 : p2;
            long greaterIndex = firstIsSmaller ? p2 : p1;
            var key = (smallerIndex << 32) + greaterIndex;

            if (_middlePointIndexCache.TryGetValue(key, out var ret))
            {
                return ret;
            }

            // not in cache, calculate it
            var point1 = Points[p1];
            var point2 = Points[p2];
            var middle = new Vector3(
                (point1.X + point2.X) / 2f,
                (point1.Y + point2.Y) / 2f,
                (point1.Z + point2.Z) / 2f);

            // add vertex makes sure point is on unit Sphere
            var i = AddVertex(middle);

            // store it, return index
            _middlePointIndexCache.Add(key, i);

            return i;
        }

        private int AddVertex(Vector3 p)
        {
            Points.Add(Vector3.Normalize(p));
            return _index++;
        }

        private void Create(int recursionLevel)
        {
            // create 12 vertices of a icosahedron
            float t = (1f + (float)System.Math.Sqrt(5f)) / 2f;

            AddVertex(new Vector3(-1, t, 0));
            AddVertex(new Vector3(1, t, 0));
            AddVertex(new Vector3(-1, -t, 0));
            AddVertex(new Vector3(1, -t, 0));

            AddVertex(new Vector3(0, -1, t));
            AddVertex(new Vector3(0, 1, t));
            AddVertex(new Vector3(0, -1, -t));
            AddVertex(new Vector3(0, 1, -t));

            AddVertex(new Vector3(t, 0, -1));
            AddVertex(new Vector3(t, 0, 1));
            AddVertex(new Vector3(-t, 0, -1));
            AddVertex(new Vector3(-t, 0, 1));

            // 5 faces around point 0
            Faces.Add(new Triangle(0, 11, 5));
            Faces.Add(new Triangle(0, 5, 1));
            Faces.Add(new Triangle(0, 1, 7));
            Faces.Add(new Triangle(0, 7, 10));
            Faces.Add(new Triangle(0, 10, 11));

            // 5 adjacent faces 
            Faces.Add(new Triangle(1, 5, 9));
            Faces.Add(new Triangle(5, 11, 4));
            Faces.Add(new Triangle(11, 10, 2));
            Faces.Add(new Triangle(10, 7, 6));
            Faces.Add(new Triangle(7, 1, 8));

            // 5 faces around point 3
            Faces.Add(new Triangle(3, 9, 4));
            Faces.Add(new Triangle(3, 4, 2));
            Faces.Add(new Triangle(3, 2, 6));
            Faces.Add(new Triangle(3, 6, 8));
            Faces.Add(new Triangle(3, 8, 9));

            // 5 adjacent faces 
            Faces.Add(new Triangle(4, 9, 5));
            Faces.Add(new Triangle(2, 4, 11));
            Faces.Add(new Triangle(6, 2, 10));
            Faces.Add(new Triangle(8, 6, 7));
            Faces.Add(new Triangle(9, 8, 1));

            // refine triangles
            for (int r = 0; r < recursionLevel; r++)
            {
                var faces2 = new List<Triangle>();

                foreach (var tri in Faces)
                {
                    // replace triangle by 4 triangles
                    int a = GetMiddlePoint(tri.I0, tri.I1);
                    int b = GetMiddlePoint(tri.I1, tri.I2);
                    int c = GetMiddlePoint(tri.I2, tri.I0);

                    faces2.Add(new Triangle(tri.I0, a, c));
                    faces2.Add(new Triangle(tri.I1, b, a));
                    faces2.Add(new Triangle(tri.I2, c, b));
                    faces2.Add(new Triangle(a, b, c));
                }

                Faces = faces2;
            }
        }
    }
}
