// Engine-free (no Godot dependency). Part of the deterministic addon.
using System;

namespace ReusableComponents.Deterministic;

/// <summary>Seeded xorshift32 random stream (shifts 13/17/5). Produces the same sequence on every host,
/// runtime and engine, unlike <see cref="System.Random"/>, whose algorithm is runtime-defined.
/// A value type: copy it to fork a stream, store <see cref="State"/> in a save to resume it exactly.
/// Not thread-safe; not cryptographic.</summary>
public struct XorShift32
{
    /// <summary>Seed used when 0 is passed (xorshift has an all-zero fixed point).</summary>
    public const uint ZeroSeedReplacement = 0x9E3779B9u;

    /// <summary>Full generator state. Save it and assign it back to resume the exact sequence.
    /// Assigning 0 is coerced to <see cref="ZeroSeedReplacement"/> on the next draw.</summary>
    public uint State;

    /// <summary>Creates a stream.</summary>
    /// <param name="seed">Any value; 0 is replaced by <see cref="ZeroSeedReplacement"/>.</param>
    public XorShift32(uint seed) { State = seed == 0 ? ZeroSeedReplacement : seed; }

    /// <summary>Advances the stream and returns the next 32-bit value (never 0).</summary>
    /// <returns>The new state.</returns>
    public uint NextUInt()
    {
        if (State == 0) State = ZeroSeedReplacement;
        State ^= State << 13; State ^= State >> 17; State ^= State << 5;
        return State;
    }

    /// <summary>Uniform-ish integer in [0, <paramref name="n"/>) by modulo (the benchmark formula;
    /// modulo bias is below 1e-9 relative for n &lt; 2^16).</summary>
    /// <param name="n">Exclusive upper bound, must be &gt; 0.</param>
    /// <returns>Value in [0, n).</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="n"/> &lt;= 0.</exception>
    public int Range(int n)
    {
        if (n <= 0) throw new ArgumentOutOfRangeException(nameof(n), n, "must be > 0");
        return (int)(NextUInt() % (uint)n);
    }

    /// <summary>Integer in [<paramref name="min"/>, <paramref name="maxExclusive"/>).</summary>
    /// <param name="min">Inclusive lower bound.</param>
    /// <param name="maxExclusive">Exclusive upper bound, must be &gt; <paramref name="min"/>.</param>
    /// <returns>Value in range.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Empty or overflowing range.</exception>
    public int Range(int min, int maxExclusive)
    {
        long span = (long)maxExclusive - min;
        if (span <= 0 || span > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(maxExclusive), "range must be non-empty and < 2^31");
        return min + Range((int)span);
    }

    /// <summary>True with probability <paramref name="percent"/>/100 (one draw).</summary>
    /// <param name="percent">0..100; values outside are clamped by the comparison.</param>
    /// <returns>Whether the roll succeeded.</returns>
    public bool Chance(int percent) => Range(100) < percent;
}
