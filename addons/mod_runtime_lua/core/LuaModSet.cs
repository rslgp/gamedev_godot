// Engine-free (no Godot dependency). Part of the mod_runtime_lua addon (requires MoonSharp 2.0.0 + mod_runtime).
using System;
using System.Collections.Generic;
using System.IO;
using MoonSharp.Interpreter;

namespace ReusableComponents.ModRuntime;

/// <summary>Lua mods listed in a load-order file, each run in its own sandboxed MoonSharp script
/// (<see cref="LuaSandbox"/>). A mod file must <c>return</c> a table; its optional <c>name</c> field is the display
/// name and its functions are hooks called as <c>hook(api, ev)</c>. Syntax errors, a non-table result, a
/// missing file and failing hook calls are recorded in the error log; other mods keep running.
/// Not thread-safe. No instruction or memory limit: an infinite loop in a mod blocks the caller.</summary>
public sealed class LuaModSet
{
    sealed class Mod
    {
        public required Script Script;
        public required Table Table;
        public required string Name;
        public required HookGuard Guard;
        public required List<(string Name, DynValue Fn)> Api;
    }

    readonly List<Mod> _mods = new();
    readonly List<string> _errors;
    ModCommandSink? _sink;

    /// <summary>Loads the mods (in load order).</summary>
    /// <param name="directory">Folder with the load-order file and the scripts; null/empty/missing = no mods.</param>
    /// <param name="errors">Shared error log.</param>
    /// <param name="logs">Receives mod <c>print</c> output.</param>
    /// <param name="api">The game's API declaration (snapshotted now).</param>
    /// <param name="options">Optional settings; null = defaults.</param>
    public LuaModSet(string? directory, List<string> errors, List<string> logs, LuaApiRegistry api, LuaModOptions? options = null)
    {
        _errors = errors ?? throw new ArgumentNullException(nameof(errors));
        ArgumentNullException.ThrowIfNull(logs);
        ArgumentNullException.ThrowIfNull(api);
        var o = options ?? new LuaModOptions();
        Policy = o.Policy ?? HookPolicy.Default;
        IReadOnlyList<string> order;
        try { order = LoadOrder.Read(directory, o.LoadOrderFile); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _errors.Add($"{o.LoadOrderFile}: {ex.Message}"); return; }
        foreach (var line in order)
        {
            if (o.IsEnabled != null && !o.IsEnabled(ModKeys.Lua(line))) continue;
            if (Path.IsPathRooted(line) || line.Contains("..", StringComparison.Ordinal)) { _errors.Add($"{line}: invalid load-order entry"); continue; }
            var path = Path.Combine(directory!, line);
            if (!File.Exists(path)) { _errors.Add($"missing mod file {line}"); continue; }
            try
            {
                var script = LuaSandbox.Create(o.Sandbox, logs.Add);
                var result = script.DoString(File.ReadAllText(path), null, line);
                if (result.Type != DataType.Table) { _errors.Add($"{line}: did not return a table"); continue; }
                var name = Wire.Text(result.Table.Get("name").CastToString() ?? line);
                _mods.Add(new Mod { Script = script, Table = result.Table, Name = name.Length > 0 ? name : line, Guard = new HookGuard(Policy, _errors), Api = BuildApi(api) });
            }
            catch (InterpreterException ex) { _errors.Add($"{line}: {ex.DecoratedMessage ?? ex.Message}"); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _errors.Add($"{line}: {ex.Message}"); }
        }
    }

    List<(string, DynValue)> BuildApi(LuaApiRegistry api)
    {
        var list = new List<(string, DynValue)>();
        foreach (var (name, cmd, query) in api.Entries)
        {
            if (cmd != null)
                list.Add((name, DynValue.NewCallback((_, a) =>
                {
                    string[] c;
                    try { c = cmd(new LuaArgs(a)); }
                    catch (ArgumentException ex) { throw new ScriptRuntimeException(ex.Message); }
                    try { (_sink ?? throw new InvalidOperationException("api called outside a hook")).Emit(c); }
                    catch (ArgumentException ex) { throw new ScriptRuntimeException(ex.Message); }
                    return DynValue.Nil;
                })));
            else
                list.Add((name, DynValue.NewCallback((_, a) =>
                {
                    try { return DynValue.NewNumber(query!(new LuaArgs(a))); }
                    catch (ArgumentException ex) { throw new ScriptRuntimeException(ex.Message); }
                })));
        }
        return list;
    }

    /// <summary>Failure policy in effect.</summary>
    public HookPolicy Policy { get; }

    /// <summary>Number of loaded mods.</summary>
    public int Count => _mods.Count;

    /// <summary>Display names in load order.</summary>
    public IReadOnlyList<string> Names { get { var l = new List<string>(_mods.Count); foreach (var m in _mods) l.Add(m.Name); return l; } }

    /// <summary>Calls the Lua function <paramref name="hook"/> (if the mod defines it) on every enabled mod,
    /// in load order, with a fresh <c>api</c> table and a fresh event table filled by <paramref name="fillEvent"/>.
    /// Each call is a transaction on <paramref name="sink"/> (see <see cref="HookPolicy"/>).</summary>
    /// <param name="sink">Receives the commands.</param>
    /// <param name="hook">Lua function name, e.g. <c>"on_tick"</c>.</param>
    /// <param name="fillEvent">Fills the event table; null = empty table.</param>
    /// <returns>Number of failed calls.</returns>
    public int Fire(ModCommandSink sink, string hook, Action<LuaEvent>? fillEvent)
    {
        ArgumentNullException.ThrowIfNull(sink);
        int failed = 0;
        _sink = sink;
        try
        {
            foreach (var m in _mods)
            {
                if (m.Guard.Disabled) continue;
                var fn = m.Table.Get(hook);
                if (fn.Type != DataType.Function) continue;
                var api = new Table(m.Script);
                foreach (var (n, f) in m.Api) api.Set(n, f);
                var ev = new Table(m.Script);
                fillEvent?.Invoke(new LuaEvent(ev));
                int mark = sink.Mark();
                try { m.Script.Call(fn, api, ev); }
                catch (InterpreterException ex) { failed++; m.Guard.Fail(sink, mark, m.Name, hook, ex.DecoratedMessage ?? ex.Message); }
                catch (Exception ex) { failed++; m.Guard.Fail(sink, mark, m.Name, hook, ex.Message); }
            }
        }
        finally { _sink = null; }
        return failed;
    }
}

/// <summary>Settings for <see cref="LuaModSet"/>.</summary>
public sealed class LuaModOptions
{
    /// <summary>Include predicate called with <c>lua:&lt;load-order line&gt;</c>; false = the file is never read. Null = all.</summary>
    public Func<string, bool>? IsEnabled { get; init; }
    /// <summary>Load-order file name.</summary>
    public string LoadOrderFile { get; init; } = LoadOrder.DefaultFileName;
    /// <summary>Sandbox settings; null = <see cref="LuaSandboxOptions.Default"/>.</summary>
    public LuaSandboxOptions? Sandbox { get; init; }
    /// <summary>Hook failure policy; null = <see cref="HookPolicy.Default"/>.</summary>
    public HookPolicy? Policy { get; init; }
}
