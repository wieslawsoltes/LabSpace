using LabSpace.Core;

namespace LabSpace.Documents;

public static class FormulaExamples
{
    public static VirtualInstrument ControlFlow()
    {
        var vi = Examples.Blank("Formula Control Flow.vi");
        vi.Description = "A bounded formula loop with a conditional continue, lexical locals and two named outputs. count=10, gain=2 yields result=84, iterations=9.";
        var count = Examples.NewNode("control", 100, 100); count.Value = 10; count.Label = "Count";
        var gain = Examples.NewNode("control", 100, 240); gain.Value = 2; gain.Label = "Gain";
        var formula = Examples.NewNode("formula", 350, 70); formula.Width = 400; formula.Height = 275;
        formula.Formula = new() { Inputs = ["count", "gain"], Outputs = ["result", "iterations"] };
        formula.Text = """
            double total = 0;
            double used = 0;
            for (double i = 0; i < count; i++) {
                if (i == 3) continue;
                total += i * gain;
                used++;
            }
            result = total;
            iterations = used;
            """;
        var sum = Examples.NewNode("indicator", 920, 100); sum.Label = "Result";
        var used = Examples.NewNode("indicator", 920, 240); used.Label = "Iterations";
        vi.Diagram.Nodes.AddRange([count, gain, formula, sum, used]);
        Examples.Connect(vi.Diagram, count, formula, "count"); Examples.Connect(vi.Diagram, gain, formula, "gain");
        vi.Diagram.Wires.Add(new() { From = formula.Id, Output = "result", To = sum.Id, Input = "x", Probe = true });
        vi.Diagram.Wires.Add(new() { From = formula.Id, Output = "iterations", To = used.Id, Input = "x" });
        vi.Panel.AddRange([
            new() { NodeId = count.Id, Bounds = new(60, 80, 180, 95), Minimum = 0, Maximum = 1000 },
            new() { NodeId = gain.Id, Bounds = new(300, 80, 180, 95), Minimum = -100, Maximum = 100 },
            new() { NodeId = sum.Id, Bounds = new(60, 250, 180, 95), Maximum = 1000000 },
            new() { NodeId = used.Id, Bounds = new(300, 250, 180, 95), Maximum = 1000 }
        ]);
        return vi;
    }
}
