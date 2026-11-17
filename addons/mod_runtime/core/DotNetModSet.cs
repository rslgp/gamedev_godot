// Engine-free (no Godot dependency). Part of the mod_runtime addon.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace ReusableComponents.ModRuntime;

/// <summary>.NET mods of the game-defined interface <typeparamref name="TMod"/>, loaded from every <c>*.dll</c> in a
/// folder into one collectible <see cref="ModLoadContext"/>. DLLs are read as bytes (the files stay
/// unlocked, so a modder can rebuild while the game runs). Mods are instantiated with their public
/// parameterless constructor and ordered by type full name (ordinal), so the order is the same on every
/// machine. A broken DLL, a throwing constructor or a throwing hook is recorded in the error log and
/// never stops the others. Dispose drops every mod reference and unloads the context (hot reload);
/// prove the unload with <see cref="ContextRef"/>. Not thread-safe; call from the simulation thread.</summary>
/// <typeparam name="TMod">The game's mod interface (any interface or base class).</typeparam>
public sealed class DotNetModSet<TMod> : IDisposable where TMod : class
{
    readonly List<TMod> _mods = new();
    readonly List<string> _names = new();
    readonly List<HookGuard> _guards = new();
    readonly List<string> _errors;
    ModLoadContext? _ctx;

    /// <summary>Loads the mods.</summary>
    /// <param name="directory">Folder with mod DLLs; null/empty or missing = no mods (not an error).</param>
    /// <param name="errors">Shared error log (load failures and failed hook calls are appended).</param>
    /// <param name="options">Optional settings; null = defaults.</param>
    public DotNetModSet(string? directory, List<string> errors, DotNetModOptions<TMod>? options = null)
    {
        _errors = errors ?? throw new ArgumentNullException(nameof(errors));
        var o = options ?? new DotNetModOptions<TMod>();
        Policy = o.Policy ?? HookPolicy.Default;
        var shared = new List<Assembly> { typeof(TMod).Assembly };
        if (o.SharedAssemblies != null) shared.AddRange(o.SharedAssemblies);
        _ctx = new ModLoadContext(o.ContextName, shared);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return;

        var found = new List<TMod>();
        var files = Directory.GetFiles(directory, "*.dll");
        Array.Sort(files, StringComparer.Ordinal);
        foreach (var dll in files)
        {
            if (Contains(_ctx.SharedAssemblyNames, Path.GetFileNameWithoutExtension(dll))) continue;
            try
            {
                using var ms = new MemoryStream(File.ReadAllBytes(dll));
                var asm = _ctx.LoadFromStream(ms);
                foreach (var t in asm.GetTypes())
                {
                    if (!typeof(TMod).IsAssignableFrom(t) || t.IsAbstract || t.IsInterface) continue;
                    var full = t.FullName ?? t.Name;
                    if (o.IsEnabled != null && !o.IsEnabled(ModKeys.DotNet(full))) continue;
                    try { found.Add((TMod)Activator.CreateInstance(t)!); }
                    catch (Exception ex) { _errors.Add($"{full}: constructor failed: {(ex as TargetInvocationException)?.InnerException?.Message ?? ex.Message}"); }
                }
            }
            catch (Exception ex) { _errors.Add($"{Path.GetFileName(dll)}: {ex.Message}"); }
        }
        found.Sort((a, b) => string.CompareOrdinal(a.GetType().FullName, b.GetType().FullName));
        foreach (var m in found)
        {
            _mods.Add(m);
            _names.Add(SafeName(m, o.NameOf));
            _guards.Add(new HookGuard(Policy, _errors));
        }
    }

    static bool Contains(IReadOnlyCollection<string> set, string name)
    {
        foreach (var s in set) if (string.Equals(s, name, StringComparison.Ordinal)) return true;
        return false;
    }

    static string SafeName(TMod m, Func<TMod, string>? nameOf)
    {
        if (nameOf == null) return m.GetType().FullName ?? m.GetType().Name;
        try { var n = Wire.Text(nameOf(m)); return n.Length > 0 ? n : m.GetType().FullName ?? "?"; }
        catch (Exception) { return m.GetType().FullName ?? "?"; }
    }

    /// <summary>Failure policy in effect.</summary>
    public HookPolicy Policy { get; }

    /// <summary>Loaded mods, in firing order. Empty after <see cref="Dispose"/>.</summary>
    public IReadOnlyList<TMod> Mods => _mods;

    /// <summary>Number of loaded mods.</summary>
    public int Count => _mods.Count;

    /// <summary>Display names (sanitized), parallel to <see cref="Mods"/>.</summary>
    public IReadOnlyList<string> Names => _names;

    /// <summary>Calls <paramref name="hook"/> on every enabled mod, in order. Each call is a transaction on
    /// <paramref name="sink"/>: if it throws, the error is logged as <c>&lt;mod&gt;.&lt;hookName&gt;: msg</c>
    /// and (per <see cref="Policy"/>) its partial commands are discarded.</summary>
    /// <param name="sink">Where the mods' API implementation emits commands.</param>
    /// <param name="hookName">Name used in error lines.</param>
    /// <param name="hook">Invokes the hook on one mod (the game passes its API object here).</param>
    /// <returns>Number of failed calls in this fire.</returns>
    public int Fire(ModCommandSink sink, string hookName, Action<TMod> hook)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(hook);
        int failed = 0;
        for (int i = 0; i < _mods.Count; i++)
        {
            if (_guards[i].Disabled) continue;
            int mark = sink.Mark();
            try { hook(_mods[i]); }
            catch (Exception ex)
            {
                failed++;
                _guards[i].Fail(sink, mark, _names[i], hookName, ex.Message);
            }
        }
        return failed;
    }

    /// <summary>Weak reference to the load context. After <see cref="Dispose"/> and a few
    /// <c>GC.Collect(); GC.WaitForPendingFinalizers();</c> rounds it must report <c>IsAlive == false</c>;
    /// if it stays alive, something (event, static cache, closure) still holds mod objects.
    /// Take it inside a <c>[MethodImpl(MethodImplOptions.NoInlining)]</c> method so no local keeps the set alive.</summary>
    /// <returns>Weak reference (target null once disposed and collected).</returns>
    /// <exception cref="ObjectDisposedException">Called after <see cref="Dispose"/>.</exception>
    public WeakReference ContextRef() => new(_ctx ?? throw new ObjectDisposedException(nameof(DotNetModSet<TMod>)));

    /// <summary>Drops every mod reference and unloads the context. Idempotent.</summary>
    public void Dispose()
    {
        _mods.Clear(); _names.Clear(); _guards.Clear();
        _ctx?.Unload();
        _ctx = null;
    }
}

/// <summary>Settings for <see cref="DotNetModSet{TMod}"/>.</summary>
/// <typeparam name="TMod">The game's mod interface.</typeparam>
public sealed class DotNetModOptions<TMod> where TMod : class
{
    /// <summary>Include predicate called with <c>dotnet:&lt;type full name&gt;</c> (see <see cref="ModKeys"/>).
    /// False = the type is never instantiated and is not an error. Null = every mod.</summary>
    public Func<string, bool>? IsEnabled { get; init; }

    /// <summary>Display-name getter (e.g. <c>m =&gt; m.Name</c>). Null = type full name. A throwing getter
    /// falls back to the type full name.</summary>
    public Func<TMod, string>? NameOf { get; init; }

    /// <summary>Extra host assemblies mods may reference besides the one defining <typeparamref name="TMod"/>.</summary>
    public IEnumerable<Assembly>? SharedAssemblies { get; init; }

    /// <summary>Hook failure policy. Null = <see cref="HookPolicy.Default"/>.</summary>
    public HookPolicy? Policy { get; init; }

    /// <summary>Diagnostic name of the load context.</summary>
    public string ContextName { get; init; } = "mods";
}
