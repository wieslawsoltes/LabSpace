using LabSpace.Core;
using SkiaSharp;

namespace LabSpace.Skia;

public static class DiagramGeometry
{
    public static bool IsArithmetic(Node n) => n.Kind is "add" or "subtract" or "multiply" or "divide" or "min" or "max";
    public static bool IsTerminal(Node n) => NodeCatalog.Get(n.Kind).IsControl || NodeCatalog.Get(n.Kind).IsIndicator;
    public static bool IsConstant(Node n) => n.Kind is "constant" or "typed-constant" or "bool" or "string" or "array";
    public static SKRect Bounds(Node n)
    {
        if (!NodeCatalog.TryGet(n.Kind, out var d)) return new((float)n.X, (float)n.Y, (float)n.X + 80, (float)n.Y + 40);
        var inputs = n.Contract is { } contract ? contract.Inputs.Count + contract.Registers.Count + 1 : d.Inputs.Length;
        var outputs = n.Contract is { } c ? c.Outputs.Count + c.Registers.Count : n.Kind == "unbundle" ? (n.Type?.Fields.Length ?? 2) : n.Kind == "error-fields" ? 3 : 1;
        var width = d.IsStructure ? 300 : n.Kind == "simulate" ? 76 : n.Kind is "bundle" or "unbundle" or "error-fields" ? 118 : IsConstant(n) ? 60 : 36;
        var height = d.IsStructure ? Math.Max(190, 26 * Math.Max(inputs, outputs)) : n.Kind == "simulate" ? 56 : n.Kind is "bundle" or "unbundle" or "error-fields" ? Math.Max(46, 23 * Math.Max(n.Type?.Fields.Length ?? 3, outputs)) : IsTerminal(n) ? 28 : IsConstant(n) ? 24 : 36;
        return new((float)n.X, (float)n.Y, (float)n.X + width, (float)n.Y + height);
    }
    public static SKRect VisualBounds(Node n)
    {
        var b = Bounds(n); var labelWidth = Math.Min(260, n.Label.Length * 7 + 12);
        if (NodeCatalog.Get(n.Kind).IsControl) b.Left -= labelWidth;
        else if (NodeCatalog.Get(n.Kind).IsIndicator) b.Right += labelWidth;
        else { b.Top -= 22; b.Right = Math.Max(b.Right, b.Left + labelWidth); }
        b.Inflate(10, 10); return b;
    }
    public static SKPoint Output(Node n, int index = 0)
    {
        var b = Bounds(n); var count = NodeCatalog.Describe(n).OutputPorts.Length;
        return new(b.Right, b.Top + b.Height * (index + 1) / (count + 1));
    }
    public static SKPoint Output(Node n, string name)
    {
        var schema = NodeCatalog.Describe(n); var canonical = schema.FindOutput(name)?.Name;
        return Output(n, Math.Max(0, System.Array.FindIndex(schema.OutputPorts, p => p.Name == canonical)));
    }
    public static SKPoint Input(Node n, int index)
    {
        var b = Bounds(n); var count = NodeCatalog.Describe(n).Inputs.Length;
        return new(b.Left, b.Top + b.Height * (index + 1) / (count + 1));
    }
    public static SKPoint[] Route(SKPoint from, SKPoint to)
    {
        if (to.X >= from.X + 35) { var mid = (from.X + to.X) / 2; return [from, new(mid, from.Y), new(mid, to.Y), to]; }
        var y = Math.Min(from.Y, to.Y) - 38;
        return [from, new(from.X + 18, from.Y), new(from.X + 18, y), new(to.X - 18, y), new(to.X - 18, to.Y), to];
    }
    public static double Distance(SKPoint p, SKPoint a, SKPoint b) => SKPoint.Distance(p, ClosestPoint(p, a, b));
    public static SKPoint ClosestPoint(SKPoint p, SKPoint a, SKPoint b)
    {
        var dx = b.X - a.X; var dy = b.Y - a.Y; var length = dx * dx + dy * dy;
        var t = length == 0 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / length, 0, 1);
        return new(a.X + t * dx, a.Y + t * dy);
    }
}
