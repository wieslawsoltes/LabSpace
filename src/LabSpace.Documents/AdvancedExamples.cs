using LabSpace.Core;

namespace LabSpace.Documents;

/// <summary>Executable examples; initialization is shared by palette, Quick Drop and programmatic insertion.</summary>
public static class AdvancedExamples
{
    public static void Initialize(Node node)
    {
        if (node.Kind == "formula") { node.Formula = new(); node.Text = "result = x;"; }
        if (node.Kind is "error-constant" or "error-control") node.Parameters["status"] = 0;
        if (node.Kind is "complex" or "complex-control") node.Parameters["imaginary"] = 0;
        if (node.Kind is "sequence-read" or "sequence-write") node.Text = "value";
        if (!StructureFrames.HasFrames(node)) return;
        node.Body = node.Alternative = null;
        node.Contract = new() { Outputs = [new() { Name = "result", UseDefaultIfUnwired = true }] };
        node.Width = 360; node.Height = 240;
        if (node.Kind == "case-multi")
        {
            node.DataType = ValueKind.Boolean;
            node.Frames = [ConstantFrame("True", 1), ConstantFrame("False", 0)];
        }
        else node.Frames = [new() { Label = "Frame 0" }, ConstantFrame("Frame 1", 1)];
    }
    public static StructureFrame ConstantFrame(string label, double value, bool fallback = false)
    {
        var constant = Examples.NewNode("constant", 80, 80); constant.Value = value;
        var output = StructuredExamples.Connector("output", "result", ValueKind.Number, 300, 80);
        var frame = new StructureFrame { Label = label, IsDefault = fallback };
        frame.Diagram.Nodes.AddRange([constant, output]); Examples.Connect(frame.Diagram, constant, output); return frame;
    }
    public static VirtualInstrument CaseDispatch()
    {
        var vi = Examples.Blank("Case Dispatch.vi"); vi.Description = "String-selected execution: run, idle or default. The visible case is an editing choice, not the runtime selector.";
        var selector = Examples.NewNode("string-control", 100, 140); selector.Label = "Command"; selector.Text = "run";
        var cases = Examples.NewNode("case-multi", 360, 90); cases.DataType = ValueKind.String; cases.Label = "Command dispatch";
        cases.Frames = [ConstantFrame("\"run\", \"start\"", 42), ConstantFrame("\"idle\"", 0), ConstantFrame("", -1, true)];
        var output = Examples.NewNode("indicator", 870, 170); output.Label = "Command result";
        vi.Diagram.Nodes.AddRange([selector, cases, output]); Examples.Connect(vi.Diagram, selector, cases, "selector"); Examples.Connect(vi.Diagram, cases, output);
        vi.Panel.AddRange([new() { NodeId = selector.Id, Widget = "String", Bounds = new(70, 70, 250, 90) }, new() { NodeId = output.Id, Bounds = new(400, 70, 220, 90), Minimum = -100, Maximum = 100 }]);
        return vi;
    }
    public static VirtualInstrument Sequence()
    {
        var vi = Examples.Blank("Sequence Pipeline.vi"); vi.Description = "Frame 0 writes a typed local. Frame 1 reads it and evaluates a compiled formula. The external result is 84.";
        var sequence = Examples.NewNode("sequence", 150, 90); sequence.Label = "Ordered pipeline";
        var first = new StructureFrame { Label = "Acquire" }; var second = new StructureFrame { Label = "Process" };
        var input = Examples.NewNode("constant", 50, 80); input.Value = 21;
        var write = Examples.NewNode("sequence-write", 270, 80); write.Text = "sample";
        first.Diagram.Nodes.AddRange([input, write]); Examples.Connect(first.Diagram, input, write);
        var read = Examples.NewNode("sequence-read", 40, 90); read.Text = "sample";
        var formula = Examples.NewNode("formula", 240, 80); formula.Text = "result = x * 4;";
        var output = StructuredExamples.Connector("output", "result", ValueKind.Number, 540, 90);
        second.Diagram.Nodes.AddRange([read, formula, output]); Examples.Connect(second.Diagram, read, formula); Examples.Connect(second.Diagram, formula, output);
        sequence.Frames = [first, second]; sequence.Contract = new() { Outputs = [new() { Name = "result" }] };
        var indicator = Examples.NewNode("indicator", 690, 190); indicator.Label = "Processed value";
        vi.Diagram.Nodes.AddRange([sequence, indicator]); Examples.Connect(vi.Diagram, sequence, indicator);
        vi.Panel.Add(new() { NodeId = indicator.Id, Bounds = new(100, 100, 230, 100), Maximum = 1000 });
        return vi;
    }
    public static VirtualInstrument ErrorsAndComplex()
    {
        var vi = Examples.Blank("Errors and Complex.vi"); vi.Description = "Error propagation and distinct named outputs. Complex input 3+4i has magnitude 5.";
        var error = Examples.NewNode("error-control", 80, 90); error.Value = 17; error.Parameters["status"] = 1; error.Text = "Acquisition unavailable";
        var errorView = Examples.NewNode("error-indicator", 360, 90);
        var split = Examples.NewNode("error-unbundle", 360, 220);
        var code = Examples.NewNode("indicator", 650, 240); code.Label = "Error code";
        var complex = Examples.NewNode("complex-control", 80, 410); complex.Value = 3; complex.Parameters["imaginary"] = 4; complex.Label = "Phasor";
        var magnitude = Examples.NewNode("complex-magnitude", 360, 410);
        var absolute = Examples.NewNode("indicator", 650, 410); absolute.Label = "Magnitude";
        vi.Diagram.Nodes.AddRange([error, errorView, split, code, complex, magnitude, absolute]);
        Examples.Connect(vi.Diagram, error, errorView); Examples.Connect(vi.Diagram, error, split);
        vi.Diagram.Wires.Add(new() { From = split.Id, Output = "code", To = code.Id, Input = "x" });
        Examples.Connect(vi.Diagram, complex, magnitude); Examples.Connect(vi.Diagram, magnitude, absolute);
        vi.Panel.AddRange([
            new() { NodeId = error.Id, Widget = "Error", Bounds = new(50, 50, 310, 160) },
            new() { NodeId = errorView.Id, Widget = "Error", Bounds = new(410, 50, 310, 160) },
            new() { NodeId = code.Id, Bounds = new(760, 50, 160, 90), Maximum = 100000 },
            new() { NodeId = complex.Id, Widget = "Complex", Bounds = new(50, 300, 280, 110) },
            new() { NodeId = absolute.Id, Bounds = new(410, 300, 180, 90), Maximum = 100 }
        ]);
        return vi;
    }
}
