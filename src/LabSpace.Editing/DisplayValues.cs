using System.Globalization;
using LabSpace.Core;

namespace LabSpace.Editing;

public sealed partial class InstrumentSession
{
    private readonly Dictionary<Node, (string Text, LabType? Type, double Number, Value Value)> _displayValues = [];

    /// <summary>Reads model controls without lossy double conversion; parsed structured literals are retained across frames.</summary>
    public Value DisplayValue(Node node)
    {
        var definition = NodeCatalog.Describe(node);
        if (definition.IsIndicator) return Values.GetValueOrDefault(node.Id) ?? Value.Default(definition.DataType);
        if (_displayValues.TryGetValue(node, out var cached) && cached.Text == node.Text && cached.Type == node.Type && cached.Number == node.Value)
            return cached.Value;
        var value = node.Kind switch
        {
            "typed-control" or "typed-constant" => ValueLiteral.Parse(node.Type ?? LabType.Number, node.Text),
            "bool" or "bool-control" => Value.Bool(node.Value != 0),
            "string" or "string-control" => Value.String(node.Text),
            "array" => Value.Vector(node.Text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(x => double.Parse(x, CultureInfo.InvariantCulture))),
            _ => Values.GetValueOrDefault(node.Id) ?? Value.Numeric(node.Value)
        };
        if (_displayValues.Count > 2048) _displayValues.Clear();
        _displayValues[node] = (node.Text, node.Type, node.Value, value);
        return value;
    }

    public void RemoveBrokenWires()
    {
        var ids = Diagnostics.Where(d => d.WireId is not null && d.Code is "DANGLING" or "TYPE" or "PORT" or "DRIVER" or "ID").Select(d => d.WireId!).ToHashSet(StringComparer.Ordinal);
        if (ids.Count > 0) Edit(() => Diagram.Wires.RemoveAll(w => ids.Contains(w.Id)));
        Message(ids.Count == 0 ? "No broken wires." : $"Removed {ids.Count} broken wires.");
    }

    public void ResetWireRouting()
    {
        if (SelectedWire is not { } id) return;
        Edit(() => Diagram.Wires.First(w => w.Id == id).Waypoints.Clear(), false);
    }
}
