using SoftEngine.Core.Shading;
using System.Runtime.CompilerServices;

namespace SoftEngine.Core.Buffers;

/// <summary>
/// The per-pixel list of transparent fragments behind order-independent transparency, and the
/// resolve that blends them.
///
/// <para>
/// Sorting transparent <em>triangles</em> back to front is the cheap answer and it is wrong
/// wherever a triangle has no single depth to be sorted by. Two panes that intersect each other
/// have no correct order — whichever is drawn second is in front along its whole length, and the
/// seam where they cross is where the picture says so. A triangle sorted by the mean of its
/// vertices is also sorted wrongly against a large one it lies partly behind and partly in front
/// of, which is most of the times a pane of glass meets the floor it is standing on.
/// </para>
///
/// <para>
/// The order that is always correct is per pixel, so that is where it is decided. A transparent
/// fragment is not blended when it is shaded: it is depth-tested against the opaque z-buffer as
/// before, and then <em>stored</em>, with its colour, its alpha and its depth. Once every
/// transparent triangle has been through, each pixel holds the list of surfaces covering it, and
/// the resolve blends that list back to front. Nothing depends on the order the triangles arrived
/// in — which is what makes it order-independent, and is why the renderer stops sorting them at
/// all when this is on.
/// </para>
///
/// <para>
/// <b>Arenas, not one buffer.</b> Storage is divided the same way the fill is: one
/// <see cref="Arena"/> per screen tile, owned by the one worker that owns that tile. A pixel
/// belongs to exactly one tile, so no two threads ever touch the same arena and there is nothing
/// to lock, nothing to bump atomically, and no false sharing between workers — the same three
/// things owning a rectangle already bought the opaque fill.
/// </para>
///
/// <para>
/// <b>What it costs.</b> Only pixels a transparent surface actually covers get storage: an arena
/// hands out a block of <see cref="Capacity"/> fragment slots the first time a pixel is written
/// and remembers which pixels it has touched, so both the resolve and the reset walk the covered
/// pixels rather than the screen. Peak memory is therefore about
/// <c>covered pixels × Capacity × 20 bytes</c> — a full screen of glass at the default capacity
/// is roughly 150 MB at 720p, and a window of it is a few. <see cref="Capacity"/> is the knob for
/// scenes that want a different trade.
/// </para>
///
/// <para>
/// <b>When a pixel runs out of slots</b> the two farthest fragments are composited into one and
/// the list carries on. It is the only place this is approximate, and the error is put at the far
/// end deliberately: those are the fragments most of whose light the nearer ones have already
/// absorbed, so merging them changes the pixel least. <see cref="OverflowCount"/> reports how
/// often it happened, so "the picture is wrong and nothing said so" is not one of the outcomes.
/// </para>
/// </summary>
public sealed class FragmentBuffer
{
    /// <summary>
    /// Fragments a pixel keeps before it starts merging the farthest two. Eight covers four
    /// panes of double-sided glass, which is more than the scenes that motivate this have.
    /// </summary>
    public const int DefaultCapacity = 8;

    private Arena?[] _arenas = [];
    private int _arenaCount;
    private int _capacity = DefaultCapacity;
    private bool _recordProbeContexts;

    /// <summary>
    /// How many fragments one pixel keeps. Raising it costs memory in proportion and removes
    /// approximation; lowering it does the reverse. Takes effect at the next
    /// <see cref="Begin"/>, because an arena's blocks are sized by it.
    /// </summary>
    public int Capacity
    {
        get => _capacity;
        set => _capacity = System.Math.Clamp(value, 1, 64);
    }

    /// <summary>Fragments stored this frame, across every arena.</summary>
    public int FragmentCount { get; private set; }

    /// <summary>
    /// How many times a pixel was full and had to composite its two farthest fragments into
    /// one. Zero means the frame's transparency was resolved exactly.
    /// </summary>
    public int OverflowCount { get; private set; }

    /// <summary>Pixels holding at least one fragment this frame.</summary>
    public int CoveredPixelCount { get; private set; }

    /// <summary>
    /// Readies <paramref name="arenaCount"/> arenas for a frame and drops whatever the last one
    /// stored. Arenas themselves are kept: their blocks are the allocation worth reusing, and a
    /// frame that draws the same glass as the last one reuses all of it.
    ///
    /// <paramref name="recordProbeContexts"/> makes every fragment carry what was drawing it, so
    /// the resolve can attribute its blends to the triangle they came from. It is only on while a
    /// pixel is being probed, which is a frame rendered for the debugger rather than for the
    /// screen.
    /// </summary>
    public void Begin(int arenaCount, bool recordProbeContexts)
    {
        _recordProbeContexts = recordProbeContexts;

        if (_arenas.Length < arenaCount)
        {
            Array.Resize(ref _arenas, System.Math.Max(arenaCount, _arenas.Length * 2));
        }

        // Arenas past the new count are left allocated but unreachable: a viewport that shrank
        // and grew again finds them still there.
        for (var i = 0; i < arenaCount; i++)
        {
            _arenas[i]?.Reset();
        }

        _arenaCount = arenaCount;
        FragmentCount = 0;
        OverflowCount = 0;
        CoveredPixelCount = 0;
    }

    /// <summary>
    /// The arena covering one tile, created on the first frame that tile receives a fragment
    /// and resized when the tile's bounds change. Called from the tile's own worker, so no two
    /// threads ever reach the same slot.
    /// </summary>
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

    /// <summary>
    /// Blends every stored fragment into <paramref name="surface"/>, each pixel's back to front.
    ///
    /// Arenas cover disjoint rectangles, so they resolve in parallel for the same reason they
    /// fill in parallel. Within a pixel the blend is sequential and ordered, which is the whole
    /// point of having stored the fragments rather than blended them.
    /// </summary>
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

        // A probed frame resolves on one thread. The history is a list in draw order and the
        // probe's own writes are the thing being recorded, so it is worth a millisecond to have
        // them arrive in an order that means something.
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

    /// <summary>
    /// The fragments of one rectangle of the screen. Single-threaded by construction — one tile,
    /// one worker — so it grows its own storage with no coordination.
    /// </summary>
    public sealed class Arena
    {
        private int _xFrom;
        private int _yFrom;
        private int _width;
        private int _height;
        private int _capacity;

        // Per pixel of the rectangle: where its block of fragment slots starts, and how many of
        // them are filled. A pixel with no fragments has no block, and _block is meaningless
        // wherever _count is zero.
        private int[] _block = [];
        private byte[] _count = [];

        // The pixels that have a block, in the order they first got one. Both the reset and the
        // resolve walk this instead of the rectangle: a tile with one pane of glass across a
        // corner of it should cost a corner's worth of work, not a tile's.
        private int[] _touched = [];
        private int _touchedCount;

        // The slots themselves, in blocks of _capacity. Kept as parallel arrays rather than an
        // array of structs so the resolve's walk over colour and the insertion's walk over depth
        // each read one contiguous run.
        private float[] _rgb = [];
        private float[] _alpha = [];
        private int[] _depth = [];
        private FrameBuffer.ProbeContext[] _contexts = [];
        private bool _keepContexts;

        private int _used;

        public int FragmentCount { get; private set; }

        public int OverflowCount { get; private set; }

        public int CoveredPixelCount => _touchedCount;

        /// <summary>
        /// Points the arena at a rectangle of the screen, reallocating its per-pixel arrays when
        /// it is not the rectangle it was pointed at last. Edge tiles are smaller than the tile
        /// grid, and a resized viewport moves all of them.
        /// </summary>
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
                // Blocks are sized by the capacity, so a change to it invalidates every one of
                // them. Nothing is stored at this point in the frame, so dropping the pool is
                // free.
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
            // Only the pixels that were written: clearing the whole rectangle would make an
            // empty tile cost as much as a full one, every frame.
            for (var i = 0; i < _touchedCount; i++)
            {
                _count[_touched[i]] = 0;
            }

            _touchedCount = 0;
            _used = 0;
            FragmentCount = 0;
            OverflowCount = 0;
        }

        /// <summary>
        /// Stores one shaded transparent fragment. The depth test against opaque geometry has
        /// already happened — a fragment behind the z-buffer never reaches here.
        /// </summary>
        internal void Add(int x, int y, int depth, LinearColor color, float alpha, in FrameBuffer.ProbeContext context)
        {
            var local = (y - _yFrom) * _width + (x - _xFrom);

            if ((uint)local >= (uint)(_width * _height))
            {
                // A write outside the arena's own rectangle. The tiled fill clamps to the tile,
                // so this cannot happen from the rasterizer; dropping it rather than corrupting
                // a neighbouring pixel is the safe reading if it ever does.
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
                    // One slot, so the pair to composite is the stored fragment and the incoming
                    // one — there is no second slot to reach for, and reaching for it would be
                    // the next pixel's.
                    MergeInto(block, depth, color, alpha, context);
                    return;
                }

                // Full. Composite the two farthest into one and carry on with a slot free.
                MergeFarthest(block);
                count = _capacity - 1;
            }

            // Slots are kept farthest-first, so the resolve is a walk rather than a sort. At a
            // capacity of eight this is an insertion sort over eight elements, which beats
            // sorting the pixel later — the fragments are in cache now, and most pixels hold
            // one or two.
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

        /// <summary>
        /// Blends this arena's fragments into the surface, each pixel's farthest first.
        /// </summary>
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

        /// <summary>
        /// Composites slots 0 and 1 of a block — the two farthest — into slot 0, and shifts the
        /// rest down to leave the last slot free.
        ///
        /// <para>
        /// The merged fragment has to stand in for both, so it is what the pair would have left
        /// behind: <c>near over (far over dst)</c> expands to a single "over" whose alpha is
        /// <c>1 - (1-af)(1-an)</c> and whose colour is the pair's contributions divided by it.
        /// A surface behind both still sees exactly the light it would have seen; what is lost is
        /// the ability to put a third surface between them, which is precisely the fragment
        /// there was no room for.
        /// </para>
        /// </summary>
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

            // The merged fragment keeps the farther depth: it is the one that decides where the
            // pair sits against everything still to be inserted.

            for (var slot = block + 1; slot < block + _capacity - 1; slot++)
            {
                CopySlot(slot + 1, slot);
            }
        }

        /// <summary>
        /// Composites an incoming fragment with the one already in <paramref name="slot"/>,
        /// leaving the pair's combined "over" behind. The same algebra as
        /// <see cref="MergeFarthest"/>, for the one-slot case where the pair is the stored
        /// fragment and the arriving one rather than two stored ones.
        /// </summary>
        private void MergeInto(int slot, int depth, LinearColor color, float alpha, in FrameBuffer.ProbeContext context)
        {
            // Which of the two is in front decides the order they composite in, and the merged
            // fragment takes the farther depth — the same rule the two-slot merge follows.
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

            // The merged fragment is attributed to whichever of the pair is farther, which is the
            // one it took its depth from.
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

            // Doubling, floored at a block per pixel of a tile row: a tile that receives any
            // transparency at all usually receives a good deal of it.
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
