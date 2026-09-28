using System.Numerics;
using LabSpace.Core;
using LabSpace.Dataflow;
using LabSpace.Documents;
using LabSpace.Editing;
using Xunit;

namespace LabSpace.Tests;

public sealed class TypedExecutionTests
{
    private static ExecutionFrame Run(Diagram graph, DataflowRuntime? runtime = null) => (runtime ?? new()).Run(GraphCompiler.Compile(graph));
    private static Node Loop(VirtualInstrument vi) => vi.Diagram.Nodes.Single(n => n.Kind == "for-loop");
    [Theory]
    [InlineData(ValueKind.Int8, "127", "127")]
    [InlineData(ValueKind.UInt8, "255", "255")]
    [InlineData(ValueKind.Int16, "-32768", "-32768")]
    [InlineData(ValueKind.UInt16, "65535", "65535")]
    [InlineData(ValueKind.Int32, "-2147483648", "-2147483648")]
    [InlineData(ValueKind.UInt32, "4294967295", "4294967295")]
    [InlineData(ValueKind.Int64, "-9223372036854775808", "-9223372036854775808")]
    [InlineData(ValueKind.UInt64, "18446744073709551615", "18446744073709551615")]
    public void ExactIntegerLiterals(ValueKind kind, string text, string expected) => Assert.Equal(expected, ValueLiteral.Parse(LabType.Scalar(kind), text).ToString());
    [Fact] public void IntegerOverflowIsRejectedInLiterals() => Assert.Throws<OverflowException>(() => ValueLiteral.Parse(LabType.Scalar(ValueKind.UInt8), "256"));
    [Fact] public void StructuralEqualityIncludesRankAndOrderedFields()
    {
        var a = LabType.Cluster(new("id", LabType.Int32)); var b = LabType.Cluster(new("id", LabType.Int32));
        Assert.Equal(a, b); Assert.Equal(a.GetHashCode(), b.GetHashCode()); Assert.NotEqual(LabType.Array(a), LabType.Array(b, 2));
        Assert.NotEqual(a, LabType.Cluster(new("other", LabType.Int32)));
    }
    [Fact] public void RectangularArrayRetainsRankAndIndexesRows()
    {
        var array = ValueLiteral.Parse(LabType.Array(LabType.Int32, 2), "[[1,2],[3,4]]");
        Assert.Equal(new[] { 2, 2 }, array.Shape); Assert.Equal(new long[] { 3, 4 }, array.IndexFirst(1).Elements.Select(v => v.Integer));
        Assert.Equal(new long[] { 0, 0 }, array.IndexFirst(9).Elements.Select(v => v.Integer));
    }
    [Fact] public void RaggedArrayIsRejected() => Assert.Throws<ArgumentException>(() => ValueLiteral.Parse(LabType.Array(LabType.Number, 2), "[[1],[2,3]]"));
    [Fact] public void ClusterRejectsDuplicateOrUnknownFields()
    {
        var type = LabType.Cluster(new("x", LabType.Number));
        Assert.Throws<ArgumentException>(() => ValueLiteral.Parse(type, "{\"x\":1,\"x\":2}")); Assert.Throws<ArgumentException>(() => ValueLiteral.Parse(type, "{\"x\":1,\"y\":2}"));
    }
    [Fact] public void IntegerArithmeticWrapsWithoutDoublePrecisionLoss()
    {
        var type = LabType.Scalar(ValueKind.UInt64); var max = Value.UnsignedInteger(ulong.MaxValue);
        Assert.Equal(0UL, ValueConversion.Binary("add", max, Value.UnsignedInteger(1), type).Unsigned);
        Assert.Equal(ulong.MaxValue - 1, ValueConversion.Binary("subtract", max, Value.UnsignedInteger(1), type).Unsigned);
    }
    [Theory] [InlineData(2.5, 2)] [InlineData(3.5, 4)] [InlineData(1e20, int.MaxValue)] [InlineData(-1e20, int.MinValue)]
    public void RealToIntegerRoundsEvenAndSaturates(double number, int expected) => Assert.Equal(expected, ValueConversion.Convert(Value.Numeric(number), LabType.Int32).Integer);
    [Fact] public void ComplexArithmeticIsExecutable()
    {
        var value = ValueConversion.Binary("multiply", Value.ComplexNumber(new(2, 3)), Value.ComplexNumber(new(4, -5)), LabType.Scalar(ValueKind.Complex)); Assert.Equal(new Complex(23, 2), value.Complex);
    }
    [Fact] public void TypeCoercionIsSharedByCompilerAndRuntime()
    {
        var vi = Examples.Arithmetic(); var old = vi.Diagram.Nodes[0]; var typed = Examples.Typed("typed-constant", "x", LabType.Int32, 0, 0, "3"); typed.Id = old.Id; vi.Diagram.Nodes[0] = typed;
        Assert.Empty(GraphCompiler.Validate(vi.Diagram)); Assert.Equal(10, Run(vi.Diagram).Values[vi.Diagram.Nodes.Last().Id].Number);
    }
    [Fact] public void UnbundleHasIndependentExactOutputs()
    {
        var vi = Examples.TypedData(); var frame = Run(vi.Diagram); var unbundle = vi.Diagram.Nodes.Single(n => n.Kind == "unbundle");
        Assert.Equal(ulong.MaxValue, frame.Read(new(unbundle.Id, "serial")).Unsigned); Assert.Equal(23.75, frame.Read(new(unbundle.Id, "temperature")).Number); Assert.True(frame.Read(new(unbundle.Id, "valid")).Boolean);
    }
    [Fact] public void ForAutoIndexBuildsPrefixesAndExposesFinalRegister()
    {
        var vi = Examples.IndexedLoop(); var loop = Loop(vi); var frame = Run(vi.Diagram);
        Assert.Equal(new double[] { 1, 3, 6, 10, 15 }, frame.Values[loop.Id].Samples); Assert.Equal(15, frame.Read(new(loop.Id, "shift:sum")).Number);
    }
    [Fact] public void ForUsesShortestOfIndexedArraysAndExplicitCount()
    {
        var vi = Examples.IndexedLoop(); var loop = Loop(vi); loop.Contract!.Inputs.Add(new() { Name = "short", Mode = TunnelMode.Indexing });
        var bound = Examples.Typed("typed-constant", "short", LabType.Array(LabType.Number), 0, 0, "[9,8]"); vi.Diagram.Nodes.Add(bound); Examples.Connect(vi.Diagram, bound, loop, "short");
        Assert.Equal(new double[] { 1, 3 }, Run(vi.Diagram).Values[loop.Id].Samples);
        loop.Parameters["count"] = 1; Assert.Equal(new double[] { 1 }, Run(vi.Diagram).Values[loop.Id].Samples);
    }
    [Theory] [InlineData(0)] [InlineData(-4)]
    public void ZeroIterationKeepsInitialRegisterAndEmitsEmptyArray(int count)
    {
        var vi = Examples.IndexedLoop(); var loop = Loop(vi); loop.Parameters["count"] = count; loop.Contract!.Registers[0].InitialValue = "42";
        var frame = Run(vi.Diagram); Assert.Empty(frame.Values[loop.Id].Samples); Assert.Equal(42, frame.Read(new(loop.Id, "shift:sum")).Number);
    }
    [Fact] public void EmptyIndexedInputRunsZeroTimes()
    {
        var vi = Examples.IndexedLoop(); vi.Diagram.Nodes[0].Text = "[]"; var frame = Run(vi.Diagram); Assert.Empty(frame.Values[Loop(vi).Id].Samples);
    }
    [Fact] public void StackedShiftRegisterReadsOlderIterations()
    {
        var vi = Examples.IndexedLoop(); vi.Diagram.Nodes[0].Text = "[1,1,1,1,1]"; var loop = Loop(vi); loop.Contract!.Registers[0].Depth = 2;
        loop.Body!.Nodes.Single(n => n.Kind == "shift-read").Parameters["element"] = 1;
        Assert.Equal(new double[] { 1, 1, 2, 2, 3 }, Run(vi.Diagram).Values[loop.Id].Samples);
    }
    [Fact] public void UninitializedRegisterPersistsAndResetClearsIt()
    {
        var vi = Examples.IndexedLoop(); var loop = Loop(vi); loop.Contract!.Registers[0].Initialized = false; var runtime = new DataflowRuntime();
        Assert.Equal(15, Run(vi.Diagram, runtime).Read(new(loop.Id, "shift:sum")).Number); Assert.Equal(30, Run(vi.Diagram, runtime).Read(new(loop.Id, "shift:sum")).Number);
        runtime.Reset(); Assert.Equal(15, Run(vi.Diagram, runtime).Read(new(loop.Id, "shift:sum")).Number);
    }
    [Fact] public void ConditionalIndexingUsesBooleanIncludeWire()
    {
        var vi = Examples.IndexedLoop(); var loop = Loop(vi); loop.Contract!.Outputs[0].Conditional = true;
        var greater = Examples.NewNode("greater", 200, 350); var limit = Examples.NewNode("constant", 30, 350); limit.Value = 2;
        var body = loop.Body!; body.Nodes.AddRange([greater, limit]); Examples.Connect(body, body.Nodes.Single(n => n.Kind == "tunnel-in"), greater, "x"); Examples.Connect(body, limit, greater, "y"); Examples.Connect(body, greater, body.Nodes.Single(n => n.Kind == "tunnel-out"), "include");
        Assert.Equal(new double[] { 6, 10, 15 }, Run(vi.Diagram).Values[loop.Id].Samples);
    }
    [Fact] public void MissingConditionalWireIsDiagnostic()
    {
        var vi = Examples.IndexedLoop(); Loop(vi).Contract!.Outputs[0].Conditional = true; Assert.Contains(GraphCompiler.Validate(vi.Diagram), d => d.Code == "CONDITION");
    }
    [Fact] public void ConcatenatingTunnelFlattensIterationArrays()
    {
        var vi = Examples.IndexedLoop(); var loop = Loop(vi); var body = loop.Body!; var sink = body.Nodes.Single(n => n.Kind == "tunnel-out");
        loop.Contract!.Outputs[0].Mode = TunnelMode.Concatenating; loop.Contract.Outputs[0].Type = sink.Type = LabType.Array(LabType.Number);
        var build = Examples.NewNode("array-build", 230, 320); body.Nodes.Add(build); var item = body.Nodes.Single(n => n.Kind == "tunnel-in"); Examples.Connect(body, item, build, "x"); Examples.Connect(body, item, build, "y"); body.Wires.RemoveAll(w => w.To == sink.Id); Examples.Connect(body, build, sink);
        Assert.Equal(new double[] { 1, 1, 2, 2, 3, 3, 4, 4, 5, 5 }, Run(vi.Diagram).Values[loop.Id].Samples);
    }
    [Fact] public void WhileExecutesAtLeastOnceAndStopsAtCondition()
    {
        var graph = new Diagram(); var loop = Examples.NewNode("while-loop", 0, 0); graph.Nodes.Add(loop); Assert.Equal(10, Run(graph).Values[loop.Id].Number);
    }
    [Fact] public void FailedFrameDoesNotCommitRegisterState()
    {
        var vi = Examples.IndexedLoop(); var loop = Loop(vi); loop.Contract!.Registers[0].Initialized = false; var runtime = new DataflowRuntime(); Run(vi.Diagram, runtime);
        var zero = Examples.NewNode("constant", 0, 0); var division = Examples.NewNode("divide", 0, 0); vi.Diagram.Nodes.AddRange([zero, division]); Examples.Connect(vi.Diagram, loop, division, "x"); vi.Diagram.Wires.Last().Output = "shift:sum"; Examples.Connect(vi.Diagram, zero, division, "y");
        Assert.Throws<NodeExecutionException>(() => Run(vi.Diagram, runtime)); Assert.Equal(1, runtime.Frames);
        vi.Diagram.Nodes.Remove(zero); vi.Diagram.Nodes.Remove(division); vi.Diagram.Wires.RemoveAll(w => w.To == division.Id);
        Assert.Equal(30, Run(vi.Diagram, runtime).Read(new(loop.Id, "shift:sum")).Number);
    }
    [Fact] public void FaultedFrameCannotResumeOrCommit()
    {
        var vi = Examples.IndexedLoop(); var runtime = new DataflowRuntime(); var frame = runtime.Start(GraphCompiler.Compile(vi.Diagram), maximumNodes: 3);
        Assert.ThrowsAny<Exception>(() => { while (!frame.Completed) frame.Step(); }); Assert.True(frame.Faulted); Assert.Throws<InvalidOperationException>(frame.Step); Assert.Equal(0, runtime.Frames);
    }
    [Fact] public void ContractAndNamedWiresRoundTrip()
    {
        var project = new LabProject { Instruments = [Examples.IndexedLoop(), Examples.TypedData()] }; var json = ProjectSerializer.Save(project); Assert.Equal(json, ProjectSerializer.Save(ProjectSerializer.Load(json))); Assert.Contains("shift:sum", json);
    }
    [Fact] public void FormatOneProjectsMigrateBeforeEditing()
    {
        var json = ProjectSerializer.Save(new() { Instruments = [Examples.Arithmetic()] }).Replace("\"formatVersion\": 2", "\"formatVersion\": 1"); Assert.Equal(2, ProjectSerializer.Load(json).FormatVersion);
    }
    [Fact] public void SessionCanWireNonPrimaryOutputAndUndoIt()
    {
        var vi = Examples.TypedData(); var session = new InstrumentSession(new() { Instruments = [vi] }); var split = vi.Diagram.Nodes.Single(n => n.Kind == "unbundle"); var numeric = vi.Diagram.Nodes.Single(n => n.Kind == "indicator");
        session.Connect(split.Id, numeric.Id, "x", "serial"); Assert.Equal("serial", session.Diagram.Wires.Single(w => w.To == numeric.Id).Output); session.Undo(); Assert.Equal("temperature", session.Diagram.Wires.Single(w => w.To == numeric.Id).Output);
    }
    [Fact] public void ContractEditsSynchronizeBothCaseBranchesAndUndo()
    {
        var node = Examples.NewNode("case-typed", 0, 0); var session = new InstrumentSession(new() { Instruments = [new() { Diagram = new() { Nodes = [node] } }] });
        var contract = new StructureContract { Inputs = [new() { Name = "flag", Type = LabType.Boolean }], Outputs = [new() { Type = LabType.Boolean }] };
        session.ConfigureStructure(node.Id, contract); Assert.All(node.Body!.Nodes, n => Assert.Equal(LabType.Boolean, n.Type)); Assert.All(node.Alternative!.Nodes, n => Assert.Equal(LabType.Boolean, n.Type));
        session.Undo(); Assert.Equal("x", session.Find(node.Id)!.Contract!.Inputs[0].Name);
    }
    [Fact] public void SeededRandomRestartsOnReset()
    {
        var graph = new Diagram { Nodes = [Examples.NewNode("random", 0, 0)] }; var runtime = new DataflowRuntime(); var first = Run(graph, runtime).Values.Values.Single().Number; Run(graph, runtime); runtime.Reset(); Assert.Equal(first, Run(graph, runtime).Values.Values.Single().Number);
    }
}
