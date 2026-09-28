using LabSpace.Core;
using LabSpace.Dataflow;
using LabSpace.Documents;
using LabSpace.Editing;
using LabSpace.Signals;
using Xunit;

namespace LabSpace.Tests;

public sealed class EngineTests
{
    private static Value Evaluate(string kind, double x, double y = 0)
    {
        var d = new Diagram(); var a = Examples.NewNode("constant", 0, 0); a.Value = x; var b = Examples.NewNode("constant", 0, 0); b.Value = y; var op = Examples.NewNode(kind, 0, 0);
        d.Nodes.AddRange([a, b, op]); var def = NodeCatalog.Get(kind); if (def.Inputs.Length > 0) Examples.Connect(d, a, op, def.Inputs[0].Name); if (def.Inputs.Length > 1) Examples.Connect(d, b, op, def.Inputs[1].Name);
        return new DataflowRuntime().Run(GraphCompiler.Compile(d)).Values[op.Id];
    }
    [Theory]
    [InlineData("add", 3, 7, 10)] [InlineData("subtract", 3, 7, -4)] [InlineData("multiply", 3, 7, 21)] [InlineData("divide", 21, 7, 3)]
    [InlineData("power", 2, 8, 256)] [InlineData("min", 3, 7, 3)] [InlineData("max", 3, 7, 7)] [InlineData("abs", -4, 0, 4)]
    [InlineData("sqrt", 81, 0, 9)] [InlineData("sin", 0, 0, 0)] [InlineData("cos", 0, 0, 1)] [InlineData("round", 2.7, 0, 3)]
    public void NumericKernels(string kind, double x, double y, double expected) => Assert.Equal(expected, Evaluate(kind, x, y).Number, 10);
    [Theory] [InlineData("greater", 5, 2, true)] [InlineData("less", 5, 2, false)] [InlineData("equal", 5, 5, true)]
    public void ComparisonKernels(string kind, double x, double y, bool expected) => Assert.Equal(expected, Evaluate(kind, x, y).Boolean);
    [Fact] public void DivisionByZeroIsReported() => Assert.Throws<NodeExecutionException>(() => Evaluate("divide", 1, 0));
    [Fact] public void NonFiniteIsReported() => Assert.Throws<NodeExecutionException>(() => Evaluate("sqrt", -1));
    [Fact] public void BuiltInExamplesCompileAndExecute()
    {
        foreach (var vi in Examples.Create().Instruments) { var plan = GraphCompiler.Compile(vi.Diagram); var frame = new DataflowRuntime().Run(plan); Assert.True(frame.Completed); Assert.Equal(vi.Diagram.Nodes.Count, frame.Values.Count); }
    }
    [Fact] public void SignalExampleHasRealResults()
    {
        var vi = Examples.SignalAnalysis(); var frame = new DataflowRuntime().Run(GraphCompiler.Compile(vi.Diagram)); var rms = vi.Diagram.Nodes.Single(n => n.Kind == "rms");
        Assert.InRange(frame.Values[rms.Id].Number, 1.5, 1.9); Assert.Equal(512, frame.Values[vi.Diagram.Nodes.Single(n => n.Kind == "simulate").Id].Samples.Length);
    }
    [Fact] public void RequiredInputIsDiagnostic()
    {
        var d = new Diagram(); d.Nodes.Add(Examples.NewNode("add", 0, 0)); Assert.Equal(2, GraphCompiler.Validate(d).Count(x => x.Code == "UNWIRED"));
    }
    [Fact] public void WireTypeIsChecked()
    {
        var d = new Diagram(); var source = Examples.NewNode("bool", 0, 0); var target = Examples.NewNode("indicator", 0, 0); d.Nodes.AddRange([source, target]); Examples.Connect(d, source, target); Assert.Contains(GraphCompiler.Validate(d), x => x.Code == "TYPE");
    }
    [Fact] public void MultipleDriversAreRejected()
    {
        var d = Examples.Arithmetic().Diagram; var output = d.Nodes.Single(x => x.Kind == "indicator"); Examples.Connect(d, d.Nodes[0], output); Assert.Contains(GraphCompiler.Validate(d), x => x.Code == "DRIVER");
    }
    [Fact] public void CyclesAreRejected()
    {
        var d = new Diagram(); var a = Examples.NewNode("abs", 0, 0); var b = Examples.NewNode("abs", 0, 0); d.Nodes.AddRange([a, b]); Examples.Connect(d, a, b); Examples.Connect(d, b, a); Assert.Contains(GraphCompiler.Validate(d), x => x.Code == "CYCLE");
    }
    [Fact] public void FeedbackBreaksCycleAndCarriesState()
    {
        var d = new Diagram(); var f = Examples.NewNode("feedback", 0, 0); var one = Examples.NewNode("constant", 0, 0); one.Value = 1; var add = Examples.NewNode("add", 0, 0); d.Nodes.AddRange([add, one, f]); Examples.Connect(d, f, add, "x"); Examples.Connect(d, one, add, "y"); Examples.Connect(d, add, f);
        var plan = GraphCompiler.Compile(d); var runtime = new DataflowRuntime(); Assert.Equal(1, runtime.Run(plan).Values[add.Id].Number); Assert.Equal(2, runtime.Run(plan).Values[add.Id].Number); runtime.Reset(); Assert.Equal(1, runtime.Run(plan).Values[add.Id].Number);
    }
    [Theory] [InlineData("for", 10)] [InlineData("while", 10)] [InlineData("subvi", 1)]
    public void NestedStructuresExecute(string kind, double expected)
    {
        var d = new Diagram(); var node = Examples.NewNode(kind, 0, 0); d.Nodes.Add(node); Assert.Equal(expected, new DataflowRuntime().Run(GraphCompiler.Compile(d)).Values[node.Id].Number);
    }
    [Theory] [InlineData(1, 1)] [InlineData(0, 2)]
    public void CaseRunsSelectedBranch(double selector, double expected)
    {
        var d = new Diagram(); var b = Examples.NewNode("bool", 0, 0); b.Value = selector; var c = Examples.NewNode("case", 0, 0); d.Nodes.AddRange([b, c]); Examples.Connect(d, b, c, "selector"); Assert.Equal(expected, new DataflowRuntime().Run(GraphCompiler.Compile(d)).Values[c.Id].Number);
    }
    [Fact] public void LoopHasSharedBudget()
    {
        var d = new Diagram(); d.Nodes.Add(Examples.NewNode("for", 0, 0)); Assert.Throws<NodeExecutionException>(() => new DataflowRuntime().Run(GraphCompiler.Compile(d), maximumNodes: 3));
    }
    [Fact] public void CancellationIsObserved()
    {
        var source = new CancellationTokenSource(); source.Cancel(); Assert.Throws<OperationCanceledException>(() => new DataflowRuntime().Run(GraphCompiler.Compile(Examples.Arithmetic().Diagram), source.Token));
    }
    [Fact] public void SteppingRetainsPartialValues()
    {
        var frame = new DataflowRuntime().Start(GraphCompiler.Compile(Examples.Arithmetic().Diagram)); frame.Step(); Assert.Single(frame.Values); Assert.False(frame.Completed); while (!frame.Completed) frame.Step(); Assert.Equal(4, frame.Values.Count);
    }
    [Fact] public void SerializedExamplesRoundTrip()
    {
        var json = ProjectSerializer.Save(Examples.Create()); Assert.Equal(json, ProjectSerializer.Save(ProjectSerializer.Load(json)));
    }
    [Fact] public void UnknownFormatIsRejected()
    {
        var project = Examples.Create(); project.FormatVersion = 999; Assert.Throws<InvalidDataException>(() => ProjectSerializer.Save(project));
    }
    [Fact] public void OversizedInputIsRejected() => Assert.Throws<InvalidDataException>(() => ProjectSerializer.Load(new string(' ', ProjectSerializer.MaximumBytes + 1)));
    [Fact] public void InputArraysCannotBeMutated()
    {
        var data = new[] { 1.0, 2.0 }; var value = Value.Vector(data); data[0] = 9; Assert.Equal(1, value.Samples[0]);
    }
    [Theory] [InlineData(0, 0)] [InlineData(2, 2)] [InlineData(1e200, 1e200)]
    public void StableRms(double input, double expected) => Assert.Equal(expected, SignalMath.Rms([input, input]));
    [Fact] public void SineRmsIsCorrect() => Assert.InRange(SignalMath.Rms(SignalMath.Generate(1024, 1024, 32, 2, 0).Samples), Math.Sqrt(2) - 1e-10, Math.Sqrt(2) + 1e-10);
    [Fact] public void SpectrumFindsFrequencyAndAmplitude()
    {
        var result = SignalMath.Spectrum(SignalMath.Generate(1024, 1024, 32, 2, 0)); Assert.Equal(513, result.Samples.Length); Assert.Equal(32, result.Samples.IndexOf(result.Samples.Max())); Assert.InRange(result.Samples[32], 1.999, 2.001);
    }
    [Fact] public void InvalidFftLengthIsRejected() => Assert.Throws<ArgumentException>(() => SignalMath.Spectrum(SignalMath.Generate(33, 1024, 20, 1, 0)));
    [Fact] public void MovingAverageIsCausal() => Assert.Equal(new[] { 1.0, 1.5, 2.5 }, SignalMath.MovingAverage(Value.Series([1.0, 2.0, 3.0], 1), 2).Samples);
    [Fact] public void ParameterEditUndoRedo()
    {
        var s = new InstrumentSession(Examples.Create()); var n = s.Diagram.Nodes.First(x => x.Kind == "control"); s.SetValue(n.Id, 8); Assert.Equal(8, s.Find(n.Id)!.Value); s.Undo(); Assert.Equal(2.5, s.Find(n.Id)!.Value); s.Redo(); Assert.Equal(8, s.Find(n.Id)!.Value);
    }
    [Fact] public void DeleteIsAtomicAndUndoRestoresWires()
    {
        var s = new InstrumentSession(Examples.Create()); var before = ProjectSerializer.Save(s.Project); s.Select(s.Diagram.Nodes.First().Id); s.Delete(); s.Undo(); Assert.Equal(before, ProjectSerializer.Save(s.Project));
    }
    [Fact] public void CopyPasteRemapsInternalWires()
    {
        var s = new InstrumentSession(new() { Instruments = [Examples.Arithmetic()] }); s.SelectAll(); s.Copy(); s.Paste(); Assert.Equal(8, s.Diagram.Nodes.Count); Assert.Equal(6, s.Diagram.Wires.Count); Assert.Empty(GraphCompiler.Validate(s.Diagram));
    }
    [Fact] public void GeometryGestureCreatesOneUndo()
    {
        var s = new InstrumentSession(Examples.Create()); var n = s.Diagram.Nodes[0]; var x = n.X; s.BeginGesture(); n.X += 20; n.X += 30; s.EndGesture(); s.Undo(); Assert.Equal(x, s.Diagram.Nodes[0].X); Assert.False(s.CanUndo);
    }
    [Fact] public void SelectionDoesNotDirtyDocument()
    {
        var s = new InstrumentSession(Examples.Create()); s.Select(s.Diagram.Nodes[0].Id); Assert.False(s.Dirty); Assert.False(s.CanUndo);
    }
    [Fact] public void BrokenDiagramCannotRun()
    {
        var s = new InstrumentSession(new() { Instruments = [Examples.Blank()] }); s.Add("add", 10, 10); s.Run(); Assert.False(s.IsRunning); Assert.Contains("unwired", s.Status);
    }
    [Fact] public void BreakpointPausesBeforeNode()
    {
        var vi = Examples.Arithmetic(); vi.Diagram.Nodes[2].Breakpoint = true; var s = new InstrumentSession(new() { Instruments = [vi] }); s.Run(); Assert.True(s.IsPaused); Assert.False(s.Values.ContainsKey(vi.Diagram.Nodes[2].Id)); s.Run(); Assert.False(s.IsPaused); Assert.Equal(10, s.Values[vi.Diagram.Nodes[3].Id].Number);
    }
    [Fact] public void LoopBodyNavigationAndUndoWork()
    {
        var vi = Examples.Loop(); var s = new InstrumentSession(new() { Instruments = [vi] }); s.Enter(vi.Diagram.Nodes.Single(n => n.Kind == "for").Id); Assert.Single(s.Path); s.Add("constant", 0, 0); s.Undo(); Assert.Equal(4, s.Diagram.Nodes.Count); s.Leave(); Assert.Empty(s.Path);
    }
    [Fact] public void InvalidConnectionRollsBack()
    {
        var s = new InstrumentSession(Examples.Create()); var before = ProjectSerializer.Save(s.Project); Assert.Throws<ArgumentException>(() => s.Connect(s.Diagram.Nodes.First(x => x.Kind == "control").Id, s.Diagram.Nodes.First(x => x.Kind == "graph").Id, "x")); Assert.Equal(before, ProjectSerializer.Save(s.Project));
    }
    [Fact] public void AutoLayoutKeepsExecutionResults()
    {
        var s = new InstrumentSession(new() { Instruments = [Examples.Arithmetic()] }); s.AutoLayout(); s.Run(); Assert.Equal(10, s.Values[s.Diagram.Nodes.Last().Id].Number);
    }
}
