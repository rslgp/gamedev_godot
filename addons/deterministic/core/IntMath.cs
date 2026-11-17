// Engine-free (no Godot dependency). Part of the deterministic addon.
using System;

namespace ReusableComponents.Deterministic;

/// <summary>Integer math with the same result on every host (no float in the result path).
/// Use for fixed-point simulations where a float rounding difference would desync.</summary>
public static class IntMath
{
    /// <summary>Exact floor of the square root. IEEE sqrt gives a close start, then the result is
    /// corrected to the exact integer floor, so it is host-independent.</summary>
    /// <param name="n">Value; &lt;= 0 returns 0.</param>
    /// <returns>Largest r with r*r &lt;= n.</returns>
    public static long ISqrt(long n)
    {
        if (n <= 0) return 0;
        long r = (long)Math.Sqrt(n);
        if (r > 3037000499L) r = 3037000499L; // floor(sqrt(long.MaxValue)): keeps r*r from overflowing
        while (r * r > n) r--;
        while (r < 3037000499L && (r + 1) * (r + 1) <= n) r++;
        return r;
    }

    /// <summary>Division rounding toward negative infinity, pure integer (e.g. world sub-pixel to tile index).</summary>
    /// <param name="a">Dividend.</param>
    /// <param name="b">Divisor, must be &gt; 0.</param>
    /// <returns>floor(a / b).</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="b"/> &lt;= 0.</exception>
    public static int FloorDiv(int a, int b)
    {
        if (b <= 0) throw new ArgumentOutOfRangeException(nameof(b), b, "must be > 0");
        return a >= 0 ? a / b : -(int)((-(long)a + b - 1) / b);
    }

    /// <summary>Scales the vector (<paramref name="dx"/>, <paramref name="dy"/>) to length <paramref name="len"/>
    /// using <see cref="ISqrt"/> (truncating division). Zero vector returns (0, 0).</summary>
    /// <param name="dx">X component.</param>
    /// <param name="dy">Y component.</param>
    /// <param name="len">Target length.</param>
    /// <returns>The scaled vector.</returns>
    public static (int X, int Y) Norm(long dx, long dy, int len)
    {
        long d = ISqrt(dx * dx + dy * dy);
        return d == 0 ? (0, 0) : ((int)(dx * len / d), (int)(dy * len / d));
    }
}
