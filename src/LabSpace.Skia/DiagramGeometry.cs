using LabSpace.Core;
using SkiaSharp;

namespace LabSpace.Skia;

public static class DiagramGeometry
{
    public static SKRect Bounds(Node n)
    {
        if (!NodeCatalog.TryGet(n.Kind, out var d)) return new((float)n.X, (float)n.Y, (float)n.X + 100, (float)n.Y + 60);
        d = NodeCatalog.Resolve(n);
        var literal = n.Kind is "constant" or "bool" or "string" or "error-constant" or "complex";
        var w = n.Kind == "formula" ? 270 : d.IsStructure ? 280 : n.Kind == "simulate" ? 118 : d.IsControl || d.IsIndicator ? 40 : literal ? 72 : 48;
        var h = n.Kind == "formula" ? 160 : d.IsStructure ? Math.Max(190, 20 + Math.Max(d.Inputs.Length, d.Outputs.Length) * 24) : n.Kind == "simulate" ? 78 : literal ? 28 : d.IsControl || d.IsIndicator ? 32 : 48;
        h = Math.Max(h, (Math.Max(d.Inputs.Length, d.Outputs.Length) + 1) * 16);
        if (d.IsStructure || n.Kind == "formula")
        {
            if (n.Width > 0) w = (int)Math.Max(n.Kind == "formula" ? 200 : 240, n.Width);
            if (n.Height > 0) h = (int)Math.Max(Math.Max(d.Inputs.Length, d.Outputs.Length) * 24 + 30, Math.Max(n.Kind == "formula" ? 100 : 160, n.Height));
        }
        return new((float)n.X, (float)n.Y, (float)n.X + w, (float)n.Y + h);
    }
    public static SKPoint Output(Node n, int index = 0)
    {
        var b = Bounds(n); var count = NodeCatalog.Resolve(n).Outputs.Length;
        return new(b.Right, b.Top + b.Height * (index + 1) / (count + 1));
    }
    public static SKPoint Output(Node n, string name)
    {
        var d = NodeCatalog.Resolve(n); var output = d.FindOutput(name);
        return Output(n, Math.Max(0, Array.FindIndex(d.Outputs, p => p.Name == output?.Name)));
    }
    public static SKPoint Input(Node n, int index)
    {
        var b = Bounds(n); var count = NodeCatalog.Resolve(n).Inputs.Length;
        return new(b.Left, b.Top + b.Height * (index + 1) / (count + 1));
    }
    public static SKPoint[] Route(SKPoint from, SKPoint to)
    {
        if (to.X >= from.X + 35) { var mid = (from.X + to.X) / 2; return [from, new(mid, from.Y), new(mid, to.Y), to]; }
        var y = Math.Min(from.Y, to.Y) - 38;
        return [from, new(from.X + 22, from.Y), new(from.X + 22, y), new(to.X - 22, y), new(to.X - 22, to.Y), to];
    }
    public static double Distance(SKPoint p, SKPoint a, SKPoint b)
    {
        var dx = b.X - a.X; var dy = b.Y - a.Y; var length = dx * dx + dy * dy;
        var t = length == 0 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / length, 0, 1);
        return Math.Sqrt(Math.Pow(p.X - a.X - t * dx, 2) + Math.Pow(p.Y - a.Y - t * dy, 2));
    }
}
