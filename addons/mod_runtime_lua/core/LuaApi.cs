// Engine-free (no Godot dependency). Part of the mod_runtime_lua addon (requires MoonSharp 2.0.0 + mod_runtime).
using System;
using System.Collections.Generic;
using MoonSharp.Interpreter;

namespace ReusableComponents.ModRuntime;

/// <summary>Arguments of one Lua API call, validated with the same <see cref="Wire"/> rules the .NET side uses.
/// Every accessor throws <see cref="ArgumentException"/> on a bad value; the host turns that into a Lua
/// error that counts as one failed hook call.</summary>
public readonly struct LuaArgs
{
    readonly CallbackArguments _a;

    /// <summary>Wraps MoonSharp arguments (used by the host; games normally only read them).</summary>
    /// <param name="args">Raw arguments.</param>
    public LuaArgs(CallbackArguments args) { _a = args; }

    /// <summary>Number of arguments passed.</summary>
    public int Count => _a.Count;

    /// <summary>Argument as an int32 (floored); numeric strings are accepted, like Lua.</summary>
    /// <param name="index">0-based position.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentException">Missing, non-numeric, NaN or out of int32 range.</exception>
    public int Int(int index) => int.Parse(IntWire(index), System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Argument as a wire integer string (see <see cref="Wire.Int(double)"/>).</summary>
    /// <param name="index">0-based position.</param>
    /// <returns>Invariant integer string.</returns>
    /// <exception cref="ArgumentException">Missing, non-numeric, NaN or out of int32 range.</exception>
    public string IntWire(int index)
    {
        var v = _a[index];
        double? d = v.Type == DataType.Number ? v.Number : v.Type == DataType.String ? v.CastToNumber() : null;
        return d is { } x ? Wire.Int(x) : throw new ArgumentException("expected integer");
    }

    /// <summary>Argument as a validated name (<see cref="Wire.Name"/>).</summary>
    /// <param name="index">0-based position.</param>
    /// <param name="maxLength">Maximum length.</param>
    /// <returns>The name.</returns>
    /// <exception cref="ArgumentException">Missing or invalid name.</exception>
    public string Name(int index, int maxLength = Wire.DefaultMaxNameLength) => Wire.Name(_a[index].CastToString(), maxLength);

    /// <summary>Argument printed like Lua's <c>tostring</c>, sanitized with <see cref="Wire.Text"/>.</summary>
    /// <param name="index">0-based position.</param>
    /// <returns>Sanitized text ("nil" when missing).</returns>
    public string Text(int index) => Wire.Text(_a[index].ToPrintString());
}

/// <summary>Event table passed as the second argument of a Lua hook (<c>function(api, ev)</c>).</summary>
public readonly struct LuaEvent
{
    readonly Table _t;

    /// <summary>Wraps a table (used by the host).</summary>
    /// <param name="table">Target table.</param>
    public LuaEvent(Table table) { _t = table; }

    /// <summary>Sets an integer field.</summary>
    /// <param name="key">Field name.</param>
    /// <param name="value">Value.</param>
    /// <returns>This event, for chaining.</returns>
    public LuaEvent Set(string key, long value) { _t.Set(key, DynValue.NewNumber(value)); return this; }

    /// <summary>Sets a string field.</summary>
    /// <param name="key">Field name.</param>
    /// <param name="value">Value (null = nil).</param>
    /// <returns>This event, for chaining.</returns>
    public LuaEvent Set(string key, string? value) { _t.Set(key, value == null ? DynValue.Nil : DynValue.NewString(value)); return this; }

    /// <summary>Sets a boolean field.</summary>
    /// <param name="key">Field name.</param>
    /// <param name="value">Value.</param>
    /// <returns>This event, for chaining.</returns>
    public LuaEvent Set(string key, bool value) { _t.Set(key, DynValue.NewBoolean(value)); return this; }
}

/// <summary>The game's Lua API, declared once before mods load: each entry becomes a function on the
/// <c>api</c> table every hook receives. Commands append to the command sink; queries return a number
/// to the script immediately (read-only views of simulation state).</summary>
public sealed class LuaApiRegistry
{
    internal readonly List<(string Name, Func<LuaArgs, string[]>? Command, Func<LuaArgs, double>? Query)> Entries = new();
    readonly HashSet<string> _names = new(StringComparer.Ordinal);

    /// <summary>Declares a command: <c>api.&lt;name&gt;(...)</c> builds a <c>string[]</c> (verb + args) that is
    /// appended to the sink.</summary>
    /// <param name="name">Lua function name (letters, digits, _).</param>
    /// <param name="build">Validates the arguments and returns the command.</param>
    /// <returns>This registry, for chaining.</returns>
    /// <exception cref="ArgumentException">Invalid or duplicate name.</exception>
    public LuaApiRegistry Command(string name, Func<LuaArgs, string[]> build) => Add(name, build ?? throw new ArgumentNullException(nameof(build)), null);

    /// <summary>Declares a query: <c>api.&lt;name&gt;(...)</c> returns a number to the script.</summary>
    /// <param name="name">Lua function name (letters, digits, _).</param>
    /// <param name="query">Computes the value (must not mutate simulation state).</param>
    /// <returns>This registry, for chaining.</returns>
    /// <exception cref="ArgumentException">Invalid or duplicate name.</exception>
    public LuaApiRegistry Query(string name, Func<LuaArgs, double> query) => Add(name, null, query ?? throw new ArgumentNullException(nameof(query)));

    /// <summary>Declared function names, in declaration order.</summary>
    public IReadOnlyList<string> Names { get { var l = new List<string>(); foreach (var e in Entries) l.Add(e.Name); return l; } }

    LuaApiRegistry Add(string name, Func<LuaArgs, string[]>? cmd, Func<LuaArgs, double>? query)
    {
        Wire.Name(name, int.MaxValue);
        if (!_names.Add(name)) throw new ArgumentException($"duplicate api function '{name}'");
        Entries.Add((name, cmd, query));
        return this;
    }
}
