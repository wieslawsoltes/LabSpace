using LabSpace.Core;
using LabSpace.Dataflow;
using LabSpace.Documents;
using Xunit;

namespace LabSpace.Tests;

public sealed class ProgrammingTests
{
    private static StructureContract FormulaContract() => new() { Inputs = [new() { Name = "x" }], Outputs = [new() { Name = "result" }] };
    private static double Formula(string source, double x = 3) => FormulaProgram.Compile(source, FormulaContract()).Evaluate(_ => Value.Numeric(x))["result"].Number;
    [Theory]
    [InlineData("result=x*x+2;", 11)]
    [InlineData("result=sin(pi/2)+pow(x,2);", 10)]
    [InlineData("result=1+2*3;", 7)]
    [InlineData("result=(1+2)*3;", 9)]
    [InlineData("result=2**3**2;", 512)]
    [InlineData("result=x>0?7:1/0;", 7)]
    [InlineData("result=0&&1/0;", 0)]
    [InlineData("result=1||1/0;", 1)]
    [InlineData("result=0||2;", 1)]
    [InlineData("result=1&&2;", 1)]
    [InlineData("if(x>0){result=5;}else{result=1/0;}", 5)]
    [InlineData("float64 i=0; result=0; while(i<5){result+=i;i++;}", 10)]
    [InlineData("result=0; for(float64 i=0;i<5;i++){if(i==2)continue; result+=i;}", 8)]
    [InlineData("result=0; for(float64 i=0;i<50;i++){if(i==4)break; result+=i;}", 6)]
    [InlineData("result=0; do {result++;}while(result<3);", 3)]
    [InlineData("// header\nresult=1;/* comment */result+=2;", 3)]
    [InlineData("result=ln(exp(3))+log(100);", 5)]
    [InlineData("float64 i=0; result=0; while(i<3){i++;float64 j=0;while(j<2){j++;result++;}}", 6)]
    public void FormulaExecutesScalarBytecode(string source, double expected) => Assert.Equal(expected, Formula(source), 10);
    [Theory]
    [InlineData("result=foo;")]
    [InlineData("result=System();")]
    [InlineData("result=1")]
    [InlineData("result=;")]
    [InlineData("result=x[0];")]
    [InlineData("break; result=1;")]
    [InlineData("while(1){result=1;")]
    [InlineData("result=1;/*")]
    [InlineData("float64 x=1; result=x;")]
    public void FormulaRejectsInvalidSource(string source) => Assert.Throws<ArgumentException>(() => FormulaProgram.Compile(source, FormulaContract()));
    [Fact] public void FormulaFuelBoundsInfiniteLoops() => Assert.Throws<ExecutionLimitException>(() => Formula("while(1){result=1;}"));
    [Fact] public void FormulaCancellationChecked() { using var c = new CancellationTokenSource(); c.Cancel(); Assert.Throws<OperationCanceledException>(() => FormulaProgram.Compile("result=x;", FormulaContract()).Evaluate(_ => Value.Numeric(1), new(100, c.Token))); }
    [Fact] public void FormulaUninitializedOutputRejectedAtRuntime() => Assert.Throws<ArithmeticException>(() => Formula("if(x<0)result=1;"));
    [Fact] public void FormulaGraphReportsSyntaxDiagnostic()
    {
        var n = Examples.NewNode("formula", 0, 0); n.Text = "result=;"; n.Contract = n.Contract! with { Inputs = [] };
        Assert.Contains(GraphCompiler.Validate(new() { Nodes = [n] }), d => d.Code == "FORMULA");
    }
    [Theory]
    [InlineData(-10, -1)] [InlineData(0, 0)] [InlineData(5, 1)] [InlineData(50, 99)] [InlineData(0.5, 0)] [InlineData(1.5, 1)]
    public void NumericCaseDispatch(double input, double expected)
    {
        var vi = ProgrammingExamples.MultiCase(); vi.Diagram.Nodes.Single(n => n.Kind == "control").Value = input;
        var result = new DataflowRuntime().Run(GraphCompiler.Compile(vi.Diagram)); Assert.Equal(expected, result.Values[vi.Diagram.Nodes.Single(n => n.Kind == "indicator").Id].Number);
    }
    [Fact] public void NumericOverlapRejected()
    {
        var vi = ProgrammingExamples.MultiCase(); vi.Diagram.Nodes.Single(n => n.Kind == "case").Frames[1].Selector = "-1..1";
        Assert.Contains(GraphCompiler.Validate(vi.Diagram), d => d.Code == "FRAMES");
    }
    [Fact] public void AllBranchesValidatedNotOnlySelected()
    {
        var vi = ProgrammingExamples.MultiCase(); vi.Diagram.Nodes.Single(n => n.Kind == "case").Frames[0].Diagram.Wires.Clear();
        Assert.Contains(GraphCompiler.Validate(vi.Diagram), d => d.Code == "UNWIRED");
    }
    [Fact] public void StringCaseHandlesQuotedCommasAndInsensitiveMatch()
    {
        var n = new Node { Kind = "case", Contract = new() { SelectorType = ValueKind.String, CaseInsensitive = true, Outputs = [new() { Name = "result" }] }, Frames = [ProgrammingExamples.ConstantFrame("\"a,b\",\"ready\"", 10), ProgrammingExamples.ConstantFrame("", 0, true)] };
        var input = Examples.NewNode("string", 0, 0); input.Text = "READY"; var d = new Diagram { Nodes = [input, n] }; Examples.Connect(d, input, n, "selector");
        Assert.Equal(10, new DataflowRuntime().Run(GraphCompiler.Compile(d)).Values[n.Id].Number);
        input.Text = "a,b"; Assert.Equal(10, new DataflowRuntime().Run(GraphCompiler.Compile(d)).Values[n.Id].Number);
    }
    [Fact] public void ErrorCaseUsesStatusNotWarningCode()
    {
        var n = new Node { Kind = "case", Contract = new() { SelectorType = ValueKind.Error, Outputs = [new() { Name = "result" }] }, Frames = [ProgrammingExamples.ConstantFrame("Error", 1), ProgrammingExamples.ConstantFrame("No Error", 0)] };
        var input = Examples.NewNode("error-constant", 0, 0); input.Value = 55; var d = new Diagram { Nodes = [input, n] }; Examples.Connect(d, input, n, "selector");
        Assert.Equal(0, new DataflowRuntime().Run(GraphCompiler.Compile(d)).Values[n.Id].Number);
        input.Parameters["status"] = 1; Assert.Equal(1, new DataflowRuntime().Run(GraphCompiler.Compile(d)).Values[n.Id].Number);
    }
    [Fact] public void SequencePassesTypedLocalToLaterFormula()
    {
        var vi = ProgrammingExamples.Sequence(); var graph = GraphCompiler.Compile(vi.Diagram); var runtime = new DataflowRuntime();
        var frame = runtime.Start(graph); var paths = new HashSet<string>();
        while (!frame.Completed) { paths.Add(frame.ActiveFrame.Path); frame.StepInto(); }
        Assert.Equal(11, frame.Values[vi.Diagram.Nodes.Single(n => n.Kind == "indicator").Id].Number); Assert.True(paths.Count >= 3);
    }
    [Fact] public void SequenceRejectsLocalReadBeforeWrite()
    {
        var vi = ProgrammingExamples.Sequence(); var n = vi.Diagram.Nodes.Single(n => n.Kind == "sequence"); n.Frames.Reverse();
        Assert.Contains(GraphCompiler.Validate(vi.Diagram), d => d.Code == "FRAMES");
    }
    [Fact] public void SequenceRejectsDuplicateExternalOutputSources()
    {
        var vi = ProgrammingExamples.Sequence(); var n = vi.Diagram.Nodes.Single(n => n.Kind == "sequence");
        var result = StructuredExamples.Connector("output", "result", ValueKind.Number, 0, 0); n.Frames[0].Diagram.Nodes.Add(result); Examples.Connect(n.Frames[0].Diagram, n.Frames[0].Diagram.Nodes[0], result);
        Assert.Contains(GraphCompiler.Validate(vi.Diagram), d => d.Code == "FRAMES");
    }
    [Fact] public void VersionThreeRoundTripsAllFrameGraphs()
    {
        var p = new LabProject { Instruments = [ProgrammingExamples.MultiCase(), ProgrammingExamples.Sequence()] }; var json = ProjectSerializer.Save(p);
        Assert.Equal(json, ProjectSerializer.Save(ProjectSerializer.Load(json))); Assert.Equal(3, ProjectSerializer.Load(json).FormatVersion);
    }
    [Fact] public void ComplexIndependentOutputValues()
    {
        var vi = ProgrammingExamples.ComplexMeasurement(); var f = new DataflowRuntime().Run(GraphCompiler.Compile(vi.Diagram)); var parts = vi.Diagram.Nodes.Single(n => n.Kind == "complex-parts");
        Assert.Equal(3, f.GetOutput(parts.Id, "real").Number); Assert.Equal(4, f.GetOutput(parts.Id, "imaginary").Number); Assert.Equal(5, f.Values[vi.Diagram.Nodes.Single(n => n.Kind == "indicator").Id].Number);
    }
    [Fact] public void ErrorOutputsAreTypedAndIndependent()
    {
        var c = Examples.NewNode("error-constant", 0, 0); c.Value = -42; c.Text = "instrument"; c.Parameters["status"] = 1;
        var u = Examples.NewNode("error-unbundle", 100, 0); var d = new Diagram { Nodes = [c, u] }; Examples.Connect(d, c, u);
        var f = new DataflowRuntime().Run(GraphCompiler.Compile(d)); Assert.True(f.GetOutput(u.Id, "status").Boolean); Assert.Equal(-42, f.GetOutput(u.Id, "code").Number); Assert.Equal("instrument", f.GetOutput(u.Id, "source").Text);
    }
    [Fact] public void FailedSequenceDoesNotCommitPriorFrameFeedback()
    {
        var vi = ProgrammingExamples.Sequence(); var n = vi.Diagram.Nodes.Single(n => n.Kind == "sequence");
        var feedback = Examples.NewNode("feedback", 200, 200); var one = Examples.NewNode("constant", 100, 200); one.Value = 7;
        n.Frames[0].Diagram.Nodes.AddRange([one, feedback]); Examples.Connect(n.Frames[0].Diagram, one, feedback);
        var formula = n.Frames[1].Diagram.Nodes.Single(v => v.Kind == "formula"); formula.Text = "result=1/0;";
        var runtime = new DataflowRuntime(); Assert.Throws<NodeExecutionException>(() => runtime.Run(GraphCompiler.Compile(vi.Diagram))); Assert.Equal(0, runtime.Frames);
        formula.Text = "result=x;"; var run = runtime.Start(GraphCompiler.Compile(vi.Diagram));
        while (run.ActiveFrame.Path == "root") run.StepInto();
        Assert.Equal(0, run.ActiveFrame.Values[feedback.Id].Number);
    }
}
