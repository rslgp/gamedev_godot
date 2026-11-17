// Engine-free (no Godot dependency). Part of the test_harness addon.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace ReusableComponents.TestHarness;

/// <summary>Parses user command-line args of the form <c>--name</c> and <c>--name=value</c> (the args after
/// <c>--</c> on a Godot command line; under Godot use <c>GodotHarness.Args()</c>). Matching is ordinal and
/// case-sensitive; the first occurrence wins. Same semantics as the benchmark hosts' <c>Arg()</c> helper:
/// a bare flag reads as <c>"1"</c>.</summary>
public sealed class HarnessArgs
{
    readonly string[] _args;

    /// <summary>Wraps an argument list.</summary>
    /// <param name="args">Raw args; null = none.</param>
    public HarnessArgs(IEnumerable<string>? args)
    {
        var l = new List<string>();
        if (args != null) foreach (var a in args) if (a != null) l.Add(a);
        _args = l.ToArray();
    }

    /// <summary>The raw arguments.</summary>
    public IReadOnlyList<string> Raw => _args;

    /// <summary>Value of <c>--name=value</c>, <c>"1"</c> for a bare <c>--name</c>, null when absent.</summary>
    /// <param name="name">Name without the leading dashes.</param>
    /// <returns>The value or null.</returns>
    public string? Get(string name)
    {
        string flag = "--" + name, prefix = flag + "=";
        foreach (var a in _args)
        {
            if (string.Equals(a, flag, StringComparison.Ordinal)) return "1";
            if (a.StartsWith(prefix, StringComparison.Ordinal)) return a[prefix.Length..];
        }
        return null;
    }

    /// <summary>True when <c>--name</c> or <c>--name=...</c> is present.</summary>
    /// <param name="name">Name without dashes.</param>
    /// <returns>Presence.</returns>
    public bool Has(string name) => Get(name) != null;

    /// <summary>Integer value (invariant culture) or <paramref name="fallback"/> when absent or malformed.</summary>
    /// <param name="name">Name without dashes.</param>
    /// <param name="fallback">Default.</param>
    /// <returns>The value.</returns>
    public int Int(string name, int fallback) =>
        int.TryParse(Get(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    /// <summary>Value, or <paramref name="fallback"/> when absent; a bare flag yields <paramref name="fallback"/> too.</summary>
    /// <param name="name">Name without dashes.</param>
    /// <param name="fallback">Default.</param>
    /// <returns>The value.</returns>
    public string String(string name, string fallback)
    {
        foreach (var a in _args)
            if (a.StartsWith("--" + name + "=", StringComparison.Ordinal)) return a[(name.Length + 3)..];
        return fallback;
    }
}
