using System.Collections.Immutable;
using LabSpace.Core;
using LabSpace.Dataflow;
using LabSpace.Documents;

namespace LabSpace.Editing;

public sealed partial class InstrumentSession
{
    private readonly Dictionary<string, int> _previewFrames = [];
    private ExecutionFrame? _stepOutTarget;
    public static Diagram? ResolveChild(Node node, NavigationLevel level) => level.FrameId is { } id ? node.Frames.FirstOrDefault(f => f.Id == id)?.Diagram : level.Alternative ? node.Alternative : node.Body;
    public int PreviewIndex(Node node) => node.Frames.Count == 0 ? 0 : Math.Clamp(_previewFrames.GetValueOrDefault(node.Id), 0, node.Frames.Count - 1);
    public void CyclePreview(string id, int direction)
    {
        if (Find(id) is not { Frames.Count: > 0 } node) return;
        _previewFrames[id] = (PreviewIndex(node) + direction + node.Frames.Count) % node.Frames.Count; Notify(SessionChange.View);
    }
    public Node? CurrentOwner
    {
        get
        {
            Node? owner = null; var graph = Instrument.Diagram;
            foreach (var level in _path) { owner = graph.Nodes.FirstOrDefault(n => n.Id == level.NodeId); if (owner is null) return null; graph = ResolveChild(owner, level) ?? graph; }
            return owner;
        }
    }
    public void EnterFrame(string id, int index)
    {
        var node = Find(id); if (node is null || index < 0 || index >= node.Frames.Count) return;
        Abort(); _path.Add(new(id, false, node.Frames[index].Id)); Selection.Clear(); SelectedWire = null; ResetExecution(); Validate(); Notify(SessionChange.All);
    }
    public void SelectFrame(int index)
    {
        if (CurrentOwner is not { } owner || index < 0 || index >= owner.Frames.Count) return;
        Abort(); _path[^1] = _path[^1] with { FrameId = owner.Frames[index].Id, Alternative = false }; Selection.Clear(); SelectedWire = null; ResetExecution(); Validate(); Notify(SessionChange.All);
    }
    private static Node CloneNode(Node node) => ProjectSerializer.Clone(new() { Instruments = [new() { Diagram = new() { Nodes = [node] } }] }).Instruments[0].Diagram.Nodes[0];
    public static Node FramesDraft(Node node)
    {
        if (node.Kind is not ("case" or "sequence")) throw new ArgumentException("Select a Case or Sequence structure.");
        var draft = CloneNode(node);
        if (draft.Frames.Count == 0)
        {
            draft.Contract = ContractDraft(draft);
            draft.Frames = [new() { Selector = "True", Diagram = draft.Body ?? new() }, new() { Selector = "False", Diagram = draft.Alternative ?? new() }];
            foreach (var frame in draft.Frames)
            {
                foreach (var n in frame.Diagram.Nodes.Where(n => n.Kind == "input" && n.Text is "state" or "x")) n.Text = "x";
                foreach (var n in frame.Diagram.Nodes.Where(n => n.Kind == "output" && string.IsNullOrWhiteSpace(n.Text))) n.Text = "result";
            }
            draft.Body = draft.Alternative = null;
        }
        return draft;
    }
    public void ConfigureFrames(string id, Node draft)
    {
        var node = Find(id) ?? throw new ArgumentException("Structure not found.");
        if (node.Kind != draft.Kind || node.Kind is not ("case" or "sequence")) throw new ArgumentException("Mismatched frame structure.");
        var clone = CloneNode(draft);
        var errors = GraphCompiler.ValidateInterface(clone);
        if (errors.Count > 0) throw new GraphValidationException(errors);
        SynchronizeFrames(clone);
        // A draft may be deliberately unwired, but structurally illegal metadata must never be committed.
        var definition = NodeCatalog.Resolve(clone); var graph = new Diagram { Nodes = [clone] };
        foreach (var port in definition.Inputs.Where(p => p.Required))
        {
            var source = StructuredExamples.Connector("input", port.Name, port.Kind, 0, 0); graph.Nodes.Add(source); Examples.Connect(graph, source, clone, port.Name);
        }
        var diagnostics = GraphCompiler.Validate(graph).Where(d => d.Code is "FRAMES" or "CONTRACT" or "CONNECTOR" or "DEPTH" or "LIMIT").ToArray();
        if (diagnostics.Length > 0) throw new GraphValidationException(diagnostics);
        Edit(() =>
        {
            if (node.Contract is null) foreach (var wire in Diagram.Wires.Where(w => w.To == id && w.Input == "initial")) wire.Input = "x";
            node.Contract = clone.Contract; node.Frames = clone.Frames; node.Body = node.Alternative = null;
            RemoveMissingExternalTerminals(node);
        });
    }
    public void ConfigureFormula(string id, string source, StructureContract contract)
    {
        var node = Find(id) ?? throw new ArgumentException("Formula not found.");
        if (node.Kind != "formula") throw new ArgumentException("Select a Formula Node.");
        var draft = new Node { Kind = "formula", Text = source, Contract = contract };
        var errors = GraphCompiler.ValidateInterface(draft); if (errors.Count > 0) throw new GraphValidationException(errors);
        FormulaProgram.Compile(source, contract);
        Edit(() => { node.Text = source; node.Contract = contract; RemoveMissingExternalTerminals(node); });
    }
    private void RemoveMissingExternalTerminals(Node node)
    {
        var def = NodeCatalog.Resolve(node);
        Diagram.Wires.RemoveAll(w => (w.To == node.Id && !def.Inputs.Any(p => p.Name == w.Input)) || (w.From == node.Id && def.FindOutput(w.Output) is null));
    }
    public static StructureFrame DuplicateFrame(Node draft, int index)
    {
        var copy = CloneNode(draft).Frames[index]; copy.Id = Guid.NewGuid().ToString("N"); copy.IsDefault = false;
        copy.Selector = draft.Kind == "case" ? draft.Frames.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) : copy.Selector + " copy";
        if (draft.Kind == "sequence")
        {
            var remove = copy.Diagram.Nodes.Where(n => n.Kind == "output").Select(n => n.Id).ToHashSet(); copy.Diagram.Nodes.RemoveAll(n => remove.Contains(n.Id)); copy.Diagram.Wires.RemoveAll(w => remove.Contains(w.From) || remove.Contains(w.To));
        }
        draft.Frames.Insert(index + 1, copy); return copy;
    }
    private static void SynchronizeFrames(Node owner)
    {
        if (owner.Kind == "case") { foreach (var frame in owner.Frames) SynchronizeConnectors(owner, frame.Diagram); return; }
        var c = owner.Contract!;
        var outputOwners = c.Outputs.ToDictionary(t => t.Name, t => owner.Frames.FirstOrDefault(f => f.Diagram.Nodes.Any(n => n.Kind == "output" && GraphCompiler.ConnectorName(n) == t.Name))?.Id ?? owner.Frames[^1].Id);
        for (var i = 0; i < owner.Frames.Count; i++)
        {
            var frame = owner.Frames[i];
            var inputs = c.Inputs.ToList(); var outputs = c.Outputs.Where(t => outputOwners[t.Name] == frame.Id).ToList();
            foreach (var local in c.Locals)
            {
                var source = owner.Frames.FindIndex(f => f.Id == local.SourceFrameId);
                if (source == i) outputs.Add(new() { Name = local.Name, Type = local.Type });
                if (source < i) inputs.Add(new() { Name = local.Name, Type = local.Type });
            }
            var facade = new Node { Kind = "subvi", Contract = new() { Inputs = inputs.ToImmutableArray(), Outputs = outputs.ToImmutableArray() } };
            // Do not silently repair duplicate output sources or reads of a future local.
            var invalidRead = frame.Diagram.Nodes.FirstOrDefault(n => n.Kind == "input" && c.Locals.Any(l => l.Name == n.Text && owner.Frames.FindIndex(f => f.Id == l.SourceFrameId) >= i));
            if (invalidRead is not null) throw new ArgumentException("Sequence local is read before its source frame: " + invalidRead.Text);
            var duplicate = frame.Diagram.Nodes.FirstOrDefault(n => n.Kind == "output" && outputOwners.TryGetValue(GraphCompiler.ConnectorName(n), out var owningId) && owningId != frame.Id);
            if (duplicate is not null) throw new ArgumentException("Sequence output has multiple source frames: " + duplicate.Text);
            SynchronizeConnectors(facade, frame.Diagram);
        }
    }
    public void StepOut()
    {
        if (_frame is null) { Step(); return; }
        _stepOutTarget = _frame.ActiveFrame; _continuous = false; IsPaused = false; IsRunning = true; Pump();
    }
    /// <summary>Creates and wires one terminal in a single transaction; preserves the existing source for fan-out.</summary>
    public Node CreateTerminal(string targetId, string terminal, bool output, bool control = false)
    {
        var target = Find(targetId) ?? throw new ArgumentException("Node not found."); var definition = NodeCatalog.Resolve(target);
        var type = output ? definition.FindOutput(terminal)?.Kind : definition.Inputs.FirstOrDefault(p => p.Name == terminal)?.Kind;
        if (type is null) throw new ArgumentException("Terminal not found.");
        if (control && type is not (ValueKind.Number or ValueKind.Boolean or ValueKind.String)) throw new ArgumentException("This type does not yet have an editable front-panel control.");
        var kind = (output, type.Value, control) switch
        {
            (true, ValueKind.Number, _) => "indicator", (true, ValueKind.Boolean, _) => "bool-indicator", (true, ValueKind.String, _) => "string-indicator", (true, ValueKind.Array, _) => "array-indicator", (true, ValueKind.Waveform, _) => "graph", (true, ValueKind.Error, _) => "error-indicator", (true, ValueKind.Complex, _) => "complex-indicator",
            (false, ValueKind.Number, true) => "control", (false, ValueKind.Boolean, true) => "bool-control", (false, ValueKind.String, true) => "string-control",
            (false, ValueKind.Number, _) => "constant", (false, ValueKind.Boolean, _) => "bool", (false, ValueKind.String, _) => "string", (false, ValueKind.Array, _) => "array", (false, ValueKind.Waveform, _) => "waveform-constant", (false, ValueKind.Error, _) => "error-constant", _ => "complex-constant"
        };
        var node = Examples.NewNode(kind, target.X + (output ? 410 : -140), target.Y + 35); node.Label = terminal;
        Edit(() =>
        {
            Diagram.Nodes.Add(node);
            if (!output) Diagram.Wires.RemoveAll(w => w.To == targetId && w.Input == terminal);
            Diagram.Wires.Add(output ? new() { From = targetId, Output = terminal, To = node.Id, Input = "x" } : new() { From = node.Id, To = targetId, Input = terminal });
            if (_path.Count == 0 && (output || control))
            {
                var widget = type.Value switch { ValueKind.Boolean => output ? "LED" : "Switch", ValueKind.String => "String", ValueKind.Array => "Array", ValueKind.Waveform => "Graph", ValueKind.Error => "Error", ValueKind.Complex => "Complex", _ => "Numeric" };
                Instrument.Panel.Add(new() { NodeId = node.Id, Widget = widget, Bounds = new(40 + Instrument.Panel.Count % 4 * 240, 70 + Instrument.Panel.Count / 4 * 180, 210, 130) });
            }
        }); Select(node.Id); return node;
    }
}
