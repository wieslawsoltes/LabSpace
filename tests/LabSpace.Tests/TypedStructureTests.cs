using System.Collections.Immutable;
using LabSpace.Core;
using LabSpace.Dataflow;
using LabSpace.Documents;
using Xunit;

namespace LabSpace.Tests;

public sealed class TypedStructureTests
{
    private static (VirtualInstrument Vi, Node Loop) Example()
    {
        var vi = StructuredExamples.IndexedAccumulator(); return (vi, vi.Diagram.Nodes.Single(n => n.Kind == "for"));
    }
    private static ExecutionFrame Run(VirtualInstrument vi) => new DataflowRuntime().Run(GraphCompiler.Compile(vi.Diagram));
    [Fact] public void IndexedInputAndNamedOutputsExecute()
    {
        var (vi, loop) = Example(); var frame = Run(vi);
        Assert.Equal(15, frame.GetOutput(loop.Id, "state").Number);
        Assert.Equal(new double[] { 1, 3, 6, 10, 15 }, frame.GetOutput(loop.Id, "totals").Samples);
        Assert.Equal(15, frame.Values[vi.Diagram.Nodes.Single(n => n.Kind == "indicator").Id].Number);
    }
    [Fact] public void ExplicitCountAndShortestArrayBoundForLoop()
    {
        var (vi, loop) = Example(); loop.Parameters["count"] = 4;
        loop.Contract = loop.Contract! with { Inputs = loop.Contract.Inputs.Add(new() { Name = "shorter", Indexing = true }) };
        var shorter = Examples.NewNode("array", 0, 0); shorter.Text = "8,9"; vi.Diagram.Nodes.Add(shorter); Examples.Connect(vi.Diagram, shorter, loop, "shorter");
        var frame = Run(vi); Assert.Equal(3, frame.GetOutput(loop.Id, "state").Number); Assert.Equal(2, frame.GetOutput(loop.Id, "totals").Samples.Length);
    }
    [Fact] public void ZeroIterationsPreserveInitialStateButCollectNothing()
    {
        var (vi, loop) = Example(); loop.Parameters["count"] = 0; vi.Diagram.Nodes.Single(n => n.Kind == "control").Value = 42;
        var frame = Run(vi); Assert.Equal(42, frame.GetOutput(loop.Id, "state").Number); Assert.Empty(frame.GetOutput(loop.Id, "totals").Samples);
    }
    [Fact] public void EmptyIndexedInputExecutesZeroIterations()
    {
        var (vi, loop) = Example(); vi.Diagram.Nodes.Single(n => n.Kind == "array").Text = "";
        Assert.Empty(Run(vi).GetOutput(loop.Id, "totals").Samples);
    }
    [Fact] public void UninitializedRegistersPersistAcrossCompletedInvocations()
    {
        var (vi, loop) = Example(); loop.Contract = loop.Contract! with { Registers = [new() { Name = "state", Initialized = false }] };
        vi.Diagram.Wires.RemoveAll(w => w.Input == "initial:state"); var runtime = new DataflowRuntime(); var plan = GraphCompiler.Compile(vi.Diagram);
        Assert.Equal(15, runtime.Run(plan).GetOutput(loop.Id).Number); Assert.Equal(30, runtime.Run(plan).GetOutput(loop.Id).Number);
        runtime.Reset(); Assert.Equal(15, runtime.Run(plan).GetOutput(loop.Id).Number);
    }
    [Fact] public void InitializedRegistersResetEachInvocation()
    {
        var (vi, loop) = Example(); var runtime = new DataflowRuntime(); var plan = GraphCompiler.Compile(vi.Diagram);
        Assert.Equal(15, runtime.Run(plan).GetOutput(loop.Id).Number); Assert.Equal(15, runtime.Run(plan).GetOutput(loop.Id).Number);
    }
    [Fact] public void FailedFrameDoesNotCommitUninitializedState()
    {
        var (vi, loop) = Example(); loop.Contract = loop.Contract! with { Registers = [new() { Name = "state", Initialized = false }] };
        vi.Diagram.Wires.RemoveAll(w => w.Input == "initial:state");
        var zero = Examples.NewNode("constant", 0, 0); var divide = Examples.NewNode("divide", 0, 0); vi.Diagram.Nodes.AddRange([zero, divide]); Examples.Connect(vi.Diagram, loop, divide, "x"); Examples.Connect(vi.Diagram, zero, divide, "y");
        var runtime = new DataflowRuntime(); Assert.Throws<NodeExecutionException>(() => runtime.Run(GraphCompiler.Compile(vi.Diagram))); Assert.Equal(0, runtime.Frames);
        zero.Value = 1; Assert.Equal(15, runtime.Run(GraphCompiler.Compile(vi.Diagram)).GetOutput(loop.Id).Number);
    }
    [Fact] public void ConditionalIndexingIncludesOnlyMatchingIterations()
    {
        var (vi, loop) = Example(); loop.Contract = loop.Contract! with { Outputs = [loop.Contract.Outputs[0] with { Mode = TunnelMode.ConditionalIndexing }] };
        var sample = loop.Body!.Nodes.Single(n => n.Kind == "input" && n.Text == "sample"); var two = Examples.NewNode("constant", 0, 350); two.Value = 2;
        var greater = Examples.NewNode("greater", 200, 350); var include = StructuredExamples.Connector("output", "include", ValueKind.Boolean, 450, 350);
        loop.Body.Nodes.AddRange([two, greater, include]); Examples.Connect(loop.Body, sample, greater, "x"); Examples.Connect(loop.Body, two, greater, "y"); Examples.Connect(loop.Body, greater, include);
        Assert.Equal(new double[] { 6, 10, 15 }, Run(vi).GetOutput(loop.Id, "totals").Samples);
    }
    [Fact] public void ConcatenationFlattensArraysInIterationOrder()
    {
        var (vi, loop) = Example(); loop.Contract = loop.Contract! with { Outputs = [loop.Contract.Outputs[0] with { Type = ValueKind.Array, Mode = TunnelMode.Concatenating }] };
        var output = loop.Body!.Nodes.Single(n => n.Kind == "output" && n.Text == "totals"); output.DataType = ValueKind.Array;
        loop.Body.Wires.RemoveAll(w => w.To == output.Id); var array = Examples.NewNode("array", 200, 350); array.Text = "7,8"; loop.Body.Nodes.Add(array); Examples.Connect(loop.Body, array, output);
        Assert.Equal(Enumerable.Range(0, 5).SelectMany(_ => new double[] { 7, 8 }), Run(vi).GetOutput(loop.Id, "totals").Samples);
    }
    [Fact] public void StackedRegisterUsesPreviousIterationHistory()
    {
        var (vi, loop) = Example(); loop.Parameters["count"] = 3;
        loop.Contract = loop.Contract! with { Registers = [new() { Name = "state", HistoryDepth = 2 }] };
        var state = loop.Body!.Nodes.Single(n => n.Kind == "input" && n.Text == "state"); state.Text = "state:1";
        Assert.Equal(4, Run(vi).GetOutput(loop.Id).Number);
    }
    [Fact] public void TypedStringRegisterAccumulatesWithoutNumericConversion()
    {
        var loop = Examples.NewNode("for", 0, 0); loop.Parameters["count"] = 3;
        loop.Contract = new() { Registers = [new() { Name = "text", Type = ValueKind.String }], PrimaryOutput = "text" };
        var arg = StructuredExamples.Connector("input", "text", ValueKind.String, 0, 0); var suffix = Examples.NewNode("string", 0, 0); suffix.Text = "λ";
        var concat = Examples.NewNode("concat", 0, 0); var result = StructuredExamples.Connector("output", "text", ValueKind.String, 0, 0);
        loop.Body = new() { Nodes = [arg, suffix, concat, result] }; Examples.Connect(loop.Body, arg, concat, "x"); Examples.Connect(loop.Body, suffix, concat, "y"); Examples.Connect(loop.Body, concat, result);
        Assert.Equal("λλλ", new DataflowRuntime().Run(GraphCompiler.Compile(new() { Nodes = [loop] })).GetOutput(loop.Id).Text);
    }
    [Fact] public void CooperativeStepsYieldBeforeLargeLoopCompletes()
    {
        var n = Examples.NewNode("for", 0, 0); n.Parameters["count"] = 10000;
        var frame = new DataflowRuntime().Start(GraphCompiler.Compile(new() { Nodes = [n] }));
        for (var i = 0; i < 40; i++) frame.StepInto();
        Assert.False(frame.Completed); Assert.InRange(frame.EvaluatedNodes, 1, 40); Assert.NotNull(frame.ActiveFrame);
    }
    [Fact] public void CancellationDuringNestedSteppingDoesNotCommit()
    {
        var (vi, _) = Example(); using var cancel = new CancellationTokenSource(); var runtime = new DataflowRuntime(); var frame = runtime.Start(GraphCompiler.Compile(vi.Diagram), cancel.Token);
        for (var i = 0; i < 6; i++) frame.StepInto(); cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => { while (!frame.Completed) frame.StepInto(); }); Assert.Equal(0, runtime.Frames);
    }
    [Fact] public void UnknownNamedOutputIsRejected()
    {
        var (vi, _) = Example(); vi.Diagram.Wires.Last().Output = "does_not_exist";
        Assert.Contains(GraphCompiler.Validate(vi.Diagram), d => d.Code == "PORT");
    }
    [Fact] public void WrongNamedOutputTypeIsRejected()
    {
        var (vi, loop) = Example(); vi.Diagram.Wires.First(w => w.From == loop.Id).Output = "totals";
        Assert.Contains(GraphCompiler.Validate(vi.Diagram), d => d.Code == "TYPE");
    }
    [Fact] public void MissingConnectorIsRejected()
    {
        var (vi, loop) = Example(); var output = loop.Body!.Nodes.Single(n => n.Kind == "output" && n.Text == "totals"); loop.Body.Nodes.Remove(output); loop.Body.Wires.RemoveAll(w => w.To == output.Id);
        Assert.Contains(GraphCompiler.Validate(vi.Diagram), d => d.Code == "CONNECTOR");
    }
    [Fact] public void ExplicitDefaultPermitsAnUnwiredOutputConnector()
    {
        var (vi, loop) = Example(); loop.Contract = loop.Contract! with { Outputs = [loop.Contract.Outputs[0] with { UseDefaultIfUnwired = true }] };
        var output = loop.Body!.Nodes.Single(n => n.Kind == "output" && n.Text == "totals"); loop.Body.Wires.RemoveAll(w => w.To == output.Id);
        Assert.Equal(new double[5], Run(vi).GetOutput(loop.Id, "totals").Samples);
    }
    [Fact] public void DuplicateContractNamesAreRejected()
    {
        var (vi, loop) = Example(); loop.Contract = loop.Contract! with { Inputs = [loop.Contract.Inputs[0], loop.Contract.Inputs[0]] };
        Assert.Contains(GraphCompiler.Validate(vi.Diagram), d => d.Code == "CONTRACT");
    }
    [Fact] public void ContractDefinitionsCacheByIdentity()
    {
        var (_, loop) = Example(); var first = NodeCatalog.Resolve(loop); Assert.Same(first, NodeCatalog.Resolve(loop));
        loop.Contract = loop.Contract! with { PrimaryOutput = "totals" }; var second = NodeCatalog.Resolve(loop); Assert.NotSame(first, second); Assert.Equal(ValueKind.Array, second.Output);
    }
    [Fact] public void VersionTwoRoundTripsNamedPortsAndContracts()
    {
        var (vi, _) = Example(); var json = ProjectSerializer.Save(new() { Instruments = [vi] }); var project = ProjectSerializer.Load(json);
        Assert.Equal(2, project.FormatVersion); Assert.Equal(json, ProjectSerializer.Save(project)); Assert.Equal(15, Run(project.Instruments[0]).Values[vi.Diagram.Nodes.Single(n => n.Kind == "indicator").Id].Number);
    }
    [Fact] public void VersionOneMigratesWithoutLosingTheGraph()
    {
        var project = Examples.Create(); project.FormatVersion = 1; var loaded = ProjectSerializer.Load(ProjectSerializer.Save(project)); Assert.Equal(2, loaded.FormatVersion); Assert.Equal(13, loaded.Instruments[0].Diagram.Nodes.Count);
    }
    [Fact] public void UnknownConnectorTypesAreRejectedByTheImporter()
    {
        var (vi, loop) = Example(); loop.Contract = loop.Contract! with { Inputs = [loop.Contract.Inputs[0] with { Type = (ValueKind)999 }] };
        Assert.Throws<InvalidDataException>(() => ProjectSerializer.Save(new() { Instruments = [vi] }));
    }
}
