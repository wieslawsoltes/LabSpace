using LabSpace.Core;
using LabSpace.Dataflow;
using LabSpace.Documents;
using Xunit;

namespace LabSpace.Tests;

public sealed class AdvancedExecutionTests
{
    private static ExecutionFrame Run(Diagram graph) => new DataflowRuntime().Run(GraphCompiler.Compile(graph));
    private static Node Case(ValueKind kind, params StructureFrame[] frames) => new()
    { Kind = "case-multi", DataType = kind, Contract = new() { Outputs = [new() { Name = "result" }] }, Frames = frames.ToList() };
    [Theory]
    [InlineData("2 + 3 * 4", 14)] [InlineData("(2 + 3) * 4", 20)] [InlineData("2 ** 3 ** 2", 512)]
    [InlineData("0 ? 1 / 0 : 7", 7)] [InlineData("1 ? 8 : sqrt(-1)", 8)]
    [InlineData("0 && 1 / 0", 0)] [InlineData("1 || 1 / 0", 1)]
    [InlineData("2 && 3", 1)] [InlineData("0 || 3", 1)] [InlineData("!0 + !5", 1)]
    [InlineData("3 < 4 && 5 >= 5", 1)] [InlineData("3 == 3 && 4 != 3", 1)]
    [InlineData("clamp(20, 1, 10)", 10)] [InlineData("pow(2, 3) + abs(-2)", 10)]
    [InlineData("sqrt(9) + floor(1.9) + ceil(1.1)", 6)] [InlineData("1e-3 * 1e3", 1)]
    [InlineData("// comment\nresult = x + 2;", 5)] [InlineData("a = x * 2; /* comment */ result = a + 1;", 7)]
    public void FormulaRunsBoundedBytecode(string source, double expected)
        => Assert.Equal(expected, FormulaProgram.Compile(source, new()).Evaluate(_ => 3)["result"], 10);
    [Theory]
    [InlineData("result = unknown;")] [InlineData("result = result + 1;")] [InlineData("result = x +;")]
    [InlineData("result = system(x);")] [InlineData("result = sin(1, 2);")] [InlineData("/* unfinished")]
    [InlineData("result = 1e999;")] [InlineData("result = x @ 2;")]
    public void InvalidFormulaReportsSourceLocation(string source) => Assert.Throws<FormulaException>(() => FormulaProgram.Compile(source, new()));
    [Fact] public void FormulaHasDistinctNamedOutputs()
    {
        var formula = Examples.NewNode("formula", 0, 0); formula.Formula = new() { Inputs = [], Outputs = ["first", "second"] }; formula.Text = "first = 3; second = first * 4;";
        var frame = Run(new() { Nodes = [formula] }); Assert.Equal(3, frame.GetOutput(formula.Id, "first").Number); Assert.Equal(12, frame.GetOutput(formula.Id, "second").Number);
    }
    [Fact] public void MissingFormulaOutputBreaksGraph()
    {
        var formula = Examples.NewNode("formula", 0, 0); formula.Formula = new() { Inputs = [], Outputs = ["a", "b"] }; formula.Text = "a = 3;";
        Assert.Contains(GraphCompiler.Validate(new() { Nodes = [formula] }), d => d.Code == "FORMULA");
    }
    [Fact] public void FormulaDepthAndRuntimeErrorsAreBounded()
    {
        Assert.Throws<FormulaException>(() => FormulaProgram.Compile(new string('(', 100) + "1" + new string(')',100), new()));
        Assert.Throws<DivideByZeroException>(() => FormulaProgram.Compile("1 / 0", new()).Evaluate(_ => 0));
        Assert.Throws<ArithmeticException>(() => FormulaProgram.Compile("sqrt(-1)", new()).Evaluate(_ => 0));
    }
    [Theory] [InlineData(-50, 0)] [InlineData(0, 1)] [InlineData(1, 1)] [InlineData(2, 1)] [InlineData(9, 2)] [InlineData(1.5, 1)]
    public void NumericCasesHandleRangesDefaultsAndRounding(double number, int index)
    {
        var node = Case(ValueKind.Number, AdvancedExamples.ConstantFrame("..-1", 0), AdvancedExamples.ConstantFrame("0..2", 1), AdvancedExamples.ConstantFrame("", 2, true));
        Assert.Equal(index, CaseDispatchTable.Compile(node).Select(Value.Numeric(number)));
    }
    [Theory] [InlineData("run", 0)] [InlineData("start", 0)] [InlineData("idle", 1)] [InlineData("stop", 2)] [InlineData("RUN", 2)]
    public void StringCasesHandleListsAndDefault(string selector, int expected)
    {
        var node = Case(ValueKind.String, AdvancedExamples.ConstantFrame("\"run\", \"start\"", 0), AdvancedExamples.ConstantFrame("\"idle\"", 1), AdvancedExamples.ConstantFrame("", 2, true));
        Assert.Equal(expected, CaseDispatchTable.Compile(node).Select(Value.String(selector)));
    }
    [Fact] public void QuotedStringSeparatorsAndRangesRemainLiteral()
    {
        var node = Case(ValueKind.String, AdvancedExamples.ConstantFrame("\"a,b..c\"", 0), AdvancedExamples.ConstantFrame("\"m\"..\"z\"", 1), AdvancedExamples.ConstantFrame("", 2, true));
        var table = CaseDispatchTable.Compile(node); Assert.Equal(0, table.Select(Value.String("a,b..c"))); Assert.Equal(1, table.Select(Value.String("n"))); Assert.Equal(2, table.Select(Value.String("z")));
        node.Parameters["caseInsensitive"] = 1; Assert.Equal(1, CaseDispatchTable.Compile(node).Select(Value.String("N")));
    }
    [Fact] public void OverlappingAndMissingDefaultCasesAreRejected()
    {
        var node = Case(ValueKind.Number, AdvancedExamples.ConstantFrame("0..3", 0), AdvancedExamples.ConstantFrame("3..9", 1, true));
        Assert.Throws<ArgumentException>(() => CaseDispatchTable.Compile(node));
        node.Frames = [AdvancedExamples.ConstantFrame("0",0)]; Assert.Throws<ArgumentException>(() => CaseDispatchTable.Compile(node));
        node.DataType = ValueKind.String; node.Frames = [AdvancedExamples.ConstantFrame("\"a\"..\"z\"",0),AdvancedExamples.ConstantFrame("\"d\"",1,true)];
        Assert.Throws<ArgumentException>(() => CaseDispatchTable.Compile(node));
    }
    [Fact] public void UnselectedCaseNeverExecutes()
    {
        var vi = AdvancedExamples.CaseDispatch(); var node = vi.Diagram.Nodes.Single(n => n.Kind == "case-multi");
        var bad = node.Frames[1].Diagram; var zero = Examples.NewNode("constant", 0,0); var divide = Examples.NewNode("divide",0,0); bad.Nodes.AddRange([zero,divide]); Examples.Connect(bad, zero,divide,"x"); Examples.Connect(bad,zero,divide,"y");
        node.VisibleFrame = 1; Assert.Equal(42, Run(vi.Diagram).GetOutput(node.Id).Number);
    }
    [Fact] public void ErrorCasesDistinguishWarningAndCodeRange()
    {
        var node = Case(ValueKind.Error, AdvancedExamples.ConstantFrame("No Error",0), AdvancedExamples.ConstantFrame("Error 10..20",1),AdvancedExamples.ConstantFrame("",2,true));
        var table = CaseDispatchTable.Compile(node); Assert.Equal(0, table.Select(Value.ErrorValue(false,17,"warning"))); Assert.Equal(1,table.Select(Value.ErrorValue(true,17,"error"))); Assert.Equal(2,table.Select(Value.ErrorValue(true,4,"other")));
    }
    [Fact] public void SequenceExecutesTypedLocalsAndFormulaInOrder()
    {
        var vi = AdvancedExamples.Sequence(); var node = vi.Diagram.Nodes.Single(n => n.Kind == "sequence");
        Assert.Equal(84, Run(vi.Diagram).GetOutput(node.Id).Number);
        node.VisibleFrame = 1; Assert.Equal(84, Run(vi.Diagram).GetOutput(node.Id).Number);
        node.Frames.Reverse(); Assert.Contains(GraphCompiler.Validate(vi.Diagram), d => d.Code == "LOCAL");
    }
    [Fact] public void LocalCannotReadSameFrameOrOutsideSequence()
    {
        var read = Examples.NewNode("sequence-read",0,0); var write = Examples.NewNode("sequence-write",0,0);
        var zero = Examples.NewNode("constant",0,0); var graph = new Diagram { Nodes = [read,write,zero] }; Examples.Connect(graph,zero,write);
        Assert.Contains(GraphCompiler.Validate(graph), d => d.Code == "LOCAL");
        var sequence = Examples.NewNode("sequence",0,0); sequence.Frames = [new() { Diagram = graph }];
        Assert.Contains(GraphCompiler.Validate(new() { Nodes = [sequence] }), d => d.Code == "LOCAL");
    }
    [Fact] public void SequenceHasNestedSteppingAndSharedCancellation()
    {
        var vi = AdvancedExamples.Sequence(); using var cts = new CancellationTokenSource();
        var frame = new DataflowRuntime().Start(GraphCompiler.Compile(vi.Diagram), cts.Token);
        frame.StepInto(); frame.StepInto(); Assert.NotSame(frame,frame.ActiveFrame); Assert.False(frame.Completed);
        cts.Cancel(); Assert.Throws<OperationCanceledException>(frame.StepInto);
    }
    [Fact] public void ErrorUnbundleOutputsHaveDistinctTypes()
    {
        var vi = AdvancedExamples.ErrorsAndComplex(); var split = vi.Diagram.Nodes.Single(n => n.Kind == "error-unbundle"); var frame = Run(vi.Diagram);
        Assert.True(frame.GetOutput(split.Id,"status").Boolean); Assert.Equal(17,frame.GetOutput(split.Id,"code").Number); Assert.Equal("Acquisition unavailable",frame.GetOutput(split.Id,"source").Text);
        Assert.Equal(5,frame.Values[vi.Diagram.Nodes.Single(n => n.Kind == "complex-magnitude").Id].Number);
    }
    [Theory] [InlineData(false,1,true,2,2)] [InlineData(true,1,true,2,1)] [InlineData(false,1,false,2,1)] [InlineData(false,0,false,2,2)]
    public void MergeErrorsPrioritizesErrorThenWarning(bool statusA,int codeA,bool statusB,int codeB,int expected)
    {
        var a = Examples.NewNode("error-constant",0,0); a.Value = codeA; a.Parameters["status"] = statusA ? 1 : 0;
        var b = Examples.NewNode("error-constant",0,0); b.Value = codeB; b.Parameters["status"] = statusB ? 1 : 0;
        var merge = Examples.NewNode("error-merge",0,0); var graph = new Diagram { Nodes = [a,b,merge] }; Examples.Connect(graph,a,merge,"x"); Examples.Connect(graph,b,merge,"y");
        Assert.Equal(expected,Run(graph).GetOutput(merge.Id).Error.Code);
    }
    [Fact] public void ComplexPartsAndOperationsAreExecutable()
    {
        var a = Examples.NewNode("complex",0,0); a.Value = 3; a.Parameters["imaginary"] = 4;
        var conjugate = Examples.NewNode("complex-conjugate",0,0); var multiply = Examples.NewNode("complex-multiply",0,0); var parts = Examples.NewNode("complex-parts",0,0);
        var graph = new Diagram { Nodes = [a,conjugate,multiply,parts] }; Examples.Connect(graph,a,conjugate); Examples.Connect(graph,a,multiply,"x"); Examples.Connect(graph,conjugate,multiply,"y"); Examples.Connect(graph,multiply,parts);
        var frame = Run(graph); Assert.Equal(25,frame.GetOutput(parts.Id,"real").Number); Assert.Equal(0,frame.GetOutput(parts.Id,"imaginary").Number);
    }
    [Fact] public void VersionThreeRoundTripsEveryFrameAndSignature()
    {
        var project = Examples.Create(); var json = ProjectSerializer.Save(project); var loaded = ProjectSerializer.Load(json);
        Assert.Equal(3,loaded.FormatVersion); Assert.Equal(json,ProjectSerializer.Save(loaded));
        foreach (var vi in loaded.Instruments) Assert.True(Run(vi.Diagram).Completed);
    }
    [Fact] public void ImportRejectsDuplicateFramesAndUnknownWidgets()
    {
        var vi = AdvancedExamples.Sequence(); var node = vi.Diagram.Nodes[0]; node.Frames[1].Id = node.Frames[0].Id;
        Assert.Throws<InvalidDataException>(() => ProjectSerializer.Save(new() { Instruments = [vi] }));
    }
}
