using LabSpace.Core;
using LabSpace.Dataflow;
using LabSpace.Documents;
using LabSpace.Editing;
using Xunit;

namespace LabSpace.Tests;

public sealed class StructureEditingTests
{
    [Theory] [InlineData("for", 10)] [InlineData("while", 10)] [InlineData("subvi", 1)]
    public void LegacyConversionPreservesExecutionAndIsOneUndo(string kind, double expected)
    {
        var node = Examples.NewNode(kind, 200, 80); var vi = Examples.Blank(); vi.Diagram.Nodes.Add(node);
        var session = new InstrumentSession(new() { Instruments = [vi] }); var before = ProjectSerializer.Save(session.Project);
        session.ConfigureStructure(node.Id, InstrumentSession.ContractDraft(node));
        Assert.Empty(session.Diagnostics); Assert.Equal(expected, new DataflowRuntime().Run(GraphCompiler.Compile(session.Diagram)).GetOutput(node.Id).Number);
        session.Undo(); Assert.Equal(before, ProjectSerializer.Save(session.Project)); Assert.False(session.CanUndo);
    }
    [Fact] public void RemovingInitializationRemovesOnlyItsOuterWire()
    {
        var vi = StructuredExamples.IndexedAccumulator(); var node = vi.Diagram.Nodes.Single(n => n.Kind == "for"); var session = new InstrumentSession(new() { Instruments = [vi] });
        session.ConfigureStructure(node.Id, node.Contract! with { Registers = [new() { Name = "state", Initialized = false }] });
        Assert.DoesNotContain(session.Diagram.Wires, w => w.Input == "initial:state"); Assert.Empty(session.Diagnostics);
        session.Run(); while (session.IsRunning) session.Tick(); Assert.Equal(15, session.Values[node.Id].Number);
        session.Run(); while (session.IsRunning) session.Tick(); Assert.Equal(30, session.Values[node.Id].Number);
        session.Undo(); Assert.Contains(session.Diagram.Wires, w => w.Input == "initial:state");
    }
    [Fact] public void InvalidContractRollsBackBeforeChangingTheModel()
    {
        var vi = Examples.Loop(); var session = new InstrumentSession(new() { Instruments = [vi] }); var node = session.Diagram.Nodes.Single(n => n.Kind == "for"); var before = ProjectSerializer.Save(session.Project);
        Assert.Throws<GraphValidationException>(() => session.ConfigureStructure(node.Id, new() { Registers = [new() { Name = "i" }], PrimaryOutput = "i" }));
        Assert.Equal(before, ProjectSerializer.Save(session.Project));
    }
    [Fact] public void LargeLoopYieldsToTheSessionAndCanBeAborted()
    {
        var node = Examples.NewNode("for", 100, 80); node.Parameters["count"] = 10000;
        var vi = Examples.Blank(); vi.Diagram.Nodes.Add(node); var session = new InstrumentSession(new() { Instruments = [vi] });
        session.Run(); Assert.True(session.IsRunning); Assert.Equal(0, session.Frames); session.Abort(); Assert.False(session.IsRunning); Assert.Equal(0, session.Frames);
    }
    [Fact] public void EmptyNestedBodyStillObservesCancellation()
    {
        var node = new Node { Kind = "for", Contract = new(), Body = new() }; node.Parameters["count"] = 10000;
        using var cancellation = new CancellationTokenSource(); var runtime = new DataflowRuntime(); var frame = runtime.Start(GraphCompiler.Compile(new() { Nodes = [node] }), cancellation.Token);
        frame.StepInto(); frame.StepInto(); cancellation.Cancel(); Assert.Throws<OperationCanceledException>(frame.StepInto); Assert.Equal(0, runtime.Frames);
    }
    [Fact] public void WhileAutoIndexingContinuesPastTheInputArrayWithDefaults()
    {
        var vi = StructuredExamples.IndexedAccumulator(); var loop = vi.Diagram.Nodes.Single(n => n.Kind == "for"); loop.Kind = "while";
        var body = loop.Body!; var i = StructuredExamples.Connector("input", "i", ValueKind.Number, 0, 0); var seven = Examples.NewNode("constant", 0, 0); seven.Value = 6;
        var greater = Examples.NewNode("greater", 0, 0); var stop = Examples.NewNode("stop", 0, 0); body.Nodes.AddRange([i, seven, greater, stop]);
        Examples.Connect(body, i, greater, "x"); Examples.Connect(body, seven, greater, "y"); Examples.Connect(body, greater, stop);
        var frame = new DataflowRuntime().Run(GraphCompiler.Compile(vi.Diagram)); Assert.Equal(8, frame.GetOutput(loop.Id, "totals").Samples.Length); Assert.Equal(15, frame.GetOutput(loop.Id).Number);
    }
    [Theory] [InlineData(0, "false branch")] [InlineData(1, "true branch")]
    public void TypedCaseSelectsOnlyTheMatchingStringBranch(double selected, string expected)
    {
        var selector = Examples.NewNode("bool", 0, 0); selector.Value = selected;
        Diagram Body(string text) { var value = Examples.NewNode("string", 0, 0); value.Text = text; var output = StructuredExamples.Connector("output", "result", ValueKind.String, 100, 0); var body = new Diagram { Nodes = [value, output] }; Examples.Connect(body, value, output); return body; }
        var node = new Node { Kind = "case", Contract = new() { Outputs = [new() { Type = ValueKind.String }] }, Body = Body("true branch"), Alternative = Body("false branch") };
        var graph = new Diagram { Nodes = [selector, node] }; Examples.Connect(graph, selector, node, "selector"); Assert.Equal(expected, new DataflowRuntime().Run(GraphCompiler.Compile(graph)).GetOutput(node.Id).Text);
    }
}
