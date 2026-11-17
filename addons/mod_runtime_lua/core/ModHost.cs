// Engine-free (no Godot dependency). Part of the mod_runtime_lua addon (requires MoonSharp 2.0.0 + mod_runtime).
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace ReusableComponents.ModRuntime;

/// <summary>Generic in-process mod host: Lua mods (<c>&lt;mods&gt;/lua</c>, load order) fire first, then .NET mods
/// (<c>&lt;mods&gt;/dotnet</c>, ordered by type full name). The GAME supplies its own contract: the .NET mod
/// interface <typeparamref name="TMod"/>, an API object that emits into <see cref="Sink"/>, and the matching
/// Lua functions via <see cref="ModHostOptions{TMod}.LuaApi"/>. <see cref="Fire"/> returns the commands in
/// order; the game validates and applies them. <see cref="Errors"/> counts every load failure and failed
/// hook call (cumulative), the robustness metric. Read the mod selection only when creating a host: a new
/// host is the "next load / hot reload". Not thread-safe; call from the simulation thread.</summary>
/// <typeparam name="TMod">The game's .NET mod interface.</typeparam>
public sealed class ModHost<TMod> : IDisposable where TMod : class
{
    readonly LuaModSet _lua;
    readonly DotNetModSet<TMod> _dotnet;

    /// <summary>Loads every mod.</summary>
    /// <param name="modsDirectory">Folder holding the lua/ and dotnet/ subfolders; null = no mods.</param>
    /// <param name="options">Game contract and settings; null = defaults (no Lua API functions).</param>
    public ModHost(string? modsDirectory, ModHostOptions<TMod>? options = null)
    {
        var o = options ?? new ModHostOptions<TMod>();
        string? Sub(string name) => string.IsNullOrEmpty(modsDirectory) ? null : Path.Combine(modsDirectory, name);
        _lua = new LuaModSet(Sub(o.LuaSubfolder), Errors, Logs, o.LuaApi ?? new LuaApiRegistry(),
            new LuaModOptions { IsEnabled = o.IsEnabled, LoadOrderFile = o.LoadOrderFile, Sandbox = o.Sandbox, Policy = o.Policy });
        _dotnet = new DotNetModSet<TMod>(Sub(o.DotNetSubfolder), Errors,
            new DotNetModOptions<TMod> { IsEnabled = o.IsEnabled, NameOf = o.NameOf, SharedAssemblies = o.SharedAssemblies, Policy = o.Policy, ContextName = o.ContextName });
    }

    /// <summary>Command buffer. The game's .NET API implementation calls <see cref="ModCommandSink.Emit"/> on it.</summary>
    public ModCommandSink Sink { get; } = new();

    /// <summary>Cumulative mod failures: load errors + failed hook calls (+ one line per disabled mod).</summary>
    public List<string> Errors { get; } = new();

    /// <summary>Everything Lua mods printed.</summary>
    public List<string> Logs { get; } = new();

    /// <summary>Loaded Lua mods.</summary>
    public int LuaCount => _lua.Count;

    /// <summary>Loaded .NET mods.</summary>
    public int DotNetCount => _dotnet.Count;

    /// <summary>Loaded .NET mod instances (firing order).</summary>
    public IReadOnlyList<TMod> DotNetMods => _dotnet.Mods;

    /// <summary>Display names: Lua (load order) then .NET (type order).</summary>
    public IReadOnlyList<string> Names { get { var l = new List<string>(_lua.Names); l.AddRange(_dotnet.Names); return l; } }

    /// <summary>Fires one hook on every mod (Lua first, then .NET) and returns the commands they emitted, in order.
    /// Any commands left in <see cref="Sink"/> before the call are returned too.</summary>
    /// <param name="hook">Hook name: the Lua function name, also used in error lines for .NET mods.</param>
    /// <param name="fillEvent">Fills the Lua event table; null = empty.</param>
    /// <param name="dotnetHook">Invokes the hook on one .NET mod (pass your API object); null = Lua only.</param>
    /// <returns>Commands (verb + args), owned by the caller.</returns>
    public List<string[]> Fire(string hook, Action<LuaEvent>? fillEvent, Action<TMod>? dotnetHook)
    {
        _lua.Fire(Sink, hook, fillEvent);
        if (dotnetHook != null) _dotnet.Fire(Sink, hook, dotnetHook);
        return Sink.Drain();
    }

    /// <summary>Weak reference to the .NET mod load context (unload proof; see <see cref="DotNetModSet{TMod}.ContextRef"/>).</summary>
    /// <returns>Weak reference.</returns>
    public WeakReference ContextRef() => _dotnet.ContextRef();

    /// <summary>Unloads the .NET mods (hot reload: dispose, then create a new host). Idempotent.</summary>
    public void Dispose() => _dotnet.Dispose();
}

/// <summary>Game contract and settings for <see cref="ModHost{TMod}"/>.</summary>
/// <typeparam name="TMod">The game's .NET mod interface.</typeparam>
public sealed class ModHostOptions<TMod> where TMod : class
{
    /// <summary>The game's Lua API (commands and queries). Null = no functions on <c>api</c>.</summary>
    public LuaApiRegistry? LuaApi { get; init; }
    /// <summary>Include predicate over <see cref="ModKeys"/> keys (e.g. a saved menu selection). Null = all.</summary>
    public Func<string, bool>? IsEnabled { get; init; }
    /// <summary>.NET display-name getter (e.g. <c>m =&gt; m.Name</c>). Null = type full name.</summary>
    public Func<TMod, string>? NameOf { get; init; }
    /// <summary>Extra host assemblies .NET mods may reference.</summary>
    public IEnumerable<Assembly>? SharedAssemblies { get; init; }
    /// <summary>Hook failure policy for both runtimes. Null = <see cref="HookPolicy.Default"/>.</summary>
    public HookPolicy? Policy { get; init; }
    /// <summary>Lua sandbox settings. Null = <see cref="LuaSandboxOptions.Default"/>.</summary>
    public LuaSandboxOptions? Sandbox { get; init; }
    /// <summary>Lua load-order file name.</summary>
    public string LoadOrderFile { get; init; } = LoadOrder.DefaultFileName;
    /// <summary>Lua subfolder name.</summary>
    public string LuaSubfolder { get; init; } = "lua";
    /// <summary>.NET subfolder name.</summary>
    public string DotNetSubfolder { get; init; } = "dotnet";
    /// <summary>Diagnostic name of the .NET load context.</summary>
    public string ContextName { get; init; } = "mods";
}
