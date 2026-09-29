using LabSpace.Core;
using LabSpace.Dataflow;
using LabSpace.Documents;
using LabSpace.Editing;
using Xunit;

namespace LabSpace.Tests;

public sealed class AdvancedEditingTests
{
    private static InstrumentSession Session(VirtualInstrument vi) => new(new() { Instruments = [vi] });
    private static void Complete(InstrumentSession session)
    {
        session.Run(); for (var i = 0; session.IsRunning && !session.IsPaused && i < 1000; i++) session.Tick();
        Assert.False(session.IsRunning); Assert.Equal("Execution complete", session.Status);
    }
    [Fact] public void FrameDraftIsDeepAndApplyIsOneUndoTransaction()
    {
        var session = Session(AdvancedExamples.CaseDispatch()); var node = session.Diagram.Nodes.Single(n => n.Kind == "case-multi");
        var original = ProjectSerializer.Save(session.Project); var draft = InstrumentSession.CloneFrames(node);
        draft[0].Diagram.Nodes.First(n => n.Kind == "constant").Value = 123; draft[0].Label = "\"RUN\"";
        Assert.Equal(original, ProjectSerializer.Save(session.Project));
        session.ConfigureFrames(node.Id, ValueKind.String, draft, true); Complete(session);
        Assert.Equal(123, session.OutputValue(node.Id)!.Number);
        session.Undo(); Assert.Equal(original, ProjectSerializer.Save(session.Project));
        session.Redo(); Complete(session); Assert.Equal(123, session.OutputValue(node.Id)!.Number);
    }
    [Fact] public void InvalidCaseDraftDoesNotAffectHistoryOrDocument()
    {
        var session = Session(AdvancedExamples.CaseDispatch()); var node = session.Diagram.Nodes.Single(n => n.Kind == "case-multi");
        var before = ProjectSerializer.Save(session.Project); var draft = InstrumentSession.CloneFrames(node); draft[1].Label = draft[0].Label;
        Assert.Throws<ArgumentException>(() => session.ConfigureFrames(node.Id,ValueKind.String,draft,false));
        Assert.Equal(before,ProjectSerializer.Save(session.Project)); Assert.False(session.CanUndo);
    }
    [Fact] public void ReorderingSequenceDetectsDependencyAndUndoRestoresIt()
    {
        var session = Session(AdvancedExamples.Sequence()); var node = session.Diagram.Nodes.Single(n => n.Kind == "sequence");
        var draft = InstrumentSession.CloneFrames(node); draft.Reverse(); session.ConfigureFrames(node.Id,ValueKind.Number,draft,false);
        Assert.Contains(session.Diagnostics,d => d.Code == "LOCAL"); session.Undo(); Assert.Empty(session.Diagnostics); Complete(session);
        Assert.Equal(84,session.OutputValue(node.Id)!.Number);
    }
    [Fact] public void FormulaCompilationPrecedesCommitAndNamedWireDeletionIsUndoable()
    {
        var session = Session(Examples.Blank("Formula.vi")); var constant = session.Add("constant",0,0); session.SetValue(constant.Id,4);
        var formula = session.Add("formula",100,0); session.Connect(constant.Id,formula.Id,"x"); var before = ProjectSerializer.Save(session.Project);
        Assert.Throws<FormulaException>(() => session.ConfigureFormula(formula.Id,new(),"result = unknown;")); Assert.Equal(before,ProjectSerializer.Save(session.Project));
        session.ConfigureFormula(formula.Id,new() { Inputs = [], Outputs = ["a","b"] },"a = 3; b = a * 4;");
        Assert.Empty(session.Diagram.Wires); Complete(session); Assert.Equal(3,session.OutputValue(formula.Id,"a")!.Number); Assert.Equal(12,session.OutputValue(formula.Id,"b")!.Number);
        session.Undo(); Assert.Equal(before,ProjectSerializer.Save(session.Project));
    }
    [Theory]
    [InlineData("add","x",ValueKind.Number)] [InlineData("and","x",ValueKind.Boolean)] [InlineData("concat","x",ValueKind.String)]
    [InlineData("reverse-array","x",ValueKind.Array)] [InlineData("error-clear","x",ValueKind.Error)] [InlineData("complex-conjugate","x",ValueKind.Complex)]
    public void CreateConstantConnectsCorrectTypeAndUndoRemovesOnlyNewEdit(string kind,string input,ValueKind type)
    {
        var session = Session(Examples.Blank("Terminal.vi")); var owner = session.Add(kind,200,100); var before = ProjectSerializer.Save(session.Project);
        var node = session.CreateTerminal(owner.Id,input,false,TerminalCreation.Constant,new(20,100));
        Assert.Equal(type,NodeCatalog.Resolve(node).Output); Assert.Contains(session.Diagram.Wires,w => w.From == node.Id && w.To == owner.Id && w.Input == input);
        session.Undo(); Assert.Equal(before,ProjectSerializer.Save(session.Project));
    }
    [Fact] public void CreateIndicatorUsesActualNamedOutputAndCreatesPanelItem()
    {
        var session = Session(Examples.Blank("Named.vi")); var complex = session.Add("complex",0,0); var parts = session.Add("complex-parts",100,0); session.Connect(complex.Id,parts.Id,"x");
        var indicator = session.CreateTerminal(parts.Id,"imaginary",true,TerminalCreation.Indicator,new(300,0));
        Assert.Contains(session.Instrument.Panel,p => p.NodeId == indicator.Id); Assert.Contains(session.Diagram.Wires,w => w.From == parts.Id && w.Output == "imaginary");
        Complete(session); Assert.Equal(0,session.Values[indicator.Id].Number);
    }
    [Fact] public void DisconnectResolvesPrimaryOutputAlias()
    {
        var session = Session(Examples.Blank("Alias.vi")); var complex = session.Add("complex",0,0); var parts = session.Add("complex-parts",100,0); session.Connect(complex.Id,parts.Id,"x");
        var indicator = session.Add("indicator",200,0); session.Connect(parts.Id,indicator.Id,"x"); session.DisconnectTerminal(parts.Id,"real",true);
        Assert.DoesNotContain(session.Diagram.Wires,w => w.From == parts.Id); session.Undo(); Assert.Contains(session.Diagram.Wires,w => w.From == parts.Id);
    }
    [Fact] public void RootExecutionRetainsSequenceContextWhenEditorIsInsideAFrame()
    {
        var session = Session(AdvancedExamples.Sequence()); var sequence = session.Diagram.Nodes.Single(n => n.Kind == "sequence");
        session.Enter(sequence.Id,frameIndex:1); Assert.Empty(session.Diagnostics); var formula = session.Diagram.Nodes.Single(n => n.Kind == "formula");
        session.ConfigureFormula(formula.Id,new(),"result = x * 5;"); Complete(session); Assert.Equal(105,session.OutputValues[new(sequence.Id,"result")].Number);
    }
    [Fact] public void StepOutFinishesChildAndPausesInCaller()
    {
        var session = Session(AdvancedExamples.Sequence()); var sequence = session.Diagram.Nodes.Single(n => n.Kind == "sequence");
        for (var i=0;i<10 && (session.DebugFrame?.Path.Contains('/') != true);i++) session.StepInto();
        Assert.True(session.IsPaused); Assert.Contains('/',session.DebugFrame!.Path);
        session.StepOut(); for(var i=0;i<100 && session.IsRunning && !session.IsPaused;i++) session.Tick();
        Assert.True(session.IsPaused); Assert.Contains("Step out",session.Status); Complete(session); Assert.Equal(84,session.OutputValue(sequence.Id)!.Number);
    }
    [Fact] public void ResizeGestureIsSingleHistoryEntryAndCancellationRestoresGeometry()
    {
        var session = Session(AdvancedExamples.Sequence()); var id = session.Diagram.Nodes.First(n=>n.Kind=="sequence").Id; var width = session.Find(id)!.Width;
        session.BeginGesture(); session.Find(id)!.Width = width+80; session.Find(id)!.Height += 40; session.EndGesture();
        session.Undo(); Assert.Equal(width,session.Find(id)!.Width); Assert.False(session.CanUndo);
        session.BeginGesture(); session.Find(id)!.Width = width+100; session.CancelGesture(); Assert.Equal(width,session.Find(id)!.Width); Assert.False(session.CanUndo);
    }
    [Fact] public void FormulaInputsCannotBeAssignedAndLogarithmBasesAreExplicit()
    {
        Assert.Throws<FormulaException>(()=>FormulaProgram.Compile("x = 3; result = x;",new()));
        Assert.Equal(6,FormulaProgram.Compile("ln(e) + log(100) + log2(8)",new()).Evaluate(_=>0)["result"],10);
    }
}
