// Engine-free (no Godot dependency). Part of the mod_runtime addon.
using System;
using System.Collections.Generic;

namespace ReusableComponents.ModRuntime;

/// <summary>Ordered buffer of mod commands (<c>string[]</c>: verb + invariant-culture args). Mods only EMIT;
/// the game applies the drained list after the hook, so mods never mutate state mid-hook.
/// <see cref="Mark"/>/<see cref="Truncate"/> give each hook call a transaction: a hook that throws can have
/// its partial commands discarded. Not thread-safe.</summary>
public sealed class ModCommandSink
{
    readonly List<string[]> _commands = new();

    /// <summary>Commands emitted since the last <see cref="Drain"/> / <see cref="Clear"/>.</summary>
    public IReadOnlyList<string[]> Commands => _commands;

    /// <summary>Number of buffered commands.</summary>
    public int Count => _commands.Count;

    /// <summary>Appends a command.</summary>
    /// <param name="command">Verb first; must be non-empty and contain no null element.</param>
    /// <exception cref="ArgumentException">Empty command or a null element.</exception>
    public void Emit(params string[] command)
    {
        if (command == null || command.Length == 0) throw new ArgumentException("empty command");
        foreach (var part in command) if (part == null) throw new ArgumentException("null command element");
        _commands.Add(command);
    }

    /// <summary>Current position, to roll back to with <see cref="Truncate"/>.</summary>
    /// <returns>The current <see cref="Count"/>.</returns>
    public int Mark() => _commands.Count;

    /// <summary>Discards every command emitted after <paramref name="mark"/>.</summary>
    /// <param name="mark">Value from <see cref="Mark"/>.</param>
    /// <returns>Number of commands discarded.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Mark beyond the current count or negative.</exception>
    public int Truncate(int mark)
    {
        if (mark < 0 || mark > _commands.Count) throw new ArgumentOutOfRangeException(nameof(mark));
        int n = _commands.Count - mark;
        _commands.RemoveRange(mark, n);
        return n;
    }

    /// <summary>Returns the buffered commands and empties the sink.</summary>
    /// <returns>A new list owned by the caller.</returns>
    public List<string[]> Drain()
    {
        var copy = new List<string[]>(_commands);
        _commands.Clear();
        return copy;
    }

    /// <summary>Drops every buffered command.</summary>
    public void Clear() => _commands.Clear();
}

/// <summary>How a hook call that throws is handled.</summary>
public sealed class HookPolicy
{
    /// <summary>True (default): commands the failing call emitted before throwing are discarded, so a
    /// hook is all-or-nothing. False: keep them (the benchmarks' original behavior).</summary>
    public bool DiscardPartialCommands { get; init; } = true;

    /// <summary>After this many failed calls a mod is disabled ("poisoned") for the rest of the session
    /// and one extra error line says so. 0 (default) = never disable.</summary>
    public int DisableAfterErrors { get; init; }

    /// <summary>The default policy (discard partial commands, never disable).</summary>
    public static HookPolicy Default { get; } = new();
}

/// <summary>Per-mod failure bookkeeping (one instance per loaded mod), shared by the .NET and Lua mod sets.
/// Error line format: <c>&lt;mod&gt;.&lt;hook&gt;: &lt;message&gt;</c>.</summary>
public sealed class HookGuard
{
    readonly HookPolicy _policy;
    readonly List<string> _errors;
    int _failures;

    /// <summary>Creates a guard for one mod.</summary>
    /// <param name="policy">Failure policy.</param>
    /// <param name="errors">Shared error log the guard appends to.</param>
    public HookGuard(HookPolicy policy, List<string> errors)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _errors = errors ?? throw new ArgumentNullException(nameof(errors));
    }

    /// <summary>True once the mod reached <see cref="HookPolicy.DisableAfterErrors"/>; skip its hooks.</summary>
    public bool Disabled { get; private set; }

    /// <summary>Failed calls so far.</summary>
    public int Failures => _failures;

    /// <summary>Records a failure: rolls back partial commands per policy, logs, maybe disables.</summary>
    /// <param name="sink">Sink the hook wrote to.</param>
    /// <param name="mark">Sink mark taken before the call.</param>
    /// <param name="modName">Display name for the log.</param>
    /// <param name="hook">Hook name for the log.</param>
    /// <param name="message">Failure message.</param>
    public void Fail(ModCommandSink sink, int mark, string modName, string hook, string message)
    {
        if (_policy.DiscardPartialCommands) sink.Truncate(mark);
        _errors.Add($"{modName}.{hook}: {message}");
        _failures++;
        if (_policy.DisableAfterErrors > 0 && _failures >= _policy.DisableAfterErrors && !Disabled)
        {
            Disabled = true;
            _errors.Add($"{modName}: disabled after {_failures} errors");
        }
    }
}
