using SoftEngine.Core.Shading;
using System.Runtime.CompilerServices;

namespace SoftEngine.Core.Buffers;

public sealed class FragmentBuffer
{
    public const int DefaultCapacity = 8;

    private Arena?[] _arenas = [];
    private int _arenaCount;
    private int _capacity = DefaultCapacity;
    private bool _recordProbeContexts;

    public int Capacity
    {
        get => _capacity;
        set => _capacity = System.Math.Clamp(value, 1, 64);
    }

    public int FragmentCount { get; private set; }

    public int OverflowCount { get; private set; }

    public int CoveredPixelCount { get; private set; }

    public void Begin(int arenaCount, bool recordProbeContexts)
    {
        _recordProbeContexts = recordProbeContexts;

        if (_arenas.Length < arenaCount)
        {
            Array.Resize(ref _arenas, System.Math.Max(arenaCount, _arenas.Length * 2));
        }

        for (var i = 0; i < arenaCount; i++)
        {
            _arenas[i]?.Reset();
        }

        _arenaCount = arenaCount;
        FragmentCount = 0;
        OverflowCount = 0;
        CoveredPixelCount = 0;
    }

    public Arena ArenaFor(int index, int xFrom, int yFrom, int xTo, int yTo)
    {
        var arena = _arenas[index];

        if (arena is null)
        {
            arena = new Arena();
            _arenas[index] = arena;
        }

        arena.Cover(xFrom, yFrom, xTo, yTo, _capacity, _recordProbeContexts);
        return arena;
    }

    public void Resolve(FrameBuffer surface)
    {
        if (_arenaCount == 0)
        {
            return;
        }

        var fragments = 0;
        var overflow = 0;
        var covered = 0;

        for (var i = 0; i < _arenaCount; i++)
        {
            if (_arenas[i] is { } arena)
            {
                fragments += arena.FragmentCount;
                overflow += arena.OverflowCount;
                covered += arena.CoveredPixelCount;
            }
        }

        FragmentCount = fragments;
        OverflowCount = overflow;
        CoveredPixelCount = covered;

        if (covered == 0)
        {
            return;
        }

        if (_arenaCount == 1 || surface.IsProbing || Environment.ProcessorCount <= 1)
        {
            for (var i = 0; i < _arenaCount; i++)
            {
                _arenas[i]?.Resolve(surface);
            }

            return;
        }

        Parallel.For(0, _arenaCount, i => _arenas[i]?.Resolve(surface));
    }

    public sealed class Arena
    {
        private int _xFrom;
        private int _yFrom;
        private int _width;
        private int _height;
        private int _capacity;

        private int[] _block = [];
        private byte[] _count = [];

        private int[] _touched = [];
        private int _touchedCount;

        private float[] _rgb = [];
        private float[] _alpha = [];
        private int[] _depth = [];
        private FrameBuffer.ProbeContext[] _contexts = [];
        private bool _keepContexts;

        private int _used;

        public int FragmentCount { get; private set; }

        public int OverflowCount { get; private set; }

        public int CoveredPixelCount => _touchedCount;

        internal void Cover(int xFrom, int yFrom, int xTo, int yTo, int capacity, bool keepContexts)
        {
            var width = System.Math.Max(xTo - xFrom, 0);
            var height = System.Math.Max(yTo - yFrom, 0);
            var pixels = width * height;

            _xFrom = xFrom;
            _yFrom = yFrom;
            _width = width;
            _height = height;
            _keepContexts = keepContexts;

            if (_capacity != capacity)
            {
                _capacity = capacity;
                _used = 0;
                _rgb = [];
                _alpha = [];
                _depth = [];
                _contexts = [];
            }

            if (_block.Length < pixels)
            {
                _block = new int[pixels];
                _count = new byte[pixels];
                _touched = new int[pixels];
                _touchedCount = 0;
            }
        }

        internal void Reset()
        {
            for (var i = 0; i < _touchedCount; i++)
            {
                _count[_touched[i]] = 0;
            }

            _touchedCount = 0;
            _used = 0;
            FragmentCount = 0;
            OverflowCount = 0;
        }

        internal void Add(int x, int y, int depth, LinearColor color, float alpha, in FrameBuffer.ProbeContext context)
        {
            var local = (y - _yFrom) * _width + (x - _xFrom);

            if ((uint)local >= (uint)(_width * _height))
            {
                return;
            }

            int count = _count[local];
            int block;

            if (count == 0)
            {
                block = _used;
                _used += _capacity;
                EnsureSlots(_used);

                _block[local] = block;
                _touched[_touchedCount++] = local;
            }
            else
            {
                block = _block[local];
            }

            if (count == _capacity)
            {
                OverflowCount++;

                if (_capacity == 1)
                {
                    MergeInto(block, depth, color, alpha, context);
                    return;
                }

                MergeFarthest(block);
                count = _capacity - 1;
            }

            var slot = count;
            while (slot > 0 && _depth[block + slot - 1] < depth)
            {
                CopySlot(block + slot - 1, block + slot);
                slot--;
            }

            WriteSlot(block + slot, depth, color, alpha, context);

            _count[local] = (byte)(count + 1);
            FragmentCount++;
        }

        internal void Resolve(FrameBuffer surface)
        {
            var surfaceWidth = surface.Width;

            for (var i = 0; i < _touchedCount; i++)
            {
                var local = _touched[i];
                int count = _count[local];

                if (count == 0)
                {
                    continue;
                }

                var x = _xFrom + local % _width;
                var y = _yFrom + local / _width;
                var index = x + y * surfaceWidth;
                var block = _block[local];

                for (var slot = block; slot < block + count; slot++)
                {
                    var rgb = slot * 3;

                    surface.BlendStoredFragment(
                        index,
                        _depth[slot],
                        new LinearColor(_rgb[rgb], _rgb[rgb + 1], _rgb[rgb + 2]),
                        _alpha[slot],
                        _keepContexts ? _contexts[slot] : default);
                }
            }
        }

        private void MergeFarthest(int block)
        {
            var far = block;
            var near = block + 1;

            var af = _alpha[far];
            var an = _alpha[near];

            var transmitted = (1f - af) * (1f - an);
            var merged = 1f - transmitted;

            if (merged > 1e-6f)
            {
                var fi = far * 3;
                var ni = near * 3;
                var weightFar = af * (1f - an);
                var inverse = 1f / merged;

                _rgb[fi] = (_rgb[fi] * weightFar + _rgb[ni] * an) * inverse;
                _rgb[fi + 1] = (_rgb[fi + 1] * weightFar + _rgb[ni + 1] * an) * inverse;
                _rgb[fi + 2] = (_rgb[fi + 2] * weightFar + _rgb[ni + 2] * an) * inverse;
            }

            _alpha[far] = merged;

            for (var slot = block + 1; slot < block + _capacity - 1; slot++)
            {
                CopySlot(slot + 1, slot);
            }
        }

        private void MergeInto(int slot, int depth, LinearColor color, float alpha, in FrameBuffer.ProbeContext context)
        {
            var stored = _depth[slot];
            var storedIsFar = stored >= depth;

            var rgb = slot * 3;
            var storedColor = new LinearColor(_rgb[rgb], _rgb[rgb + 1], _rgb[rgb + 2]);
            var storedAlpha = _alpha[slot];
            var storedContext = _keepContexts ? _contexts[slot] : default;

            var (farColor, af, nearColor, an) = storedIsFar
                ? (storedColor, storedAlpha, color, alpha)
                : (color, alpha, storedColor, storedAlpha);

            var merged = 1f - (1f - af) * (1f - an);
            var result = farColor;

            if (merged > 1e-6f)
            {
                var weightFar = af * (1f - an);
                var inverse = 1f / merged;

                result = new LinearColor(
                    (farColor.R * weightFar + nearColor.R * an) * inverse,
                    (farColor.G * weightFar + nearColor.G * an) * inverse,
                    (farColor.B * weightFar + nearColor.B * an) * inverse);
            }

            WriteSlot(slot, storedIsFar ? stored : depth, result, merged, storedIsFar ? storedContext : context);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CopySlot(int from, int to)
        {
            var f = from * 3;
            var t = to * 3;

            _rgb[t] = _rgb[f];
            _rgb[t + 1] = _rgb[f + 1];
            _rgb[t + 2] = _rgb[f + 2];
            _alpha[to] = _alpha[from];
            _depth[to] = _depth[from];

            if (_keepContexts)
            {
                _contexts[to] = _contexts[from];
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteSlot(int slot, int depth, LinearColor color, float alpha, in FrameBuffer.ProbeContext context)
        {
            var rgb = slot * 3;

            _rgb[rgb] = color.R;
            _rgb[rgb + 1] = color.G;
            _rgb[rgb + 2] = color.B;
            _alpha[slot] = alpha;
            _depth[slot] = depth;

            if (_keepContexts)
            {
                _contexts[slot] = context;
            }
        }

        private void EnsureSlots(int slots)
        {
            if (_depth.Length >= slots)
            {
                if (_keepContexts && _contexts.Length < slots)
                {
                    Array.Resize(ref _contexts, _depth.Length);
                }

                return;
            }

            var capacity = System.Math.Max(slots, System.Math.Max(_depth.Length * 2, _capacity * 32));

            Array.Resize(ref _rgb, capacity * 3);
            Array.Resize(ref _alpha, capacity);
            Array.Resize(ref _depth, capacity);

            if (_keepContexts)
            {
                Array.Resize(ref _contexts, capacity);
            }
        }
    }
}
