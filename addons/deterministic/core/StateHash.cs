// Engine-free (no Godot dependency). Part of the deterministic addon.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace ReusableComponents.Deterministic;

/// <summary>FNV-1a 64 primitives. A 64-bit value is mixed as 8 bytes, little-endian.</summary>
public static class Fnv1a64
{
    /// <summary>FNV-1a 64 offset basis.</summary>
    public const ulong OffsetBasis = 14695981039346656037UL;
    /// <summary>FNV-1a 64 prime.</summary>
    public const ulong Prime = 1099511628211UL;

    /// <summary>Mixes <paramref name="value"/> into <paramref name="hash"/> as 8 little-endian bytes.</summary>
    /// <param name="hash">Running hash.</param>
    /// <param name="value">Value to mix.</param>
    /// <returns>The new hash.</returns>
    public static ulong Mix(ulong hash, long value)
    {
        for (int i = 0; i < 8; i++) { hash ^= (byte)(value >> (i * 8)); hash *= Prime; }
        return hash;
    }

    /// <summary>Lower-case 16-digit hex (the <c>state_hash</c> wire format).</summary>
    /// <param name="hash">Hash value.</param>
    /// <returns>e.g. <c>"cbf29ce484222325"</c>.</returns>
    public static string Hex(ulong hash) => hash.ToString("x16", CultureInfo.InvariantCulture);
}

/// <summary>Order-sensitive FNV-1a 64 builder for a <c>state_hash</c>: one string that changes when any
/// mixed gameplay value changes, so parity, save/resume and replay checks become a string compare.
/// A mutable struct: keep it in a local and call Add on the local (do not pass it by value).
/// Allocation-free except <see cref="ToHex"/>. Not thread-safe.</summary>
public struct StateHash
{
    ulong _h;
    bool _started;

    /// <summary>Current hash value (the offset basis when nothing was added).</summary>
    public readonly ulong Value => _started ? _h : Fnv1a64.OffsetBasis;

    /// <summary>Mixes one integer (8 bytes little-endian).</summary>
    /// <param name="value">Value.</param>
    public void Add(long value) { _h = Fnv1a64.Mix(Value, value); _started = true; }

    /// <summary>Mixes a bool as 1 or 0.</summary>
    /// <param name="value">Value.</param>
    public void Add(bool value) => Add(value ? 1L : 0L);

    /// <summary>Mixes every char as one 8-byte value, WITHOUT a length prefix (the benchmark form for
    /// mod variable names). Prefer <see cref="AddString"/> for new code: it is unambiguous.</summary>
    /// <param name="text">Text; null mixes nothing.</param>
    public void AddChars(string? text) { if (text != null) foreach (char c in text) Add(c); }

    /// <summary>Mixes the length, then every char (unambiguous: "ab"+"c" differs from "a"+"bc").</summary>
    /// <param name="text">Text; null is mixed like an empty string.</param>
    public void AddString(string? text) { Add(text?.Length ?? 0); AddChars(text); }

    /// <summary>Mixes a string-keyed int map in ordinal key order: for each key, its chars
    /// (<see cref="AddChars"/>) then its value. Same layout as the shooter benchmark's mod-vars hash.
    /// Allocates a sorted key array (call once per hash, not per entity).</summary>
    /// <param name="map">Map; null mixes nothing.</param>
    public void AddOrdinalMap(IReadOnlyDictionary<string, int>? map)
    {
        if (map == null) return;
        var keys = new string[map.Count];
        int i = 0;
        foreach (var k in map.Keys) keys[i++] = k;
        Array.Sort(keys, StringComparer.Ordinal);
        foreach (var k in keys) { AddChars(k); Add(map[k]); }
    }

    /// <summary>The hash as 16 lower-case hex digits.</summary>
    /// <returns>Hex string.</returns>
    public readonly string ToHex() => Fnv1a64.Hex(Value);
}
