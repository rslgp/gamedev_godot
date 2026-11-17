using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace ReusableComponents.TestHarness;

/// <summary>Godot side of the headless harness: user args, frame waits, screenshots and exit codes.
/// Godot exits with code 0 even after an unhandled C# exception, so a runner must ALSO scan the output
/// for error patterns (see <c>tools/run_godot_headless.ps1</c>).</summary>
public static class GodotHarness
{
    /// <summary>User args (everything after <c>--</c>) from <c>OS.GetCmdlineUserArgs()</c>.</summary>
    /// <returns>Parsed args.</returns>
    public static HarnessArgs Args() => new(OS.GetCmdlineUserArgs());

    /// <summary>Waits <paramref name="frames"/> physics frames of <paramref name="node"/>'s tree.</summary>
    /// <param name="node">Any node inside the tree.</param>
    /// <param name="frames">Frame count (&lt;= 0 returns immediately).</param>
    /// <returns>Completes after the frames.</returns>
    public static async Task PhysicsFrames(Node node, int frames)
    {
        for (int i = 0; i < frames; i++) await node.ToSignal(node.GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    /// <summary>Waits <paramref name="frames"/> process frames.</summary>
    /// <param name="node">Any node inside the tree.</param>
    /// <param name="frames">Frame count.</param>
    /// <returns>Completes after the frames.</returns>
    public static async Task ProcessFrames(Node node, int frames)
    {
        for (int i = 0; i < frames; i++) await node.ToSignal(node.GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    /// <summary>Saves the viewport as PNG after <paramref name="settleFrames"/> frames and one post-draw.
    /// Needs a windowed (non-headless) run; under <c>--headless</c> the image is empty and false is returned.</summary>
    /// <param name="node">Node whose viewport is captured.</param>
    /// <param name="path">PNG path (res://, user:// or absolute).</param>
    /// <param name="settleFrames">Frames to wait first (layout, tweens).</param>
    /// <returns>True when a non-empty image was written.</returns>
    public static async Task<bool> Screenshot(Node node, string path, int settleFrames = 6)
    {
        if (DisplayServer.GetName() == "headless") return false;
        await ProcessFrames(node, settleFrames);
        await node.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var img = node.GetViewport().GetTexture().GetImage();
        return img != null && !img.IsEmpty() && img.SavePng(path) == Error.Ok;
    }

    /// <summary>Quits with the report's exit code after printing its summary.</summary>
    /// <param name="node">Any node inside the tree.</param>
    /// <param name="report">Finished report.</param>
    /// <param name="tag">Summary tag.</param>
    public static void Finish(Node node, TestReport report, string tag = "COMPONENTTEST")
    {
        report.Summary(tag);
        node.GetTree().Quit(report.ExitCode);
    }
}

/// <summary>A component test suite run by <see cref="ComponentTestRunner"/>.</summary>
public interface IComponentTestSuite
{
    /// <summary>Suite name, matched by <c>--componenttest=&lt;Name&gt;</c> (ordinal, case-insensitive).</summary>
    string Name { get; }

    /// <summary>Runs the checks. Exceptions are caught by the runner and counted as a failed check.</summary>
    /// <param name="host">A node in the tree the suite may add children to (freed after the suite).</param>
    /// <param name="report">Report to record checks in.</param>
    /// <returns>Completes when the suite is done.</returns>
    Task Run(Node host, TestReport report);
}

/// <summary>Runs registered suites selected by <c>--componenttest</c> (all) or <c>--componenttest=A,B</c>,
/// prints PASS/FAIL lines and quits with 0 (all passed), 1 (a check failed) or 2 (nothing ran /
/// unknown suite name). Each suite gets a fresh child node that is freed afterwards.</summary>
public sealed class ComponentTestRunner
{
    readonly List<IComponentTestSuite> _suites = new();

    /// <summary>Registers a suite.</summary>
    /// <param name="suite">Suite.</param>
    /// <returns>This runner, for chaining.</returns>
    public ComponentTestRunner Add(IComponentTestSuite suite) { _suites.Add(suite ?? throw new ArgumentNullException(nameof(suite))); return this; }

    /// <summary>Registered suite names.</summary>
    public IReadOnlyList<string> Names { get { var l = new List<string>(); foreach (var s in _suites) l.Add(s.Name); return l; } }

    /// <summary>Runs the selection and quits the tree with the exit code.</summary>
    /// <param name="root">Node in the tree (suites get children of it).</param>
    /// <param name="selection">Comma-separated suite names; null, "" or "1" = all.</param>
    /// <returns>The exit code passed to Quit.</returns>
    public async Task<int> RunAndQuit(Node root, string? selection)
    {
        var chosen = new List<IComponentTestSuite>();
        if (string.IsNullOrEmpty(selection) || selection == "1") chosen.AddRange(_suites);
        else
            foreach (var want in selection.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var hit = _suites.Find(s => string.Equals(s.Name, want, StringComparison.OrdinalIgnoreCase));
                if (hit == null) { GD.PushError($"ComponentTestRunner: unknown suite '{want}'. Known: {string.Join(",", Names)}"); root.GetTree().Quit(2); return 2; }
                chosen.Add(hit);
            }
        int pass = 0, fail = 0;
        foreach (var suite in chosen)
        {
            var r = new TestReport(suite.Name, s => GD.Print(s));
            var host = new Node { Name = "suite_" + suite.Name };
            root.AddChild(host);
            try { await suite.Run(host, r); }
            catch (Exception ex) { r.Crash("suite", ex); }
            host.QueueFree();
            await GodotHarness.ProcessFrames(root, 1);
            if (r.Passed + r.Failed == 0) r.Check("ran at least one check", false);
            pass += r.Passed; fail += r.Failed;
        }
        int code = chosen.Count == 0 ? 2 : fail > 0 ? 1 : pass == 0 ? 2 : 0;
        GD.Print($"COMPONENTTEST {pass} passed, {fail} failed suites={chosen.Count}");
        root.GetTree().Quit(code);
        return code;
    }
}
