// Engine-free (no Godot dependency). Part of the test_harness addon.
using System;

namespace ReusableComponents.TestHarness;

/// <summary>Collects checks and prints one machine-parsable line per check:
/// <c>[PASS] &lt;suite&gt; &lt;check&gt;</c> / <c>[FAIL] &lt;suite&gt; &lt;check&gt; -- &lt;detail&gt;</c>, then a summary
/// <c>&lt;TAG&gt; &lt;pass&gt; passed, &lt;fail&gt; failed</c>. A report with ZERO checks is a failure
/// (<see cref="ExitCode"/> 2): green over nothing executed is not green. Not thread-safe.</summary>
public sealed class TestReport
{
    readonly Action<string> _print;

    /// <summary>Creates a report.</summary>
    /// <param name="suite">Suite name printed on every line (no spaces recommended).</param>
    /// <param name="print">Line sink (Godot: <c>GD.Print</c>). Null = <see cref="Console.WriteLine(string)"/>.</param>
    public TestReport(string suite, Action<string>? print = null)
    {
        Suite = string.IsNullOrWhiteSpace(suite) ? "suite" : suite;
        _print = print ?? Console.WriteLine;
    }

    /// <summary>Suite name.</summary>
    public string Suite { get; }
    /// <summary>Checks that passed.</summary>
    public int Passed { get; private set; }
    /// <summary>Checks that failed.</summary>
    public int Failed { get; private set; }

    /// <summary>Records and prints one check.</summary>
    /// <param name="name">Check description.</param>
    /// <param name="ok">Result.</param>
    /// <param name="detail">Optional detail appended to FAIL lines (and PASS lines when <paramref name="alwaysDetail"/>).</param>
    /// <param name="alwaysDetail">Print the detail on PASS too.</param>
    /// <returns><paramref name="ok"/>, for chaining.</returns>
    public bool Check(string name, bool ok, string? detail = null, bool alwaysDetail = false)
    {
        if (ok) Passed++; else Failed++;
        var tail = detail != null && (!ok || alwaysDetail) ? " -- " + detail : "";
        _print($"[{(ok ? "PASS" : "FAIL")}] {Suite} {name}{tail}");
        return ok;
    }

    /// <summary>Records an exception as one failed check (use in a catch around a suite).</summary>
    /// <param name="name">Where it happened.</param>
    /// <param name="ex">The exception.</param>
    public void Crash(string name, Exception ex) => Check(name + " (crashed)", false, ex.GetType().Name + ": " + ex.Message);

    /// <summary>0 = at least one check and none failed; 1 = a check failed; 2 = no checks ran.</summary>
    public int ExitCode => Failed > 0 ? 1 : Passed == 0 ? 2 : 0;

    /// <summary>Prints and returns the summary line.</summary>
    /// <param name="tag">Leading tag, e.g. <c>COMPONENTTEST</c>.</param>
    /// <returns>The line.</returns>
    public string Summary(string tag = "TESTS")
    {
        var line = $"{tag} {Passed} passed, {Failed} failed";
        _print(line);
        return line;
    }
}
