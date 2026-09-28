using LabSpace.Core;
using LabSpace.Documents;

namespace LabSpace.Editing;

public sealed partial class InstrumentSession
{
    public void ConfigureStructure(string id, StructureContract contract)
    {
        contract.Validate();
        Edit(() =>
        {
            var node = Find(id) ?? throw new ArgumentException("Structure is missing.");
            if (!AdvancedNodes.IsTypedStructure(node.Kind)) throw new ArgumentException("Select a typed structure.");
            node.Contract = contract;
            Synchronize(node.Body ??= new(), contract, node.Kind == "while-loop");
            if (node.Kind == "case-typed") Synchronize(node.Alternative ??= new(), contract, false);
            var schema = NodeCatalog.Describe(node);
            Diagram.Wires.RemoveAll(w => w.To == id && !schema.Inputs.Any(p => p.Name == w.Input) || w.From == id && schema.FindOutput(w.Output) is null);
        });
    }
    private static void Synchronize(Diagram body, StructureContract contract, bool needsStop)
    {
        var required = new List<(string Kind, string Name, LabType Type, int Slot)>();
        required.AddRange(contract.Inputs.Select(t => ("tunnel-in", t.Name, t.Type, 0)));
        required.AddRange(contract.Outputs.Where(t => !t.UseDefaultIfUnwired).Select(t => ("tunnel-out", t.Name, t.Type, 0)));
        foreach (var register in contract.Registers)
        {
            for (var slot = 0; slot < register.Depth; slot++) required.Add(("shift-read", register.Name, register.Type, slot));
            required.Add(("shift-write", register.Name, register.Type, 0));
        }
        var removed = body.Nodes.Where(n => n.Kind is "tunnel-in" or "tunnel-out" or "shift-read" or "shift-write")
            .Where(n => !required.Any(t => t.Kind == n.Kind && t.Name == n.Text && (n.Kind != "shift-read" || t.Slot == n.Parameter("element", 0)))
                && !(n.Kind == "tunnel-out" && contract.Outputs.Any(t => t.Name == n.Text))).Select(n => n.Id).ToHashSet();
        body.Nodes.RemoveAll(n => removed.Contains(n.Id)); body.Wires.RemoveAll(w => removed.Contains(w.From) || removed.Contains(w.To));
        var yIn = 60; var yOut = 60;
        foreach (var terminal in required)
        {
            var n = body.Nodes.FirstOrDefault(n => n.Kind == terminal.Kind && n.Text == terminal.Name && (n.Kind != "shift-read" || n.Parameter("element", 0) == terminal.Slot));
            if (n is null)
            {
                var output = terminal.Kind is "tunnel-out" or "shift-write";
                n = Examples.Typed(terminal.Kind, terminal.Name, terminal.Type, output ? 460 : 40, output ? yOut : yIn, terminal.Name);
                if (terminal.Kind == "shift-read") n.Parameters["element"] = terminal.Slot;
                body.Nodes.Add(n); if (output) yOut += 85; else yIn += 85;
            }
            n.Type = terminal.Type;
        }
        // Default-enabled output terminals may still exist and must receive type changes.
        foreach (var n in body.Nodes.Where(n => n.Kind == "tunnel-out")) n.Type = contract.Outputs.First(t => t.Name == n.Text).Type;
        if (needsStop && !body.Nodes.Any(n => n.Kind == "stop")) body.Nodes.Add(Examples.NewNode("stop", 570, 380));
    }
    public void SetType(string id, LabType type)
    {
        type.Validate(); Edit(() =>
        {
            var node = Find(id) ?? throw new ArgumentException("Node is missing."); node.Type = type;
            if (node.Kind is "typed-control" or "typed-constant") { try { _ = ValueLiteral.Parse(type, node.Text); } catch { node.Text = ""; } }
            _ = NodeCatalog.Describe(node);
            var panel = Path.Count == 0 ? Instrument.Panel.FirstOrDefault(p => p.NodeId == id) : null;
            if (panel is not null) panel.Widget = type.Kind switch { ValueKind.Array => "Array", ValueKind.Cluster => "Cluster", ValueKind.Error => "Error", ValueKind.Enum => "Enum", ValueKind.Boolean => NodeCatalog.Describe(node).IsControl ? "Switch" : "LED", ValueKind.String => "String", _ => "Numeric" };
        });
    }
}
