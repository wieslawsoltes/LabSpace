using LabSpace.Core;
using LabSpace.Dataflow;
using LabSpace.Documents;
using LabSpace.Editing;
using Xunit;

namespace LabSpace.Tests;

public sealed class ProgrammingEditingTests
{
    private static InstrumentSession Session(VirtualInstrument vi) => new(new() { Instruments = [vi] });

    [Fact]
    public void FramePreviewDoesNotModifyTheDocument()
    {
        var session = Session(ProgrammingExamples.MultiCase()); var node = session.Diagram.Nodes.Single(n => n.Kind == "case");
        var original = ProjectSerializer.Save(session.Project); session.CyclePreview(node.Id, 1);
        Assert.Equal(1, session.PreviewIndex(node)); Assert.Equal(original, ProjectSerializer.Save(session.Project)); Assert.False(session.CanUndo);
        session.Enter(node.Id); Assert.Equal(node.Frames[1].Id, session.Path.Single().FrameId);
        session.SelectFrame(2); Assert.Same(node.Frames[2].Diagram, session.Diagram); session.Leave(); Assert.Same(node, session.Find(node.Id));
    }

    [Fact]
    public void CaseReorderingIsAtomicAndDoesNotChangeTheSelectedValue()
    {
        var session = Session(ProgrammingExamples.MultiCase()); var node = session.Diagram.Nodes.Single(n => n.Kind == "case"); var before = ProjectSerializer.Save(session.Project);
        var draft = InstrumentSession.FramesDraft(node); (draft.Frames[0], draft.Frames[2]) = (draft.Frames[2], draft.Frames[0]);
        Assert.Equal(before, ProjectSerializer.Save(session.Project)); session.ConfigureFrames(node.Id, draft); session.Run();
        Assert.Equal(1, session.Values[session.Diagram.Nodes.Single(n => n.Kind == "indicator").Id].Number);
        session.Undo(); Assert.Equal(before, ProjectSerializer.Save(session.Project));
    }

    [Fact]
    public void OverlappingCasesAreRejectedWithoutAnyDocumentMutation()
    {
        var session = Session(ProgrammingExamples.MultiCase()); var node = session.Diagram.Nodes.Single(n => n.Kind == "case"); var before = ProjectSerializer.Save(session.Project);
        var draft = InstrumentSession.FramesDraft(node); draft.Frames[0].Selector = "5";
        Assert.Throws<GraphValidationException>(() => session.ConfigureFrames(node.Id, draft)); Assert.Equal(before, ProjectSerializer.Save(session.Project)); Assert.False(session.CanUndo);
    }

    [Fact]
    public void SequenceReorderingCannotIntroduceForwardReads()
    {
        var session = Session(ProgrammingExamples.Sequence()); var node = session.Diagram.Nodes.Single(n => n.Kind == "sequence"); var before = ProjectSerializer.Save(session.Project);
        var draft = InstrumentSession.FramesDraft(node); draft.Frames.Reverse();
        Assert.Throws<ArgumentException>(() => session.ConfigureFrames(node.Id, draft)); Assert.Equal(before, ProjectSerializer.Save(session.Project));
    }

    [Fact]
    public void LegacyBooleanCaseConvertsOnlyWhenApplied()
    {
        var node = Examples.NewNode("case", 0, 0); var session = Session(new() { Diagram = new() { Nodes = [node] } }); var before = ProjectSerializer.Save(session.Project);
        var draft = InstrumentSession.FramesDraft(node); Assert.Equal(2, draft.Frames.Count); Assert.Equal(before, ProjectSerializer.Save(session.Project));
        session.ConfigureFrames(node.Id, draft); Assert.Null(node.Body); Assert.Equal(2, node.Frames.Count); session.Undo(); Assert.Equal(before, ProjectSerializer.Save(session.Project));
    }

    [Fact]
    public void EditingNestedFormulaUsesOneUndoTransaction()
    {
        var session = Session(ProgrammingExamples.Sequence()); var owner = session.Diagram.Nodes.Single(n => n.Kind == "sequence"); session.EnterFrame(owner.Id, 1);
        var formula = session.Diagram.Nodes.Single(n => n.Kind == "formula"); var before = ProjectSerializer.Save(session.Project);
        session.ConfigureFormula(formula.Id, "result = x * 10;", formula.Contract!); session.Leave(); session.Run();
        Assert.Equal(30, session.Values[session.Diagram.Nodes.Single(n => n.Kind == "indicator").Id].Number);
        session.Undo(); Assert.Equal(before, ProjectSerializer.Save(session.Project));
    }

    [Fact]
    public void InvalidFormulaCannotReplaceExecutableSource()
    {
        var formula = Examples.NewNode("formula", 0, 0); var session = Session(new() { Diagram = new() { Nodes = [formula] } });
        var before = ProjectSerializer.Save(session.Project); Assert.Throws<ArgumentException>(() => session.ConfigureFormula(formula.Id, "result = missing;", formula.Contract!));
        Assert.Equal(before, ProjectSerializer.Save(session.Project)); Assert.False(session.CanUndo);
    }

    [Fact]
    public void CreateIndicatorPreservesNamedSourceFanOutAndUndo()
    {
        var session = Session(ProgrammingExamples.ComplexMeasurement()); var parts = session.Diagram.Nodes.Single(n => n.Kind == "complex-parts");
        var before = ProjectSerializer.Save(session.Project); var indicator = session.CreateTerminal(parts.Id, "imaginary", true); session.Run();
        Assert.Equal(4, session.Values[indicator.Id].Number); Assert.Contains(session.Diagram.Wires, w => w.From == parts.Id && w.Output == "imaginary" && w.To == indicator.Id);
        Assert.Contains(session.Instrument.Panel, p => p.NodeId == indicator.Id && p.Widget == "Numeric"); session.Undo(); Assert.Equal(before, ProjectSerializer.Save(session.Project));
    }

    [Fact]
    public void CreateConstantReplacesOnlyOneInputAndRestoresItOnUndo()
    {
        var session = Session(ProgrammingExamples.ComplexMeasurement()); var build = session.Diagram.Nodes.Single(n => n.Kind == "complex-build"); var before = ProjectSerializer.Save(session.Project);
        var constant = session.CreateTerminal(build.Id, "real", false); Assert.Single(session.Diagram.Wires.Where(w => w.To == build.Id && w.Input == "real"));
        Assert.Contains(session.Diagram.Wires, w => w.From == constant.Id && w.To == build.Id); session.Undo(); Assert.Equal(before, ProjectSerializer.Save(session.Project));
    }

    [Fact]
    public void ComplexAndErrorIndicatorsGetTypedFrontPanelWidgets()
    {
        var vi = ProgrammingExamples.ComplexMeasurement(); var session = Session(vi); var build = session.Diagram.Nodes.Single(n => n.Kind == "complex-build");
        var indicator = session.CreateTerminal(build.Id, "result", true); session.Run(); Assert.Equal(ValueKind.Complex, session.Values[indicator.Id].Kind);
        Assert.Contains(vi.Panel, p => p.NodeId == indicator.Id && p.Widget == "Complex");
        var error = session.Add("error-constant", 0, 0); var display = session.CreateTerminal(error.Id, "result", true);
        Assert.Contains(vi.Panel, p => p.NodeId == display.Id && p.Widget == "Error");
    }

    [Fact]
    public void StepOutReturnsToTheParentWithoutNavigatingTheEditor()
    {
        var session = Session(ProgrammingExamples.Sequence());
        for (var i = 0; i < 20 && session.DebugFrame?.Path.Contains('/') != true; i++) session.StepInto();
        Assert.True(session.IsPaused); Assert.Contains('/', session.DebugFrame!.Path); var depth = session.Path.Count;
        session.StepOut(); for (var i = 0; i < 100 && session.IsRunning; i++) session.Tick();
        Assert.False(session.IsRunning); Assert.True(session.IsPaused); Assert.Equal(depth, session.Path.Count);
    }
}
