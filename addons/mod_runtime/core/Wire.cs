// Engine-free (no Godot dependency). Part of the mod_runtime addon.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace ReusableComponents.ModRuntime;

/// <summary>Argument validation shared by every mod runtime (.NET API implementations and the Lua bridge
/// call the SAME methods, so a mod behaves identically in both). A rejected argument throws
/// <see cref="ArgumentException"/>; the host counts that hook call as one mod error.
/// Commands travel as <c>string[]</c> with invariant-culture integers.</summary>
public static class Wire
{
    /// <summary>Default maximum length of a <see cref="Name"/> (32, the shooter benchmark's rule).</summary>
    public const int DefaultMaxNameLength = 32;

    /// <summary>Validates an identifier-like name: non-empty, ASCII letters/digits/underscore only.</summary>
    /// <param name="name">Candidate.</param>
    /// <param name="maxLength">Maximum length (use <see cref="int.MaxValue"/> for no cap).</param>
    /// <returns>The same string.</returns>
    /// <exception cref="ArgumentException">Empty, too long, or a forbidden character.</exception>
    public static string Name(string? name, int maxLength = DefaultMaxNameLength)
    {
        if (string.IsNullOrEmpty(name) || name.Length > maxLength) throw new ArgumentException($"bad name '{name}'");
        foreach (char ch in name)
            if (!(char.IsAsciiLetterOrDigit(ch) || ch == '_')) throw new ArgumentException($"bad name '{name}'");
        return name;
    }

    /// <summary>Sanitizes display text: every control character (incl. newline) becomes a space, so a
    /// mod can never inject line breaks into logs or a line protocol. Null becomes "".</summary>
    /// <param name="text">Raw text.</param>
    /// <returns>Sanitized text, same length.</returns>
    public static string Text(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var a = text.ToCharArray();
        for (int i = 0; i < a.Length; i++) if (char.IsControl(a[i])) a[i] = ' ';
        return new string(a);
    }

    /// <summary>Invariant-culture int (the command wire format).</summary>
    /// <param name="value">Value.</param>
    /// <returns>e.g. "-12".</returns>
    public static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Validates a script number as an int32 (floored) and formats it for the wire.</summary>
    /// <param name="value">Number from a script.</param>
    /// <returns>Invariant integer string.</returns>
    /// <exception cref="ArgumentException">NaN, infinite, or outside the int32 range.</exception>
    public static string Int(double value)
    {
        if (double.IsNaN(value) || value < int.MinValue || value > int.MaxValue) throw new ArgumentException("expected integer");
        return ((int)Math.Floor(value)).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Reads argument <paramref name="index"/> of a command as an int (host side, when applying).
    /// Missing or malformed values read as 0 (never throws), matching the benchmark sims.</summary>
    /// <param name="command">Command (element 0 is the verb).</param>
    /// <param name="index">Argument position.</param>
    /// <returns>The floored value or 0.</returns>
    public static int ParseInt(IReadOnlyList<string> command, int index) =>
        command != null && index >= 0 && index < command.Count
        && double.TryParse(command[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
        && d >= int.MinValue && d <= int.MaxValue ? (int)Math.Floor(d) : 0;
}
