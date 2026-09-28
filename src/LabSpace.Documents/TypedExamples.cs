using LabSpace.Core;

namespace LabSpace.Documents;

public static partial class Examples
{
    public static Node Typed(string kind, string name, LabType type, double x, double y, string text = "") => new() { Kind = kind, Label = name, Type = type, Text = text, X = x, Y = y };
    public static void ConfigureTypedStructure(Node node)
    {
        var loop = node.Kind is "for-loop" or "while-loop";
        var forLoop = node.Kind == "for-loop";
        node.Contract = new(); node.Body = new();
        if (!loop)
        {
            node.Contract.Inputs.Add(new() { Name = "x" }); node.Contract.Outputs.Add(new());
            var source = Typed("tunnel-in", "x", LabType.Number, 45, 80, "x"); var sink = Typed("tunnel-out", "value", LabType.Number, 330, 80, "value");
            node.Body.Nodes.AddRange([source, sink]); Connect(node.Body, source, sink);
            if (node.Kind == "case-typed")
            {
                node.Alternative = new(); var a = Typed("tunnel-in", "x", LabType.Number, 45, 80, "x"); var b = Typed("tunnel-out", "value", LabType.Number, 330, 80, "value"); node.Alternative.Nodes.AddRange([a, b]); Connect(node.Alternative, a, b);
            }
            return;
        }
        if (forLoop) node.Contract.Inputs.Add(new() { Name = "values", Mode = TunnelMode.Indexing });
        node.Contract.Outputs.Add(new() { Mode = forLoop ? TunnelMode.Indexing : TunnelMode.LastValue });
        node.Contract.Registers.Add(new() { Name = "sum", InitialValue = "0" });
        var item = forLoop ? Typed("tunnel-in", "values [ ]", LabType.Number, 35, 65, "values") : NewNode("constant", 35, 65);
        if (!forLoop) { item.Value = 1; item.Label = "increment"; }
        var previous = Typed("shift-read", "sum ↓", LabType.Number, 35, 190, "sum");
        var add = NewNode("add", 215, 95); var output = Typed("tunnel-out", "value [ ]", LabType.Number, 415, 65, "value");
        var next = Typed("shift-write", "sum ↑", LabType.Number, 415, 190, "sum");
        node.Body.Nodes.AddRange([item, previous, add, output, next]); Connect(node.Body, item, add, "x"); Connect(node.Body, previous, add, "y"); Connect(node.Body, add, output); Connect(node.Body, add, next);
        if (!forLoop)
        {
            var iteration = NewNode("iteration", 40, 310); var limit = NewNode("constant", 210, 380); limit.Value = 8; limit.Label = "last iteration − 1";
            var greater = NewNode("greater", 400, 320); var stop = NewNode("stop", 570, 320); node.Body.Nodes.AddRange([iteration, limit, greater, stop]); Connect(node.Body, iteration, greater, "x"); Connect(node.Body, limit, greater, "y"); Connect(node.Body, greater, stop);
        }
    }
    public static VirtualInstrument IndexedLoop()
    {
        var vi = Blank("Auto-index & Registers.vi"); vi.Description = "An executable prefix sum: an indexed input drives For Loop count, a real shift register carries the sum, and two independently wireable outputs expose all prefixes and the final total.";
        var source = Typed("typed-control", "Input array", LabType.Array(LabType.Number), 45, 140, "[1,2,3,4,5]");
        var loop = NewNode("for-loop", 240, 85); var array = Typed("typed-indicator", "Prefix sums", LabType.Array(LabType.Number), 600, 90);
        var total = NewNode("indicator", 600, 255); total.Label = "Final sum";
        vi.Diagram.Nodes.AddRange([source, loop, array, total]); Connect(vi.Diagram, source, loop, "values"); Connect(vi.Diagram, loop, array);
        vi.Diagram.Wires.Add(new() { From = loop.Id, Output = "shift:sum", To = total.Id });
        vi.Panel.AddRange([new() { NodeId = source.Id, Widget = "Array", Bounds = new(60, 90, 210, 220) }, new() { NodeId = array.Id, Widget = "Array", Bounds = new(360, 90, 210, 220) }, new() { NodeId = total.Id, Widget = "Numeric", Bounds = new(660, 90, 180, 90), Maximum = 1000 }]);
        return vi;
    }
    public static VirtualInstrument TypedData()
    {
        var vi = Blank("Typed Data.vi"); vi.Description = "Exact U64 data, ordered clusters, multi-output unbundling, and explicit error-cluster construction. The serial number remains exact beyond double precision.";
        var type = LabType.Cluster(new("serial", LabType.Scalar(ValueKind.UInt64)), new("temperature", LabType.Number), new("valid", LabType.Boolean));
        var data = Typed("typed-control", "Measurement", type, 45, 90, "{\"serial\":18446744073709551615,\"temperature\":23.75,\"valid\":true}");
        var unbundle = Typed("unbundle", "Unbundle", type, 285, 90);
        var serial = Typed("typed-indicator", "Serial number (U64)", LabType.Scalar(ValueKind.UInt64), 540, 60);
        var temperature = NewNode("indicator", 540, 180); temperature.Label = "Temperature";
        var valid = NewNode("bool-indicator", 540, 285); valid.Label = "Valid";
        vi.Diagram.Nodes.AddRange([data, unbundle, serial, temperature, valid]); Connect(vi.Diagram, data, unbundle);
        foreach (var (name, target) in new[] { ("serial", serial), ("temperature", temperature), ("valid", valid) }) vi.Diagram.Wires.Add(new() { From = unbundle.Id, Output = name, To = target.Id });
        vi.Panel.AddRange([new() { NodeId = data.Id, Widget = "Cluster", Bounds = new(65, 85, 350, 215) }, new() { NodeId = serial.Id, Widget = "Numeric", Bounds = new(490, 85, 300, 85) }, new() { NodeId = temperature.Id, Widget = "Thermometer", Bounds = new(500, 210, 140, 260), Minimum = -20, Maximum = 100 }, new() { NodeId = valid.Id, Widget = "LED", Bounds = new(700, 230, 100, 85) }]);
        return vi;
    }
}
