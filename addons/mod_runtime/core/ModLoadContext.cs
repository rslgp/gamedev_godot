// Engine-free (no Godot dependency). Part of the mod_runtime addon.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Loader;

namespace ReusableComponents.ModRuntime;

/// <summary>Collectible <see cref="AssemblyLoadContext"/> for .NET mods (hot reload: <see cref="AssemblyLoadContext.Unload"/>
/// then load a new context). Assemblies named in the shared set resolve to the HOST's already-loaded copy,
/// whatever context the host itself lives in. This is required under Godot .NET, whose game assembly is
/// not in the Default context: returning null there makes the mod's reference to the API assembly fail
/// to load. Everything else falls back to the default resolution.
/// Not a sandbox: mod code runs with full trust.</summary>
public sealed class ModLoadContext : AssemblyLoadContext
{
    readonly Dictionary<string, Assembly> _shared = new(StringComparer.Ordinal);

    /// <summary>Creates the context.</summary>
    /// <param name="name">Diagnostic name.</param>
    /// <param name="sharedAssemblies">Host assemblies mods compile against (at least the one that defines
    /// the mod interface). Null entries are ignored.</param>
    public ModLoadContext(string name, IEnumerable<Assembly> sharedAssemblies) : base(name, isCollectible: true)
    {
        ArgumentNullException.ThrowIfNull(sharedAssemblies);
        foreach (var a in sharedAssemblies)
            if (a?.GetName().Name is { } n) _shared[n] = a;
    }

    /// <summary>Simple names of the shared host assemblies (a DLL with one of these names in the mods
    /// folder is skipped by the loader).</summary>
    public IReadOnlyCollection<string> SharedAssemblyNames => _shared.Keys;

    /// <summary>Resolves shared assemblies to the host copy; null (default resolution) otherwise.</summary>
    /// <param name="assemblyName">Requested assembly.</param>
    /// <returns>Host assembly or null.</returns>
    protected override Assembly? Load(AssemblyName assemblyName) =>
        assemblyName.Name != null && _shared.TryGetValue(assemblyName.Name, out var a) ? a : null;
}
