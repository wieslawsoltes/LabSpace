using LabSpace.Core;
using LabSpace.Dataflow;
using LabSpace.Documents;
using LabSpace.Editing;
using Xunit;

namespace LabSpace.Tests;

public sealed class FormulaControlFlowTests
{
    private static double Run(string code, double x = 3) => FormulaProgram.Compile(code, new()).Evaluate(_ => x)["result"];

    [Theory]
    [InlineData("if(x>0){result=7;}else{result=1/0;}", 7)]
    [InlineData("if(x<0)result=1;else if(x==3)result=2;else result=3;", 2)]
    [InlineData("float64 i=0; result=0; while(i<5){result+=i;i++;}", 10)]
    [InlineData("result=0; for(float64 i=0;i<5;i++){if(i==2)continue; result+=i;}", 8)]
    [InlineData("result=0; for(float64 i=0;i<50;++i){if(i==4)break; result+=i;}", 6)]
    [InlineData("result=0; do {result++;}while(result<3);", 3)]
    [InlineData("do{result=7;}while(0);", 7)]
    [InlineData("while(1){result=9;break;}", 9)]
    [InlineData("for(;;){result=8;break;}", 8)]
    [InlineData("float64 i=3; result=0; while(i>0){result+=i;--i;}", 6)]
    [InlineData("result=0; for(float64 i=0;i<3;i++){for(float64 j=0;j<2;j++){result++;}}", 6)]
    [InlineData("float64 i=0; result=0; do{i++;if(i<3)continue;result+=i;}while(i<4);", 7)]
    [InlineData("result=1; for(float64 i=0;i<0;i++){result=1/0;}", 1)]
    [InlineData("result=1; while(0){result=1/0;}", 1)]
    [InlineData("float64 result=2;result*=5;result/=2;result-=1;result%=3;", 1)]
    [InlineData("float64 a=1;{float64 a=8;result=a;}result+=a;", 9)]
    [InlineData("{float64 x=8;result=x;}result+=x;", 11)]
    [InlineData("result=0;{double i=0;for(;i<3;i++){double t=i+1;result+=t;}}", 6)]
    [InlineData("result=0;for(float64 i=0;i<5;i++){if(i<2)continue;if(i==4)break;result+=i;}", 5)]
    [InlineData("float64 i=0;for(;i<2;result++){result=i+4;i++;}result=2;", 2)]
    public void StructuredStatementsExecute(string source, double expected) => Assert.Equal(expected, Run(source), 10);

    [Theory]
    [InlineData("if(x>0)result=1;")]
    [InlineData("while(x>0){result=1;break;}")]
    [InlineData("for(float64 i=0;i<x;i++)result=1;")]
    [InlineData("do{if(x>0)break;result=1;}while(0);")]
    [InlineData("float64 i=0;do{if(x>0)continue;float64 y=2;}while(y>0);result=1;")]
    [InlineData("{float64 y=1;}result=y;")]
    [InlineData("for(float64 i=0;i<3;i++){}result=i;")]
    [InlineData("float64 x=2;result=x;")]
    [InlineData("x=2;result=x;")]
    [InlineData("float64 a=1;float64 a=2;result=a;")]
    [InlineData("float64 result;result+=1;")]
    [InlineData("float64 result;result=result+1;")]
    [InlineData("break;result=1;")]
    [InlineData("continue;result=1;")]
    [InlineData("result=1;for(;;){")]
    [InlineData("result=1;for(float64 i=0;i<3;i+=){result++;}")]
    [InlineData("float64 if=1;result=if;")]
    [InlineData("result=0;for(float64 i=0;i<3;i++){float64 t;if(i>0)t=1;result+=t;}")]
    public void InvalidScopeOrAssignmentIsDiagnosed(string source)
        => Assert.Throws<FormulaException>(() => FormulaProgram.Compile(source, new()));

    [Fact]
    public void AllReturningBranchesAssignMultipleOutputs()
    {
        var signature = new FormulaSignature { Outputs = ["a", "b"] };
        var result = FormulaProgram.Compile("if(x>0){a=x;b=2;}else{a=0;b=1;}", signature).Evaluate(_ => 3);
        Assert.Equal(3, result["a"]); Assert.Equal(2, result["b"]);
    }

    [Theory]
    [InlineData("while(1){}")] [InlineData("for(;;){}")] [InlineData("do{}while(1);")]
    public void InfiniteLoopsHaveDeterministicFuel(string source)
    {
        var program = FormulaProgram.Compile(source, new());
        Assert.Throws<ExecutionLimitException>(() => program.Evaluate(_ => 0, maximumInstructions: 257));
    }

    [Fact]
    public void BudgetIsFreshForEachInvocationAndStateIsNotShared()
    {
        var program = FormulaProgram.Compile("result=0;for(float64 i=0;i<x;i++)result+=i;", new());
        Assert.Equal(10, program.Evaluate(_ => 5)["result"]);
        Assert.Equal(3, program.Evaluate(_ => 3)["result"]);
        Assert.Equal(0, program.Evaluate(_ => 0)["result"]);
        Assert.Throws<ArgumentOutOfRangeException>(() => program.Evaluate(_ => 3, maximumInstructions: 0));
    }

    [Fact]
    public void CancellationDuringInputAcquisitionPreventsFormulaExecution()
    {
        using var stop = new CancellationTokenSource();
        var program = FormulaProgram.Compile("while(1){}", new());
        Assert.Throws<OperationCanceledException>(() => program.Evaluate(_ => { stop.Cancel(); return 1; }, cancellation: stop.Token));
    }

    [Fact]
    public void RootBudgetCancellationReachesFormulaBytecode()
    {
        using var stop = new CancellationTokenSource(); var budget = new ExecutionBudget(1000, stop.Token);
        var program = FormulaProgram.Compile("while(1){}", new());
        Assert.Throws<OperationCanceledException>(() => program.Evaluate(_ => { stop.Cancel(); return 1; }, budget));
    }

    [Fact]
    public void FormulaInstructionFailureDoesNotCommitFeedback()
    {
        var formula = Examples.NewNode("formula", 0, 0); formula.Formula = new() { Inputs = [] }; formula.Text = "while(1){}";
        var constant = Examples.NewNode("constant", 0, 0); constant.Value = 7;
        var feedback = Examples.NewNode("feedback", 0, 0); var graph = new Diagram { Nodes = [constant, feedback, formula] };
        Examples.Connect(graph, constant, feedback);
        var runtime = new DataflowRuntime();
        var exception = Assert.Throws<NodeExecutionException>(() => runtime.Run(GraphCompiler.Compile(graph)));
        Assert.IsType<ExecutionLimitException>(exception.InnerException); Assert.Equal(0, runtime.Frames);
        formula.Text = "result=1;";
        var frame = runtime.Start(GraphCompiler.Compile(graph)); Assert.Equal(0, frame.Values[feedback.Id].Number);
    }

    [Fact]
    public void LoopSourceUsesTheExistingTransactionalFormulaEditor()
    {
        var vi = AdvancedExamples.Sequence(); var session = new InstrumentSession(new() { Instruments = [vi] });
        var sequence = session.Diagram.Nodes.Single(n => n.Kind == "sequence"); session.Enter(sequence.Id, frameIndex: 1);
        var formula = session.Diagram.Nodes.Single(n => n.Kind == "formula"); var original = ProjectSerializer.Save(session.Project);
        session.ConfigureFormula(formula.Id, formula.Formula!, "result=0;for(float64 i=0;i<4;i++)result+=x;");
        session.Leave(); session.Run(); while (session.IsRunning) session.Tick();
        Assert.Equal(84, session.Values[sequence.Id].Number);
        session.Undo(); Assert.Equal(original, ProjectSerializer.Save(session.Project));
    }
}
