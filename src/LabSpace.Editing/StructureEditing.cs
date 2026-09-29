using LabSpace.Core;
using LabSpace.Dataflow;
using LabSpace.Documents;

namespace LabSpace.Editing;

public sealed partial class InstrumentSession
{
    /// <summary>Returns an immutable draft without changing the project. Legacy numeric structures are migrated only on Apply.</summary>
    public static StructureContract ContractDraft(Node node) => node.Contract ?? (node.Kind is "for" or "while"
        ? new() { Registers = [new() { Name = "state" }], PrimaryOutput = "state" }
        : new() { Inputs = [new() { Name = "x", Required = false }], Outputs = [new() { Name = "result" }] });

    public void ConfigureStructure(string id, StructureContract contract)
    {
        var node = Find(id) ?? throw new ArgumentException("Structure not found.");
        if (!NodeCatalog.Resolve(node).IsStructure) throw new ArgumentException("Select a structure.");
        if (node.Frames.Count > 0) { var draft = FramesDraft(node); draft.Contract = contract; ConfigureFrames(id, draft); return; }
        var probe = new Node { Kind = node.Kind, Contract = contract, Body = new(), Alternative = new() };
        var errors = GraphCompiler.Validate(new() { Nodes = [probe] }).Where(d => d.Code == "CONTRACT").ToArray();
        if (errors.Length > 0) throw new GraphValidationException(errors);
        Edit(() =>
        {
            var legacy = node.Contract is null;
            if (legacy)
            {
                var from = node.Kind == "subvi" ? "x" : "initial";
                var to = node.Kind is "for" or "while" ? "initial:state" : "x";
                foreach (var wire in Diagram.Wires.Where(w => w.To == id && w.Input == from)) wire.Input = to;
                if (node.Parameters.Remove(from, out var initial)) node.Parameters[to] = initial;
                foreach (var body in new[] { node.Body, node.Alternative }.OfType<Diagram>())
                {
                    foreach (var n in body.Nodes.Where(n => n.Kind == "input" && n.Text is "state" or "x")) n.Text = node.Kind is "for" or "while" ? "state" : "x";
                    foreach (var n in body.Nodes.Where(n => n.Kind == "output" && string.IsNullOrWhiteSpace(n.Text))) n.Text = node.Kind is "for" or "while" ? "state" : "result";
                }
            }
            node.Contract = contract; node.Body ??= new(); SynchronizeConnectors(node, node.Body);
            if (node.Kind == "case") { node.Alternative ??= new(); SynchronizeConnectors(node, node.Alternative); }
            var definition = NodeCatalog.Resolve(node);
            // Removing a declared terminal is explicit and removes only its attached wires. Undo restores the entire edit.
            Diagram.Wires.RemoveAll(w => (w.To == id && !definition.Inputs.Any(p => p.Name == w.Input)) || (w.From == id && definition.FindOutput(w.Output) is null));
        });
    }
    private static void SynchronizeConnectors(Node owner, Diagram body)
    {
        var contract = owner.Contract!;
        var inputs = contract.Inputs.ToDictionary(t => t.Name, t => t.Type, StringComparer.Ordinal);
        var outputs = contract.Outputs.ToDictionary(t => t.Name, t => t.Type, StringComparer.Ordinal);
        foreach (var register in contract.Registers)
        {
            outputs[register.Name] = register.Type;
            for (var i = 0; i < register.HistoryDepth; i++) inputs[register.Name + (i == 0 ? "" : ":" + i)] = register.Type;
        }
        if (owner.Kind is "for" or "while") inputs["i"] = ValueKind.Number;
        if (owner.Kind == "for") inputs["N"] = ValueKind.Number;
        foreach (var output in contract.Outputs.Where(t => t.Mode == TunnelMode.ConditionalIndexing)) outputs[output.Condition] = ValueKind.Boolean;
        foreach (var (kind, terminals) in new[] { ("input", inputs), ("output", outputs) })
        {
            var remove = body.Nodes.Where(n => n.Kind == kind && !terminals.ContainsKey(kind == "input" ? n.Text : GraphCompiler.ConnectorName(n))).Select(n => n.Id).ToHashSet();
            body.Nodes.RemoveAll(n => remove.Contains(n.Id)); body.Wires.RemoveAll(w => remove.Contains(w.From) || remove.Contains(w.To));
            var row = 0;
            foreach (var (name, type) in terminals)
            {
                var connector = body.Nodes.FirstOrDefault(n => n.Kind == kind && (kind == "input" ? n.Text : GraphCompiler.ConnectorName(n)) == name);
                if (connector is null) body.Nodes.Add(StructuredExamples.Connector(kind, name, type, kind == "input" ? 30 : 620, 40 + row * 90));
                else connector.DataType = type;
                row++;
            }
        }
        if ((owner.Kind == "while" || contract.ConditionalFor) && !body.Nodes.Any(n => n.Kind == "stop")) body.Nodes.Add(Examples.NewNode("stop", 620, 40 + outputs.Count * 90));
    }
    public void StepInto()
    {
        try
        {
            IsRunning = false; IsPaused = true; _frame ??= _runtime.Start(Plan()); _frame.StepInto(); PublishFrame();
            if (_frame.Completed) { CompleteFrame(); _frame = null; IsPaused = false; }
            Status = IsPaused ? "Step into · " + (_frame?.ActiveFrame.Path ?? "root") : "Execution complete";
            Notify(SessionChange.Execution | SessionChange.View);
        }
        catch (Exception error) { ExecutionError(error); }
    }
}
