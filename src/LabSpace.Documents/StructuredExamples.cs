using LabSpace.Core;

namespace LabSpace.Documents;

/// <summary>Executable examples of named terminals, numeric auto-indexing and shift-register accumulation.</summary>
public static class StructuredExamples
{
    public static VirtualInstrument IndexedAccumulator()
    {
        var vi = Examples.Blank("Indexed Accumulator.vi");
        vi.Description = "A For Loop auto-indexes the input array, carries a shift register and publishes both the running sums and final value. Double-click the loop to edit its actual body.";
        var values = Examples.NewNode("array", 50, 100); values.Label = "Samples"; values.Text = "1, 2, 3, 4, 5";
        var initial = Examples.NewNode("control", 50, 280); initial.Label = "Initial sum";
        var loop = Examples.NewNode("for", 340, 100); loop.Label = "Running sum";
        loop.Contract = new()
        {
            Inputs = [new() { Name = "sample", Type = ValueKind.Number, Indexing = true }],
            Outputs = [new() { Name = "totals", Type = ValueKind.Number, Mode = TunnelMode.Indexing }],
            Registers = [new() { Name = "state", Type = ValueKind.Number }], PrimaryOutput = "state"
        };
        var sample = Connector("input", "sample", ValueKind.Number, 55, 65);
        var state = Connector("input", "state", ValueKind.Number, 55, 195);
        var add = Examples.NewNode("add", 260, 95);
        var next = Connector("output", "state", ValueKind.Number, 470, 80);
        var totals = Connector("output", "totals", ValueKind.Number, 470, 225);
        loop.Body = new() { Nodes = [sample, state, add, next, totals] };
        Examples.Connect(loop.Body, sample, add, "x"); Examples.Connect(loop.Body, state, add, "y"); Examples.Connect(loop.Body, add, next); Examples.Connect(loop.Body, add, totals);
        var final = Examples.NewNode("indicator", 790, 110); final.Label = "Final sum";
        var array = Examples.NewNode("array-indicator", 790, 280); array.Label = "Running sums";
        vi.Diagram.Nodes.AddRange([values, initial, loop, final, array]);
        Examples.Connect(vi.Diagram, values, loop, "sample"); Examples.Connect(vi.Diagram, initial, loop, "initial:state");
        Examples.Connect(vi.Diagram, loop, final);
        vi.Diagram.Wires.Add(new() { From = loop.Id, Output = "totals", To = array.Id, Input = "x" });
        vi.Panel.AddRange([
            new() { NodeId = initial.Id, Bounds = new(70, 90, 160, 90), Maximum = 1000 },
            new() { NodeId = final.Id, Bounds = new(340, 90, 200, 100), Maximum = 1000 },
            new() { NodeId = array.Id, Widget = "Array", Bounds = new(610, 90, 220, 300), Maximum = 1000 }
        ]);
        return vi;
    }
    public static Node Connector(string kind, string name, ValueKind type, double x, double y) => new() { Kind = kind, Text = name, Label = name, DataType = type, X = x, Y = y };
}
