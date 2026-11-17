// Engine-free (no Godot dependency). Part of the mod_runtime_lua addon (requires MoonSharp 2.0.0 + mod_runtime).
using System;
using System.Collections.Generic;
using System.Text;
using MoonSharp.Interpreter;

namespace ReusableComponents.ModRuntime;

/// <summary>Sandbox settings for Lua mods (MoonSharp). Every mod gets its OWN <see cref="Script"/>, so its
/// library tables are private: a mod that sets <c>string.format = nil</c> breaks only itself.</summary>
public sealed class LuaSandboxOptions
{
    /// <summary>The benchmark whitelist of globals kept from MoonSharp's soft sandbox; every other global is set to nil.</summary>
    public static readonly IReadOnlyList<string> DefaultAllowedGlobals = ["math", "string", "table", "pairs", "ipairs", "next",
        "tostring", "tonumber", "type", "select", "unpack", "pcall", "error", "setmetatable", "print"];

    /// <summary>Globals a mod may see (ordinal names). Default <see cref="DefaultAllowedGlobals"/>.</summary>
    public IReadOnlyList<string> AllowedGlobals { get; init; } = DefaultAllowedGlobals;

    /// <summary>True (default): <c>math.random</c> and <c>math.randomseed</c> are removed. They are backed by a
    /// non-deterministic generator, so a mod calling them would desync replays, saves and cross-engine parity.
    /// Expose a deterministic random query owned by the simulation instead (see docs/INTEGRATION.md).</summary>
    public bool RemoveMathRandom { get; init; } = true;

    /// <summary>Maximum length of a <c>string.rep</c> result (default 1 MiB chars); larger requests raise a
    /// Lua error instead of exhausting memory. 0 = keep MoonSharp's original <c>string.rep</c>.</summary>
    public int MaxStringRepLength { get; init; } = 1 << 20;

    /// <summary>Default settings.</summary>
    public static LuaSandboxOptions Default { get; } = new();
}

/// <summary>Creates sandboxed MoonSharp scripts.</summary>
public static class LuaSandbox
{
    /// <summary>A new script with the soft-sandbox modules, globals outside the whitelist removed,
    /// <c>unpack</c> aliased to <c>table.unpack</c> when missing, and <c>print</c> redirected to <paramref name="print"/>.</summary>
    /// <param name="options">Sandbox settings; null = <see cref="LuaSandboxOptions.Default"/>.</param>
    /// <param name="print">Receives everything the mod prints (never stdout). Null = discard.</param>
    /// <returns>The script.</returns>
    public static Script Create(LuaSandboxOptions? options = null, Action<string>? print = null)
    {
        var o = options ?? LuaSandboxOptions.Default;
        var script = new Script(CoreModules.Preset_SoftSandbox);
        var allowed = new HashSet<string>(o.AllowedGlobals, StringComparer.Ordinal);
        var remove = new List<string>();
        foreach (var k in script.Globals.Keys)
            if (k.CastToString() is { } name && !allowed.Contains(name)) remove.Add(name);
        foreach (var name in remove) script.Globals[name] = DynValue.Nil;
        if (allowed.Contains("unpack") && script.Globals.Get("unpack").IsNil() && script.Globals.Get("table").Type == DataType.Table)
            script.Globals["unpack"] = script.Globals.Get("table").Table.Get("unpack");
        if (o.RemoveMathRandom && script.Globals.Get("math").Type == DataType.Table)
        {
            var math = script.Globals.Get("math").Table;
            math.Set("random", DynValue.Nil);
            math.Set("randomseed", DynValue.Nil);
        }
        if (o.MaxStringRepLength > 0 && script.Globals.Get("string").Type == DataType.Table)
        {
            int cap = o.MaxStringRepLength;
            script.Globals.Get("string").Table.Set("rep", DynValue.NewCallback((_, a) => Rep(a, cap)));
        }
        script.Options.DebugPrint = print ?? (_ => { });
        return script;
    }

    static DynValue Rep(CallbackArguments a, int cap)
    {
        var s = a[0].Type is DataType.String or DataType.Number ? a[0].CastToString() : throw new ScriptRuntimeException("bad argument #1 to 'rep' (string expected)");
        var nd = a[1].CastToNumber() ?? throw new ScriptRuntimeException("bad argument #2 to 'rep' (number expected)");
        var sep = a.Count > 2 && !a[2].IsNil() ? a[2].CastToString() ?? "" : "";
        if (double.IsNaN(nd) || nd < 1) return DynValue.NewString("");
        double total = (double)s.Length * Math.Floor(nd) + (double)sep.Length * (Math.Floor(nd) - 1);
        if (total > cap) throw new ScriptRuntimeException($"string.rep result too large ({total:0} > {cap})");
        long n = (long)Math.Floor(nd);
        var sb = new StringBuilder((int)total);
        for (long i = 0; i < n; i++) { if (i > 0) sb.Append(sep); sb.Append(s); }
        return DynValue.NewString(sb.ToString());
    }
}
