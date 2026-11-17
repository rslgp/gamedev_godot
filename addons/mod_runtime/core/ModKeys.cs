// Engine-free (no Godot dependency). Part of the mod_runtime addon.
using System;
using System.Collections.Generic;
using System.IO;

namespace ReusableComponents.ModRuntime;

/// <summary>Stable mod identities used by the loader's include predicate and by a selection menu:
/// <c>lua:&lt;load-order line&gt;</c> and <c>dotnet:&lt;type full name&gt;</c>. The mod_select_menu addon
/// produces the same keys (checked by the library's integration test).</summary>
public static class ModKeys
{
    /// <summary>Prefix of Lua mod keys.</summary>
    public const string LuaPrefix = "lua:";
    /// <summary>Prefix of .NET mod keys.</summary>
    public const string DotNetPrefix = "dotnet:";

    /// <summary>Key of a Lua mod.</summary>
    /// <param name="loadOrderLine">The trimmed load-order line (file name).</param>
    /// <returns><c>lua:&lt;line&gt;</c>.</returns>
    public static string Lua(string loadOrderLine) => LuaPrefix + loadOrderLine;

    /// <summary>Key of a .NET mod.</summary>
    /// <param name="typeFullName"><see cref="Type.FullName"/> of the mod type.</param>
    /// <returns><c>dotnet:&lt;full name&gt;</c>.</returns>
    public static string DotNet(string typeFullName) => DotNetPrefix + typeFullName;
}

/// <summary>Load-order file rules: one mod file per line, <c>#</c> starts a comment, surrounding
/// whitespace and blank lines are ignored, a repeated entry is ignored (first wins).</summary>
public static class LoadOrder
{
    /// <summary>Default file name.</summary>
    public const string DefaultFileName = "load_order.txt";

    /// <summary>Parses load-order text.</summary>
    /// <param name="text">File content (any line endings); null = empty.</param>
    /// <returns>Entries in order, de-duplicated (ordinal).</returns>
    public static IReadOnlyList<string> Parse(string? text)
    {
        var list = new List<string>();
        if (string.IsNullOrEmpty(text)) return list;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Split('#')[0].Trim();
            if (line.Length == 0 || !seen.Add(line)) continue;
            list.Add(line);
        }
        return list;
    }

    /// <summary>Reads and parses <paramref name="fileName"/> inside <paramref name="directory"/>.</summary>
    /// <param name="directory">Folder; null/empty or missing = no entries.</param>
    /// <param name="fileName">Load-order file name.</param>
    /// <returns>Entries, or empty when the file does not exist.</returns>
    /// <exception cref="IOException">The file exists but cannot be read.</exception>
    public static IReadOnlyList<string> Read(string? directory, string fileName = DefaultFileName)
    {
        if (string.IsNullOrEmpty(directory)) return Array.Empty<string>();
        var path = Path.Combine(directory, fileName);
        return File.Exists(path) ? Parse(File.ReadAllText(path)) : Array.Empty<string>();
    }
}
