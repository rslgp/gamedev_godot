using Godot;
using FileAccess = Godot.FileAccess;

namespace ReusableComponents.SaveStore;

/// <summary>Small persistent profile (settings, best scores, meta-progression) on Godot's <see cref="ConfigFile"/>.
/// A missing or corrupt file loads as DEFAULTS (every getter returns its fallback) and reports why in
/// <see cref="LoadError"/>; it never throws and never crashes the game. Engine thread only.</summary>
public sealed class ProfileStore
{
    ConfigFile _cfg = new();

    /// <summary>Creates a store for <paramref name="path"/> (nothing is read until <see cref="Load"/>).</summary>
    /// <param name="path"><c>user://</c> path, e.g. <c>user://profile.cfg</c>.</param>
    public ProfileStore(string path) { Path = path; }

    /// <summary>File path.</summary>
    public string Path { get; }

    /// <summary>Result of the last <see cref="Load"/>: <see cref="Error.Ok"/>, <see cref="Error.FileNotFound"/>
    /// (first run) or a parse/read error (corrupt file, defaults in use).</summary>
    public Error LoadError { get; private set; } = Error.Ok;

    /// <summary>Reads the file. Missing or corrupt = empty profile (defaults).</summary>
    /// <returns>True when the file was read.</returns>
    public bool Load()
    {
        var cfg = new ConfigFile();
        LoadError = FileAccess.FileExists(Path) ? cfg.Load(Path) : Error.FileNotFound;
        _cfg = LoadError == Error.Ok ? cfg : new ConfigFile();
        return LoadError == Error.Ok;
    }

    /// <summary>Writes the profile.</summary>
    /// <returns><see cref="Error.Ok"/> or the write error (never throws).</returns>
    public Error Save() => _cfg.Save(Path);

    /// <summary>Integer value or <paramref name="fallback"/> when absent or not an integer.</summary>
    /// <param name="section">Section.</param>
    /// <param name="key">Key.</param>
    /// <param name="fallback">Default.</param>
    /// <returns>The value.</returns>
    public long GetInt(string section, string key, long fallback = 0)
    {
        var v = _cfg.GetValue(section, key, fallback);
        return v.VariantType == Variant.Type.Int ? v.AsInt64() : fallback;
    }

    /// <summary>String value or <paramref name="fallback"/>.</summary>
    /// <param name="section">Section.</param>
    /// <param name="key">Key.</param>
    /// <param name="fallback">Default.</param>
    /// <returns>The value.</returns>
    public string GetString(string section, string key, string fallback = "")
    {
        var v = _cfg.GetValue(section, key, fallback);
        return v.VariantType == Variant.Type.String ? v.AsString() : fallback;
    }

    /// <summary>Bool value or <paramref name="fallback"/>.</summary>
    /// <param name="section">Section.</param>
    /// <param name="key">Key.</param>
    /// <param name="fallback">Default.</param>
    /// <returns>The value.</returns>
    public bool GetBool(string section, string key, bool fallback = false)
    {
        var v = _cfg.GetValue(section, key, fallback);
        return v.VariantType == Variant.Type.Bool ? v.AsBool() : fallback;
    }

    /// <summary>Sets a value (call <see cref="Save"/> to persist).</summary>
    /// <param name="section">Section.</param>
    /// <param name="key">Key.</param>
    /// <param name="value">Any Variant-compatible value.</param>
    public void Set(string section, string key, Variant value) => _cfg.SetValue(section, key, value);

    /// <summary>Raises a stored integer to <paramref name="candidate"/> if larger (best score, best wave).</summary>
    /// <param name="section">Section.</param>
    /// <param name="key">Key.</param>
    /// <param name="candidate">New value.</param>
    /// <returns>True when the stored value changed.</returns>
    public bool SetMax(string section, string key, long candidate)
    {
        if (candidate <= GetInt(section, key, long.MinValue) && _cfg.HasSectionKey(section, key)) return false;
        _cfg.SetValue(section, key, candidate);
        return true;
    }
}
