namespace LabSpace.Core;

public readonly record struct PointD(double X, double Y);
public readonly record struct RectD(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public bool Contains(PointD p) => p.X >= X && p.X <= Right && p.Y >= Y && p.Y <= Bottom;
}

public sealed class LabProject
{
    public int FormatVersion { get; set; } = 2;
    public string Name { get; set; } = "Untitled project";
    public List<VirtualInstrument> Instruments { get; set; } = [];
}

public sealed class VirtualInstrument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Untitled.vi";
    public string Description { get; set; } = "";
    public Diagram Diagram { get; set; } = new();
    public List<PanelItem> Panel { get; set; } = [];
}

public sealed class Diagram
{
    public List<Node> Nodes { get; set; } = [];
    public List<Wire> Wires { get; set; } = [];
}

public sealed class Node
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Kind { get; set; } = "constant";
    public string Label { get; set; } = "Numeric constant";
    public double X { get; set; }
    public double Y { get; set; }
    public double Value { get; set; }
    public string Text { get; set; } = "";
    public Dictionary<string, double> Parameters { get; set; } = [];
    public bool Breakpoint { get; set; }
    public ValueKind DataType { get; set; }
    public StructureContract? Contract { get; set; }
    public Diagram? Body { get; set; }
    public Diagram? Alternative { get; set; }
    public double Parameter(string name, double fallback) => Parameters.TryGetValue(name, out var result) ? result : fallback;
}

public sealed class Wire
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string Input { get; set; } = "x";
    public string Output { get; set; } = "result";
    public bool Probe { get; set; }
}

public sealed class PanelItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string NodeId { get; set; } = "";
    public string Widget { get; set; } = "Numeric";
    public RectD Bounds { get; set; } = new(30, 30, 140, 80);
    public double Minimum { get; set; }
    public double Maximum { get; set; } = 10;
}
