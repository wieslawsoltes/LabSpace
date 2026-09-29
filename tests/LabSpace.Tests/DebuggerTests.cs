using LabSpace.Core;
using LabSpace.Dataflow;
using LabSpace.Documents;
using LabSpace.Editing;
using Xunit;

namespace LabSpace.Tests;

public sealed class DebuggerTests
{
    private static InstrumentSession Session(VirtualInstrument? vi = null) => new(new() { Instruments = [vi ?? Examples.Arithmetic()] });
    private static void Finish(InstrumentSession session)
    {
        for (var i = 0; i < 1000 && session.IsRunning && !session.IsPaused; i++) session.Tick();
        Assert.False(session.IsRunning); Assert.False(session.IsPaused);
    }
    [Fact] public void DebuggerOptionsAndMarkersDoNotEditProjectOrHistory()
    {
        var s = Session(); var json = ProjectSerializer.Save(s.Project); var revision = s.Revision;
        s.Select(s.Diagram.Nodes[0].Id); s.ToggleBreakpoint(); s.SelectWire(s.Diagram.Wires[0].Id); s.ToggleProbe();
        s.SetDebuggingEnabled(false); s.SetRetainWireValues(false);
        Assert.Equal(json, ProjectSerializer.Save(s.Project)); Assert.Equal(revision, s.Revision); Assert.False(s.Dirty); Assert.False(s.CanUndo);
        Assert.True(s.IsBreakpoint(s.Diagram.Nodes[0])); Assert.True(s.IsProbe(s.Diagram.Wires[0]));
    }
    [Fact] public void ImportedBreakpointCanBeDisabledWithoutChangingImportedModel()
    {
        var vi = Examples.Arithmetic(); vi.Diagram.Nodes[0].Breakpoint = true; var s = Session(vi);
        s.ToggleBreakpoint(vi.Diagram.Nodes[0].Id); Assert.True(vi.Diagram.Nodes[0].Breakpoint); Assert.False(s.IsBreakpoint(vi.Diagram.Nodes[0]));
        s.Run(); Finish(s);
    }
    [Fact] public void ProbeChangesDoNotDiscardPausedExecution()
    {
        var s = Session(); s.StepInto(); var frame = s.DebugFrame; Assert.NotNull(frame);
        s.ToggleProbe(s.Diagram.Wires[0].Id); Assert.Same(frame, s.DebugFrame); Assert.True(s.IsPaused);
        s.Run(); Finish(s);
    }
    [Fact] public void DebuggingOverrideSuppressesBreakpointsAndSingleStepping()
    {
        var vi = Examples.Arithmetic(); vi.Diagram.Nodes[0].Breakpoint = true; var s = Session(vi); s.SetDebuggingEnabled(false);
        s.StepInto(); Assert.Null(s.DebugFrame); Assert.False(s.IsPaused);
        s.Run(); Finish(s); Assert.Equal(10, s.Values[vi.Diagram.Nodes.Single(n => n.Kind == "indicator").Id].Number);
    }
    [Fact] public void BreakpointOverridePausesBeforeTheSelectedNode()
    {
        var s = Session(); var node = s.Diagram.Nodes[2]; s.ToggleBreakpoint(node.Id); s.Run();
        Assert.True(s.IsPaused); Assert.DoesNotContain(node.Id, s.Values.Keys);
        s.Run(); Finish(s);
    }
    [Fact] public void CrossViProbesKeepValuesAfterSwitchingEditors()
    {
        var a = Examples.Arithmetic(); var b = Examples.Arithmetic(); b.Name = "Other.vi";
        var wire = a.Diagram.Wires[0]; wire.Probe = true;
        var s = new InstrumentSession(new() { Instruments = [a, b] }); s.Run(); Finish(s);
        var before = Assert.Single(s.GetProbes()); Assert.NotNull(before.Value); s.Switch(b.Id);
        var after = Assert.Single(s.GetProbes()); Assert.Equal(a.Name, after.Instrument); Assert.Equal(before.Value, after.Value); Assert.True(after.Retained);
        s.LocateProbe(after.Address); Assert.Equal(a.Id, s.ActiveId); Assert.Equal(wire.Id, s.SelectedWire);
    }
    [Fact] public void NodeIdentityIsScopedByViAndDiagramPath()
    {
        var a = Examples.Arithmetic(); var b = ProjectSerializer.Clone(new() { Instruments = [a] }).Instruments[0]; b.Id = Guid.NewGuid().ToString("N"); b.Name = "Same node IDs.vi";
        var s = new InstrumentSession(new() { Instruments = [a, b] }); var id = a.Diagram.Nodes[0].Id;
        s.ToggleBreakpoint(id); s.Switch(b.Id); Assert.False(s.IsBreakpoint(s.Find(id)!)); s.Switch(a.Id); Assert.True(s.IsBreakpoint(s.Find(id)!));
    }
    [Fact] public void HiddenSequenceFramesRetainTheirOwnValues()
    {
        var vi = AdvancedExamples.Sequence(); var sequence = vi.Diagram.Nodes.Single(n => n.Kind == "sequence");
        var wire = sequence.Frames[0].Diagram.Wires[0]; wire.Probe = true;
        var s = Session(vi); s.Run(); Finish(s); var probe = Assert.Single(s.GetProbes()); Assert.Equal(21, probe.Value!.Number);
        Assert.Contains("/frame:", probe.Address.Path); s.LocateProbe(probe.Address);
        Assert.Single(s.Path); Assert.Equal(21, s.OutputValue(wire.From, wire.Output)!.Number);
    }
    [Fact] public void LocatingAProbeInsideThePausedViDoesNotAbortTheActivation()
    {
        var vi = AdvancedExamples.Sequence(); var sequence = vi.Diagram.Nodes.Single(n => n.Kind == "sequence"); sequence.Frames[0].Diagram.Wires[0].Probe = true;
        var s = Session(vi); s.StepInto(); s.StepInto(); var active = s.DebugFrame;
        s.LocateProbe(Assert.Single(s.GetProbes()).Address); Assert.Same(active, s.DebugFrame); Assert.True(s.IsPaused);
    }
    [Fact] public void DisablingRetentionReleasesHistoryAndDoesNotDirtyTheProject()
    {
        var s = Session(); s.ToggleProbe(s.Diagram.Wires[0].Id); s.Run(); Finish(s); Assert.True(s.RetainedDebugBytes > 0);
        s.SetRetainWireValues(false); Assert.Equal(0, s.RetainedDebugBytes); Assert.Null(Assert.Single(s.GetProbes()).Value);
        s.Run(); Finish(s); Assert.Equal(0, s.RetainedDebugFrames); Assert.False(s.Dirty);
    }
    [Fact] public void CodeEditsInvalidateRetainedValues()
    {
        var s = Session(); s.ToggleProbe(s.Diagram.Wires[0].Id); s.Run(); Finish(s);
        s.Add("constant", 100, 100); Assert.Equal(0, s.RetainedDebugFrames); Assert.Null(Assert.Single(s.GetProbes()).Value);
    }
    [Fact] public void ReplacingTheProjectClearsOverridesAndRetainedFrames()
    {
        var s = Session(); s.ToggleProbe(s.Diagram.Wires[0].Id); s.Run(); Finish(s);
        s.Replace(new() { Instruments = [Examples.Arithmetic()] }); Assert.Empty(s.GetProbes()); Assert.Equal(0, s.RetainedDebugBytes);
    }
    [Fact] public void CompletedActivationObserverRunsOncePerFrame()
    {
        var runtime = new DataflowRuntime(); var paths = new List<string>(); runtime.FrameCompleted += f => { Assert.True(f.Completed); paths.Add(f.Path); };
        var frame = runtime.Run(GraphCompiler.Compile(AdvancedExamples.Sequence().Diagram)); var count = paths.Count;
        Assert.Equal(3, count); Assert.Equal("root", paths[^1]); frame.StepInto(); Assert.Equal(count, paths.Count);
    }
    [Fact] public void RetainedFrameCacheEvictsOldVisAtItsCountLimit()
    {
        // Fill the cache with stable nested activation paths without exceeding the 64-VI project limit.
        var project = new LabProject { Instruments = [Examples.Blank("Many frames.vi")] };
        for (var i = 0; i < 3; i++)
        {
            var sequence = Examples.NewNode("sequence", i * 500, 0); sequence.Frames.Clear();
            for (var j = 0; j < 48; j++) sequence.Frames.Add(new() { Label = "Frame " + j });
            sequence.Contract = new() { Outputs = [new() { Name = "result", UseDefaultIfUnwired = true }] };
            project.Instruments[0].Diagram.Nodes.Add(sequence);
        }
        var s = new InstrumentSession(project); s.Run(); Finish(s);
        Assert.InRange(s.RetainedDebugFrames, 1, 128); Assert.InRange(s.RetainedDebugBytes, 1, 16 * 1024 * 1024);
    }
}
