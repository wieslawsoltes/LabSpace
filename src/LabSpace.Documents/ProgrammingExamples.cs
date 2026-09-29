using LabSpace.Core;

namespace LabSpace.Documents;

public static class ProgrammingExamples
{
    public static void Initialize(Node node)
    {
        if (node.Kind == "formula")
        {
            node.Contract = new() { Inputs = [new() { Name = "x" }], Outputs = [new() { Name = "result" }] };
            node.Text = "result = x * x;";
        }
        if (node.Kind == "sequence")
        {
            node.Contract = new() { Inputs = [new() { Name = "x", Required = false }], Outputs = [new() { Name = "result" }] };
            var frame = new StructureFrame { Selector = "0" };
            var input = StructuredExamples.Connector("input", "x", ValueKind.Number, 40, 90);
            var output = StructuredExamples.Connector("output", "result", ValueKind.Number, 390, 90);
            frame.Diagram.Nodes.AddRange([input, output]); Examples.Connect(frame.Diagram, input, output); node.Frames.Add(frame);
        }
        if (node.Kind == "complex-constant") node.Parameters["imaginary"] = 1;
        if (node.Kind == "error-constant") node.Parameters["status"] = 0;
    }
    public static StructureFrame ConstantFrame(string selector, double number, bool isDefault = false)
    {
        var frame = new StructureFrame { Selector = selector, IsDefault = isDefault };
        var constant = Examples.NewNode("constant", 55, 90); constant.Value = number;
        var output = StructuredExamples.Connector("output", "result", ValueKind.Number, 390, 90);
        frame.Diagram.Nodes.AddRange([constant, output]); Examples.Connect(frame.Diagram, constant, output); return frame;
    }
    public static VirtualInstrument MultiCase()
    {
        var vi = Examples.Blank("Multi-case Dispatch.vi");
        vi.Description = "Numeric Case with inclusive ranges, an explicit default, and an editable subdiagram selector. -5 → -1; 0 → 0; 5 → 1; 50 → 99.";
        var control = Examples.NewNode("control", 40, 100); control.Label = "Selector"; control.Value = 5;
        var node = new Node { Kind = "case", Label = "Range dispatch", X = 300, Y = 65,
            Contract = new() { SelectorType = ValueKind.Number, Outputs = [new() { Name = "result" }] },
            Frames = [ConstantFrame("..-1", -1), ConstantFrame("0", 0), ConstantFrame("1..10", 1), ConstantFrame("", 99, true)] };
        var indicator = Examples.NewNode("indicator", 740, 120); indicator.Label = "Selected branch result";
        vi.Diagram.Nodes.AddRange([control, node, indicator]); Examples.Connect(vi.Diagram, control, node, "selector"); Examples.Connect(vi.Diagram, node, indicator);
        vi.Panel.AddRange([new() { NodeId = control.Id, Bounds = new(65, 100, 180, 90), Minimum = -100, Maximum = 100 }, new() { NodeId = indicator.Id, Bounds = new(380, 100, 220, 90), Minimum = -100, Maximum = 100 }]);
        return vi;
    }
    public static VirtualInstrument Sequence()
    {
        var vi = Examples.Blank("Sequence and Formula.vi");
        vi.Description = "Frame 0 acquires x. Frame 1 reads a typed sequence local and computes result = x*x + 2. With x=3 the result is 11. Step into both real frames.";
        var control = Examples.NewNode("control", 40, 100); control.Label = "x"; control.Value = 3;
        var first = new StructureFrame { Selector = "Acquire" }; var second = new StructureFrame { Selector = "Compute" };
        var read = StructuredExamples.Connector("input", "x", ValueKind.Number, 50, 80); var save = StructuredExamples.Connector("output", "sample", ValueKind.Number, 410, 80);
        first.Diagram.Nodes.AddRange([read, save]); Examples.Connect(first.Diagram, read, save);
        var local = StructuredExamples.Connector("input", "sample", ValueKind.Number, 40, 90); var formula = Examples.NewNode("formula", 240, 55); formula.Text = "// Calibrated scalar measurement\nresult = x * x + 2;";
        var result = StructuredExamples.Connector("output", "result", ValueKind.Number, 670, 90);
        second.Diagram.Nodes.AddRange([local, formula, result]); Examples.Connect(second.Diagram, local, formula); Examples.Connect(second.Diagram, formula, result);
        var sequence = new Node { Kind = "sequence", Label = "Acquire and compute", X = 300, Y = 65, Frames = [first, second], Contract = new()
        { Inputs = [new() { Name = "x" }], Outputs = [new() { Name = "result" }], Locals = [new() { Name = "sample", SourceFrameId = first.Id }] } };
        var indicator = Examples.NewNode("indicator", 770, 110); indicator.Label = "Calibrated result";
        vi.Diagram.Nodes.AddRange([control, sequence, indicator]); Examples.Connect(vi.Diagram, control, sequence); Examples.Connect(vi.Diagram, sequence, indicator);
        vi.Panel.AddRange([new() { NodeId = control.Id, Bounds = new(65, 100, 180, 90), Minimum = -100, Maximum = 100 }, new() { NodeId = indicator.Id, Bounds = new(380, 100, 220, 90), Maximum = 10000 }]);
        return vi;
    }
    public static VirtualInstrument ComplexMeasurement()
    {
        var vi = Examples.Blank("Complex Measurement.vi"); vi.Description = "Build 3+4i, compute its magnitude and inspect independent real/imaginary outputs. |3+4i| = 5.";
        var real = Examples.NewNode("control", 60, 80); real.Label = "Real"; real.Value = 3;
        var imaginary = Examples.NewNode("control", 60, 240); imaginary.Label = "Imaginary"; imaginary.Value = 4;
        var build = Examples.NewNode("complex-build", 320, 135); var magnitude = Examples.NewNode("complex-magnitude", 520, 135);
        var result = Examples.NewNode("indicator", 770, 135); result.Label = "Magnitude";
        var parts = Examples.NewNode("complex-parts", 520, 300);
        vi.Diagram.Nodes.AddRange([real, imaginary, build, magnitude, result, parts]); Examples.Connect(vi.Diagram, real, build, "real"); Examples.Connect(vi.Diagram, imaginary, build, "imaginary"); Examples.Connect(vi.Diagram, build, magnitude); Examples.Connect(vi.Diagram, magnitude, result); Examples.Connect(vi.Diagram, build, parts);
        vi.Panel.AddRange([new() { NodeId = real.Id, Bounds = new(65, 80, 180, 90), Minimum = -100, Maximum = 100 }, new() { NodeId = imaginary.Id, Bounds = new(65, 240, 180, 90), Minimum = -100, Maximum = 100 }, new() { NodeId = result.Id, Bounds = new(430, 120, 210, 100), Maximum = 1000 }]);
        return vi;
    }
}
