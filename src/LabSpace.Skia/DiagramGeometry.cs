using LabSpace.Core;
using SkiaSharp;

namespace LabSpace.Skia;

public static class DiagramGeometry
{
    public static SKRect Bounds(Node n)
    {
        if (!NodeCatalog.TryGet(n.Kind, out var d)) return new((float)n.X, (float)n.Y, (float)n.X + 100, (float)n.Y + 60);
        var w = d.IsStructure ? 160 : n.Kind == "simulate" ? 112 : d.IsControl || d.IsIndicator || n.Kind.EndsWith("constant") || n.Kind == "constant" ? 92 : 76;
        var h = d.IsStructure ? 102 : n.Kind == "simulate" ? 82 : d.IsControl || d.IsIndicator || n.Kind == "constant" ? 48 : 62;
        return new((float)n.X, (float)n.Y, (float)n.X + w, (float)n.Y + h);
    }
    public static SKPoint Output(Node n) { var b = Bounds(n); return new(b.Right, b.MidY); }
    public static SKPoint Input(Node n, int index)
    {
        var b = Bounds(n); var count = NodeCatalog.Get(n.Kind).Inputs.Length;
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
