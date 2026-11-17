// Engine-free (no Godot dependency): unit-testable headless, reusable by any host.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace ReusableComponents.ModSelect;

/// <summary>Which mods are enabled. A mod with no saved state is ENABLED (so no config = every mod on,
/// and a mod added later starts on). Persisted as a small INI-style text file, one
/// <c>"key"=true|false</c> per mod in a <c>[mods]</c> section, keys sorted ordinally so the
/// file is byte-stable. Pure data: changing it never touches a running game; the host reads it when it
/// (re)loads mods.</summary>
public sealed class ModSelection
{
    /// <summary>File format version written in the header.</summary>
    public const int FormatVersion = 1;
    const string Section = "[mods]";
    readonly SortedDictionary<string, bool> _state = new(StringComparer.Ordinal);

    /// <summary>True if <paramref name="key"/> is enabled (unknown keys are enabled).
    /// Pass this method as the loader predicate.</summary>
    public bool IsEnabled(string key) => !_state.TryGetValue(key, out var on) || on;

    /// <summary>Sets one mod on or off.</summary>
    public void Set(string key, bool enabled)
    {
        if (string.IsNullOrEmpty(key) || key.IndexOfAny(['=', '\n', '\r', '[', ']', ';', '"']) >= 0 || key.Trim() != key)
            throw new ArgumentException($"invalid mod key '{key}'", nameof(key));
        _state[key] = enabled;
    }

    /// <summary>Explicit states (sorted ordinally). Keys absent here are enabled.</summary>
    public IReadOnlyDictionary<string, bool> States => _state;

    /// <summary>Deep copy (edit a copy in a menu, commit by saving).</summary>
    public ModSelection Clone()
    {
        var c = new ModSelection();
        foreach (var kv in _state) c._state[kv.Key] = kv.Value;
        return c;
    }

    /// <summary>Records every catalog entry explicitly (readable file); unknown keys already saved are kept.</summary>
    public void CaptureAll(IEnumerable<ModEntry> entries)
    {
        foreach (var e in entries) _state[e.Key] = IsEnabled(e.Key);
    }

    /// <summary>Serializes to text (invariant, '\n' line endings, sorted keys).</summary>
    public string Serialize()
    {
        var sb = new StringBuilder();
        sb.Append("; mod selection, format ").Append(FormatVersion.ToString(CultureInfo.InvariantCulture)).Append('\n');
        sb.Append(Section).Append('\n');
        foreach (var kv in _state) sb.Append('"').Append(kv.Key).Append("\"=").Append(kv.Value ? "true" : "false").Append('\n');
        return sb.ToString();
    }

    /// <summary>Parses text written by <see cref="Serialize"/>. Never throws: malformed lines are
    /// skipped and reported in <paramref name="errors"/> (a bad line leaves that mod enabled).</summary>
    public static ModSelection Parse(string text, List<string>? errors = null)
    {
        var sel = new ModSelection();
        bool inSection = false;
        int n = 0;
        foreach (var raw in text.Split('\n'))
        {
            n++;
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
            if (line[0] == '[') { inSection = line == Section; continue; }
            if (!inSection) continue;
            int eq = line.LastIndexOf('=');
            if (eq <= 0) { errors?.Add($"line {n}: expected key=value"); continue; }
            var key = line[..eq].Trim();
            if (key.Length >= 2 && key[0] == '"' && key[^1] == '"') key = key[1..^1];
            var val = line[(eq + 1)..].Trim();
            bool? on = val == "true" ? true : val == "false" ? false : null;
            if (on is null) { errors?.Add($"line {n}: value must be true or false"); continue; }
            try { sel.Set(key, on.Value); }
            catch (ArgumentException ex) { errors?.Add($"line {n}: {ex.Message}"); }
        }
        return sel;
    }

    /// <summary>Loads from <paramref name="path"/>. Missing file = empty selection (all enabled).
    /// Unreadable file = all enabled plus an entry in <paramref name="errors"/>; never throws.</summary>
    public static ModSelection Load(string path, List<string>? errors = null)
    {
        try { return File.Exists(path) ? Parse(File.ReadAllText(path, Encoding.UTF8), errors) : new ModSelection(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            errors?.Add($"{Path.GetFileName(path)}: {ex.Message}");
            return new ModSelection();
        }
    }

    /// <summary>Saves atomically (temp file + replace). Returns null on success, else the error text.</summary>
    public string? Save(string path)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, Serialize(), new UTF8Encoding(false));
            File.Move(tmp, path, overwrite: true);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return ex.Message;
        }
    }

    /// <summary>Counts (enabled, total) over <paramref name="entries"/>.</summary>
    public (int Enabled, int Total) Count(IEnumerable<ModEntry> entries)
    {
        int on = 0, all = 0;
        foreach (var e in entries) { all++; if (IsEnabled(e.Key)) on++; }
        return (on, all);
    }
}
