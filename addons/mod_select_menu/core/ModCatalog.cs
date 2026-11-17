// Engine-free (no Godot dependency): unit-testable headless, reusable by any host.
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ReusableComponents.ModSelect;

/// <summary>Which runtime a mod belongs to.</summary>
public enum ModRuntime
{
    /// <summary>A Lua script listed in a load-order file.</summary>
    Lua,
    /// <summary>A .NET type implementing the host's mod interface inside a DLL.</summary>
    DotNet,
}

/// <summary>One discovered mod. <see cref="Key"/> is the stable identity used for persistence and by
/// the loader predicate: <c>lua:&lt;load-order line&gt;</c> or <c>dotnet:&lt;type full name&gt;</c>.</summary>
/// <param name="Key">Stable identity (see <see cref="ModCatalog.KeyFor"/>).</param>
/// <param name="Name">Display name (search matches this).</param>
/// <param name="Runtime">Lua or DotNet.</param>
/// <param name="Source">File the mod comes from (load-order entry or DLL file name), for display.</param>
public sealed record ModEntry(string Key, string Name, ModRuntime Runtime, string Source);

/// <summary>Discovers mods WITHOUT executing them: Lua from a load-order file, .NET by reading DLL
/// metadata (System.Reflection.Metadata; no assembly is loaded, no mod code runs, files stay unlocked).
/// Order is deterministic: Lua in load-order, then .NET sorted by type full name (ordinal).</summary>
public sealed class ModCatalog
{
    /// <summary>Discovered mods (Lua first, then .NET).</summary>
    public IReadOnlyList<ModEntry> Entries { get; }
    /// <summary>Non-fatal discovery problems (unreadable DLL, bad metadata). Never thrown.</summary>
    public IReadOnlyList<string> Errors { get; }

    ModCatalog(List<ModEntry> entries, List<string> errors) { Entries = entries; Errors = errors; }

    /// <summary>Builds the persistence/loader key for a mod.</summary>
    /// <param name="runtime">Lua or DotNet.</param>
    /// <param name="id">Lua: the load-order line (file name). DotNet: the type full name.</param>
    public static string KeyFor(ModRuntime runtime, string id) => (runtime == ModRuntime.Lua ? "lua:" : "dotnet:") + id;

    /// <summary>Discovers mods.</summary>
    /// <param name="luaDir">Folder with the load-order file; null/empty or missing = no Lua mods.</param>
    /// <param name="dotnetDir">Folder with mod DLLs; null/empty or missing = no .NET mods.</param>
    /// <param name="modInterfaceFullName">Full name of the interface a .NET mod implements
    /// (e.g. <c>MyGame.IMod</c>). Required to list .NET mods.</param>
    /// <param name="loadOrderFile">Load-order file name inside <paramref name="luaDir"/>.</param>
    /// <param name="namePropertyName">Property whose constant string is the display name
    /// (read from IL when the getter is <c>ldstr; ret</c>, else the type's simple name is used).</param>
    public static ModCatalog Discover(string? luaDir, string? dotnetDir, string? modInterfaceFullName,
        string loadOrderFile = "load_order.txt", string namePropertyName = "Name")
    {
        var entries = new List<ModEntry>();
        var errors = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(luaDir))
        {
            var order = Path.Combine(luaDir, loadOrderFile);
            if (File.Exists(order))
            {
                foreach (var raw in File.ReadAllLines(order))
                {
                    // same parsing rule as a typical loader: '#' starts a comment, blank lines skipped
                    var line = raw.Split('#')[0].Trim();
                    if (line.Length == 0) continue;
                    var key = KeyFor(ModRuntime.Lua, line);
                    if (!seen.Add(key)) continue;
                    entries.Add(new ModEntry(key, Path.GetFileNameWithoutExtension(line), ModRuntime.Lua, line));
                }
            }
        }
        if (!string.IsNullOrEmpty(dotnetDir) && Directory.Exists(dotnetDir))
        {
            if (string.IsNullOrEmpty(modInterfaceFullName)) errors.Add("mod interface name not set: .NET mods not listed");
            else
            {
                var found = new List<ModEntry>();
                foreach (var dll in Directory.GetFiles(dotnetDir, "*.dll").OrderBy(p => p, StringComparer.Ordinal))
                {
                    try { ReadDll(dll, modInterfaceFullName, namePropertyName, found); }
                    catch (Exception ex) when (ex is BadImageFormatException or IOException or UnauthorizedAccessException or InvalidOperationException)
                    { errors.Add($"{Path.GetFileName(dll)}: {ex.Message}"); }
                }
                found.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
                foreach (var e in found) if (seen.Add(e.Key)) entries.Add(e);
            }
        }
        return new ModCatalog(entries, errors);
    }

    static void ReadDll(string dll, string iface, string nameProp, List<ModEntry> sink)
    {
        // bytes, not a file stream: the DLL stays unlocked so it can be rebuilt while the game runs
        using var pe = new PEReader(ImmutableArray.Create(File.ReadAllBytes(dll)));
        if (!pe.HasMetadata) return;
        var md = pe.GetMetadataReader();
        var file = Path.GetFileName(dll);
        foreach (var th in md.TypeDefinitions)
        {
            var td = md.GetTypeDefinition(th);
            if ((td.Attributes & (TypeAttributes.Abstract | TypeAttributes.Interface)) != 0) continue;
            bool implements = false;
            foreach (var ih in td.GetInterfaceImplementations())
                if (TypeName(md, md.GetInterfaceImplementation(ih).Interface) == iface) { implements = true; break; }
            if (!implements) continue;
            var full = FullName(md, td);
            var display = ConstName(pe, md, td, nameProp) ?? md.GetString(td.Name);
            sink.Add(new ModEntry(KeyFor(ModRuntime.DotNet, full), display, ModRuntime.DotNet, file));
        }
    }

    static string? TypeName(MetadataReader md, EntityHandle h) => h.Kind switch
    {
        HandleKind.TypeReference => Join(md.GetString(md.GetTypeReference((TypeReferenceHandle)h).Namespace), md.GetString(md.GetTypeReference((TypeReferenceHandle)h).Name)),
        HandleKind.TypeDefinition => FullName(md, md.GetTypeDefinition((TypeDefinitionHandle)h)),
        _ => null, // generic instantiation: not a plain mod interface
    };

    static string Join(string ns, string name) => ns.Length == 0 ? name : ns + "." + name;

    // matches System.Type.FullName: Namespace.Outer+Inner
    static string FullName(MetadataReader md, TypeDefinition td)
    {
        var decl = td.GetDeclaringType();
        return decl.IsNil ? Join(md.GetString(td.Namespace), md.GetString(td.Name))
            : FullName(md, md.GetTypeDefinition(decl)) + "+" + md.GetString(td.Name);
    }

    // getter body exactly `ldstr "<const>"; ret` (expression-bodied constant) -> the constant
    static string? ConstName(PEReader pe, MetadataReader md, TypeDefinition td, string nameProp)
    {
        foreach (var ph in td.GetProperties())
        {
            var p = md.GetPropertyDefinition(ph);
            var pn = md.GetString(p.Name);
            if (pn != nameProp && !pn.EndsWith("." + nameProp, StringComparison.Ordinal)) continue;
            var getter = p.GetAccessors().Getter;
            if (getter.IsNil) continue;
            var rva = md.GetMethodDefinition(getter).RelativeVirtualAddress;
            if (rva == 0) continue;
            var il = pe.GetMethodBody(rva).GetILBytes();
            if (il is not { Length: 6 } || il[0] != 0x72 || il[5] != 0x2A) continue;
            int token = il[1] | il[2] << 8 | il[3] << 16 | il[4] << 24;
            if ((token >> 24) != 0x70) continue; // user-string heap token
            var s = md.GetUserString(MetadataTokens.UserStringHandle(token & 0xFFFFFF));
            if (s.Length > 0) return s;
        }
        return null;
    }

    /// <summary>Case-insensitive, culture-invariant (ordinal) substring match on <see cref="ModEntry.Name"/>.
    /// Blank query matches everything.</summary>
    /// <param name="entry">Mod to test.</param>
    /// <param name="query">Search text; leading/trailing whitespace ignored.</param>
    public static bool Matches(ModEntry entry, string? query)
    {
        var q = query?.Trim();
        return string.IsNullOrEmpty(q) || entry.Name.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Entries whose name matches <paramref name="query"/>, in catalog order.</summary>
    public IReadOnlyList<ModEntry> Filter(string? query) => Entries.Where(e => Matches(e, query)).ToList();
}
