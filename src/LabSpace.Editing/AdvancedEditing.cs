using LabSpace.Core;
using LabSpace.Dataflow;
using LabSpace.Documents;

namespace LabSpace.Editing;

public enum TerminalCreation { Constant, Control, Indicator }

public sealed partial class InstrumentSession
{
    private ExecutionFrame? _stepOutTarget;
    public static Diagram? ResolveChild(Node? node, NavigationLevel level) => node is null ? null : level.FrameIndex >= 0
        ? node.Frames.ElementAtOrDefault(level.FrameIndex)?.Diagram : level.Alternative ? node.Alternative : node.Body;
    public Node? ParentStructure
    {
        get
        {
            var diagram = Instrument.Diagram; Node? owner = null;
            foreach (var level in _path)
            {
                owner = diagram.Nodes.FirstOrDefault(n => n.Id == level.NodeId);
                if (ResolveChild(owner, level) is not { } child) return null;
                diagram = child;
            }
            return owner;
        }
    }
    public void SelectFrame(string nodeId, int index)
    {
        var node = Find(nodeId) ?? throw new ArgumentException("Structure not found.");
        if (!StructureFrames.HasFrames(node) || index < 0 || index >= node.Frames.Count) throw new ArgumentOutOfRangeException(nameof(index));
        node.VisibleFrame = index; Notify(SessionChange.View);
    }
    public void NavigateFrame(int index)
    {
        var parent = ParentStructure;
        if (parent is null || !StructureFrames.HasFrames(parent) || index < 0 || index >= parent.Frames.Count) return;
        var id = parent.Id; Leave(); SelectFrame(id, index); Enter(id, frameIndex: index);
    }
    public static List<StructureFrame> CloneFrames(Node owner)
    {
        var project = new LabProject { Instruments = [new() { Diagram = new() { Nodes = [owner] } }] };
        return ProjectSerializer.Clone(project).Instruments[0].Diagram.Nodes[0].Frames;
    }
    public void ConfigureFrames(string nodeId, ValueKind type, IReadOnlyList<StructureFrame> frames, bool ignoreCase)
    {
        var node = Find(nodeId) ?? throw new ArgumentException("Structure not found.");
        if (!StructureFrames.HasFrames(node) || frames.Count is < 1 or > 64) throw new ArgumentException("A multi-frame structure requires 1–64 frames.");
        var candidate = new Node { Kind = node.Kind, DataType = type, Frames = frames.ToList(), Contract = node.Contract };
        candidate.Parameters["caseInsensitive"] = ignoreCase ? 1 : 0;
        var copy = CloneFrames(candidate);
        if (node.Kind == "case-multi") CaseDispatchTable.Compile(candidate);
        Edit(() =>
        {
            node.Frames = copy; node.DataType = type; node.Parameters["caseInsensitive"] = ignoreCase ? 1 : 0;
            node.VisibleFrame = Math.Clamp(node.VisibleFrame, 0, copy.Count - 1);
            foreach (var frame in copy) SynchronizeConnectors(node, frame.Diagram, node.Kind != "sequence");
        });
    }
    public void ConfigureFormula(string nodeId, FormulaSignature signature, string source)
    {
        FormulaProgram.Compile(source, signature);
        var node = Find(nodeId) ?? throw new ArgumentException("Formula node not found.");
        if (node.Kind != "formula") throw new ArgumentException("Select a Formula Node.");
        Edit(() =>
        {
            node.Formula = signature; node.Text = source;
            Diagram.Wires.RemoveAll(w => (w.To == nodeId && !signature.Inputs.Contains(w.Input))
                || (w.From == nodeId && w.Output != "result" && !signature.Outputs.Contains(w.Output)));
        });
    }
    public void SetError(string nodeId, bool status, int code, string source)
    {
        _ = Value.ErrorValue(status, code, source);
        var node = Find(nodeId) ?? throw new ArgumentException("Node not found.");
        if (node.Kind is not ("error-control" or "error-constant")) throw new ArgumentException("Select an editable error cluster.");
        Edit(() => { node.Value = code; node.Text = source; node.Parameters["status"] = status ? 1 : 0; }, false);
    }
    public static string? TerminalKind(ValueKind kind, TerminalCreation creation) => (kind, creation) switch
    {
        (ValueKind.Number, TerminalCreation.Constant) => "constant", (ValueKind.Number, TerminalCreation.Control) => "control", (ValueKind.Number, TerminalCreation.Indicator) => "indicator",
        (ValueKind.Boolean, TerminalCreation.Constant) => "bool", (ValueKind.Boolean, TerminalCreation.Control) => "bool-control", (ValueKind.Boolean, TerminalCreation.Indicator) => "bool-indicator",
        (ValueKind.String, TerminalCreation.Constant) => "string", (ValueKind.String, TerminalCreation.Control) => "string-control", (ValueKind.String, TerminalCreation.Indicator) => "string-indicator",
        (ValueKind.Array, TerminalCreation.Constant) => "array", (ValueKind.Array, TerminalCreation.Indicator) => "array-indicator",
        (ValueKind.Waveform, TerminalCreation.Indicator) => "graph",
        (ValueKind.Error, TerminalCreation.Constant) => "error-constant", (ValueKind.Error, TerminalCreation.Control) => "error-control", (ValueKind.Error, TerminalCreation.Indicator) => "error-indicator",
        (ValueKind.Complex, TerminalCreation.Constant) => "complex", (ValueKind.Complex, TerminalCreation.Control) => "complex-control", (ValueKind.Complex, TerminalCreation.Indicator) => "complex-indicator",
        _ => null
    };
    /// <summary>Creates a correctly typed terminal, panel item and wire in one transaction.</summary>
    public Node CreateTerminal(string ownerId, string portName, bool output, TerminalCreation creation, PointD position)
    {
        var owner = Find(ownerId) ?? throw new ArgumentException("Owner node not found.");
        var definition = NodeCatalog.Resolve(owner);
        var type = output ? definition.FindOutput(portName)?.Kind : definition.Inputs.FirstOrDefault(p => p.Name == portName)?.Kind;
        if (type is null || output != (creation == TerminalCreation.Indicator)) throw new ArgumentException("Choose a compatible terminal creation command.");
        var kind = TerminalKind(type.Value, creation) ?? throw new ArgumentException("This terminal type has no matching creation command.");
        var node = Examples.NewNode(kind, position.X, position.Y); node.Label = portName;
        if (!output && definition.Inputs.First(p => p.Name == portName) is { } input) node.Value = owner.Parameter(portName, input.Default);
        Edit(() =>
        {
            Diagram.Nodes.Add(node);
            if (output) Diagram.Wires.Add(new() { From = ownerId, Output = portName, To = node.Id, Input = "x" });
            else { Diagram.Wires.RemoveAll(w => w.To == ownerId && w.Input == portName); Diagram.Wires.Add(new() { From = node.Id, To = ownerId, Input = portName }); }
            if (_path.Count == 0 && creation != TerminalCreation.Constant)
            {
                var widget = type switch { ValueKind.Boolean => output ? "LED" : "Switch", ValueKind.String => "String", ValueKind.Array => "Array", ValueKind.Waveform => "Graph", ValueKind.Error => "Error", ValueKind.Complex => "Complex", _ => "Numeric" };
                Instrument.Panel.Add(new() { NodeId = node.Id, Widget = widget, Bounds = new(40 + Instrument.Panel.Count % 3 * 300, 60 + Instrument.Panel.Count / 3 * 180, widget == "Graph" ? 400 : 260, widget == "Graph" ? 240 : widget == "Error" ? 160 : 110), Minimum = -1000, Maximum = 1000 });
            }
        });
        Select(node.Id); return node;
    }
    public void DisconnectTerminal(string nodeId, string port, bool output)
    {
        var definition = NodeCatalog.Resolve(Find(nodeId) ?? throw new ArgumentException("Node not found."));
        var name = output ? definition.FindOutput(port)?.Name : port;
        Edit(() => Diagram.Wires.RemoveAll(w => output
            ? w.From == nodeId && definition.FindOutput(w.Output)?.Name == name
            : w.To == nodeId && w.Input == port));
    }
    public void StepOut()
    {
        if (_frame is null || !IsPaused) { Message("Pause inside a structure before stepping out."); return; }
        _stepOutTarget = _frame.ActiveFrame; _continuous = false; IsPaused = false; IsRunning = true; Pump();
    }
}
