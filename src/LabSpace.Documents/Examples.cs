using LabSpace.Core;

namespace LabSpace.Documents;

public static class Examples
{
    public static LabProject Create() => new() { Name = "Signal analysis.lvproj", Instruments = [SignalAnalysis(), Arithmetic(), Loop(), StructuredExamples.IndexedAccumulator(), ProgrammingExamples.MultiCase(), ProgrammingExamples.Sequence(), ProgrammingExamples.ComplexMeasurement()] };
    public static VirtualInstrument Blank(string name = "Untitled.vi") => new() { Name = name, Description = "Add controls and functions, wire compatible terminals, then Run." };
    public static Node NewNode(string kind, double x, double y)
    {
        var d = NodeCatalog.Get(kind); var n = new Node { Kind = kind, Label = d.Title, X = x, Y = y };
        if (kind == "simulate") { n.Parameters["count"] = 512; n.Parameters["rate"] = 2000; n.Parameters["noise"] = .02; n.Text = "Sine"; }
        if (kind == "filter") n.Parameters["window"] = 8;
        if (kind == "array") n.Text = "1, 2, 3, 4, 5";
        if (kind == "input") n.Text = "state";
        if (d.IsStructure && kind != "sequence") { n.Body = StructureBody(kind == "while"); if (kind == "case") n.Alternative = StructureBody(false, 2); }
        ProgrammingExamples.Initialize(n);
        return n;
    }
    public static Diagram StructureBody(bool stop, double increment = 1)
    {
        var graph = new Diagram();
        var state = NewNode("input", 45, 75); state.Label = "state"; state.Text = "state";
        var one = NewNode("constant", 45, 175); one.Label = "increment"; one.Value = increment;
        var add = NewNode("add", 240, 100); var output = NewNode("output", 445, 100);
        graph.Nodes.AddRange([state, one, add, output]); Connect(graph, state, add, "x"); Connect(graph, one, add, "y"); Connect(graph, add, output);
        if (stop)
        {
            var i = NewNode("input", 45, 300); i.Label = "iteration"; i.Text = "i";
            var count = NewNode("constant", 220, 380); count.Value = 8; count.Label = "last iteration − 1";
            var compare = NewNode("greater", 420, 300); var condition = NewNode("stop", 610, 300);
            graph.Nodes.AddRange([i, count, compare, condition]); Connect(graph, i, compare, "x"); Connect(graph, count, compare, "y"); Connect(graph, compare, condition);
        }
        return graph;
    }
    public static void Connect(Diagram graph, Node from, Node to, string input = "x") => graph.Wires.Add(new() { From = from.Id, To = to.Id, Input = input });
    public static VirtualInstrument SignalAnalysis()
    {
        var vi = new VirtualInstrument { Name = "Signal Analysis.vi", Description = "SIMULATED ACQUISITION • Adjust amplitude and frequency, then run. Orange = numeric, green = Boolean, brown = waveform. No hardware is connected." };
        Node Add(string kind, string title, double x, double y, double value = 0) { var n = NewNode(kind, x, y); n.Label = title; n.Value = value; vi.Diagram.Nodes.Add(n); return n; }
        var amplitude = Add("control", "Amplitude (V)", 45, 70, 2.5);
        var frequency = Add("control", "Frequency (Hz)", 45, 210, 25);
        var signal = Add("simulate", "Simulate Signal", 255, 105);
        var filter = Add("filter", "Low-pass filter", 470, 105);
        var graph = Add("graph", "Acquired signal", 690, 55);
        var rms = Add("rms", "RMS", 475, 260);
        var readout = Add("indicator", "RMS voltage", 690, 240);
        var limit = Add("constant", "Limit (V)", 475, 415, 2);
        var comparator = Add("greater", "Over limit?", 890, 270);
        var led = Add("bool-indicator", "Alarm", 1070, 270);
        var fft = Add("fft", "FFT magnitude", 255, 430);
        var spectrum = Add("graph", "Amplitude spectrum (Hz)", 690, 470);
        var chart = Add("chart", "Signal history", 890, 55);
        var d = vi.Diagram;
        Connect(d, amplitude, signal, "amplitude"); Connect(d, frequency, signal, "frequency"); Connect(d, signal, filter); Connect(d, filter, graph); Connect(d, filter, rms); Connect(d, rms, readout); Connect(d, rms, comparator, "x"); Connect(d, limit, comparator, "y"); Connect(d, comparator, led); Connect(d, signal, fft); Connect(d, fft, spectrum); Connect(d, filter, chart);
        vi.Panel.AddRange([
            new() { NodeId = amplitude.Id, Widget = "Knob", Bounds = new(30, 55, 155, 175), Minimum = 0, Maximum = 10 },
            new() { NodeId = frequency.Id, Widget = "Numeric", Bounds = new(35, 255, 145, 85), Minimum = 0, Maximum = 1000 },
            new() { NodeId = graph.Id, Widget = "Graph", Bounds = new(225, 55, 570, 255) },
            new() { NodeId = readout.Id, Widget = "Gauge", Bounds = new(835, 65, 240, 180), Minimum = 0, Maximum = 5 },
            new() { NodeId = led.Id, Widget = "LED", Bounds = new(900, 265, 115, 85) },
            new() { NodeId = spectrum.Id, Widget = "Graph", Bounds = new(225, 355, 570, 225) },
            new() { NodeId = chart.Id, Widget = "Chart", Bounds = new(835, 385, 240, 195) }
        ]);
        return vi;
    }
    public static VirtualInstrument Arithmetic()
    {
        var vi = Blank("Arithmetic.vi"); vi.Description = "Change x and y. The same values appear in the panel and diagram; the indicator receives x + y.";
        var x = NewNode("control", 80, 80); x.Label = "x"; x.Value = 3;
        var y = NewNode("control", 80, 220); y.Label = "y"; y.Value = 7;
        var add = NewNode("add", 320, 130); var output = NewNode("indicator", 570, 130); output.Label = "x + y";
        vi.Diagram.Nodes.AddRange([x, y, add, output]); Connect(vi.Diagram, x, add, "x"); Connect(vi.Diagram, y, add, "y"); Connect(vi.Diagram, add, output);
        vi.Panel.AddRange([new() { NodeId = x.Id, Bounds = new(80, 90, 180, 90) }, new() { NodeId = y.Id, Bounds = new(80, 245, 180, 90) }, new() { NodeId = output.Id, Bounds = new(410, 150, 230, 110), Maximum = 100 }]);
        return vi;
    }
    public static VirtualInstrument Loop()
    {
        var vi = Blank("Stateful Loop.vi"); vi.Description = "Double-click For Loop to edit its real nested body. It adds one to the carried state on each iteration.";
        var count = NewNode("control", 70, 85); count.Label = "Iterations"; count.Value = 10;
        var loop = NewNode("for", 320, 95); var output = NewNode("indicator", 680, 95); output.Label = "Final state";
        vi.Diagram.Nodes.AddRange([count, loop, output]); Connect(vi.Diagram, count, loop, "count"); Connect(vi.Diagram, loop, output);
        vi.Panel.AddRange([new() { NodeId = count.Id, Bounds = new(70, 95, 180, 90), Maximum = 10000 }, new() { NodeId = output.Id, Bounds = new(400, 95, 200, 100), Maximum = 10000 }]);
        return vi;
    }
}
