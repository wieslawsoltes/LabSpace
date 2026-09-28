using LabSpace.Core;
using LabSpace.Editing;
using SkiaSharp;

namespace LabSpace.Skia;

public sealed class DiagramRenderer(LabFonts fonts) : IDisposable
{
    private readonly LabDrawing _d = new(fonts);
    private readonly Dictionary<string, (SKPoint[] Points, SKPath Path, SKColor Color)> _wires = [];
    private int _signature;
    public void Draw(SKCanvas c, InstrumentSession session, SKRect viewport, string? wiringFrom = null, SKPoint? pointer = null, SKRect? marquee = null)
    {
        c.Clear(SKColors.White); var graph = session.Diagram; EnsureGeometry(graph);
        // Visible-area grid only; work outside the viewport is never rasterized.
        var startX = Math.Floor(viewport.Left / 20) * 20; var startY = Math.Floor(viewport.Top / 20) * 20;
        for (var x = startX; x < viewport.Right; x += 20) for (var y = startY; y < viewport.Bottom; y += 20) _d.Rect(c, (float)x, (float)y, 1, 1, "#EDEDED");
        foreach (var wire in graph.Wires)
        {
            if (!_wires.TryGetValue(wire.Id, out var geometry) || !geometry.Path.Bounds.IntersectsWith(viewport)) continue;
            if (session.SelectedWire == wire.Id) _d.Path(c, geometry.Path, LabDrawing.Color("#92BDEC"), 6);
            _d.Path(c, geometry.Path, geometry.Color, 2.1f);
            var points = geometry.Points;
            if (wire.Probe)
            {
                var p = points[Math.Min(2, points.Length - 1)]; var value = session.Values.TryGetValue(wire.From, out var v) ? v.ToString() : "not executed";
                var box = new SKRect(p.X + 7, p.Y - 25, p.X + 145, p.Y - 4); _d.Bevel(c, box, "#FFFFDA"); _d.Text(c, value.Length > 22 ? value[..21] + "…" : value, box.Left + 5, box.Top + 15, 11);
            }
        }
        foreach (var n in graph.Nodes)
        {
            var bounds = DiagramGeometry.Bounds(n); var expanded = bounds; expanded.Inflate(90, 30);
            if (expanded.IntersectsWith(viewport)) DrawNode(c, n, bounds, session);
        }
        if (wiringFrom is not null && pointer.HasValue)
        {
            var n = graph.Nodes.FirstOrDefault(n => n.Id == wiringFrom);
            if (n is not null) { var points = DiagramGeometry.Route(DiagramGeometry.Output(n), pointer.Value); using var path = BuildPath(points); _d.Path(c, path, LabDrawing.WireColor(NodeCatalog.Get(n.Kind).Output), 2); _d.Circle(c, pointer.Value.X, pointer.Value.Y, 4, LabDrawing.Color("#277BC0"), false); }
        }
        if (marquee.HasValue) { _d.Rect(c, marquee.Value, new SKColor(50, 120, 210, 22)); _d.Border(c, marquee.Value, LabDrawing.Color("#3175C4")); }
        if (graph.Nodes.Count == 0) { _d.Text(c, "Build your first virtual instrument", viewport.MidX, viewport.MidY - 16, 23, "#747474", true); _d.Text(c, "Choose a function from the palette, then connect its terminals.", viewport.MidX, viewport.MidY + 15, 14, "#858585", true); }
    }
    private void EnsureGeometry(Diagram graph)
    {
        var hash = new HashCode(); hash.Add(graph);
        foreach (var n in graph.Nodes) { hash.Add(n.Id); hash.Add(n.X); hash.Add(n.Y); hash.Add(n.Kind); }
        foreach (var w in graph.Wires) { hash.Add(w.Id); hash.Add(w.From); hash.Add(w.To); hash.Add(w.Input); }
        var signature = hash.ToHashCode(); if (signature == _signature) return; _signature = signature;
        foreach (var wire in _wires.Values) wire.Path.Dispose(); _wires.Clear();
        var nodes = graph.Nodes.ToDictionary(n => n.Id);
        foreach (var w in graph.Wires)
        {
            if (!nodes.TryGetValue(w.From, out var from) || !nodes.TryGetValue(w.To, out var to) || !NodeCatalog.TryGet(from.Kind, out var source) || !NodeCatalog.TryGet(to.Kind, out var target)) continue;
            var index = Array.FindIndex(target.Inputs, p => p.Name == w.Input); if (index < 0) continue;
            var points = DiagramGeometry.Route(DiagramGeometry.Output(from), DiagramGeometry.Input(to, index));
            _wires[w.Id] = (points, BuildPath(points), source.Output == target.Inputs[index].Kind ? LabDrawing.WireColor(source.Output) : SKColors.Red);
        }
    }
    private static SKPath BuildPath(SKPoint[] points) { var p = new SKPath(); p.MoveTo(points[0]); foreach (var point in points.Skip(1)) p.LineTo(point); return p; }
    public string? HitWire(Diagram graph, SKPoint point, double tolerance)
    {
        EnsureGeometry(graph);
        foreach (var (id, entry) in _wires) for (var i = 1; i < entry.Points.Length; i++) if (DiagramGeometry.Distance(point, entry.Points[i - 1], entry.Points[i]) <= tolerance) return id;
        return null;
    }
    private void DrawNode(SKCanvas c, Node n, SKRect b, InstrumentSession session)
    {
        if (!NodeCatalog.TryGet(n.Kind, out var def)) { _d.Bevel(c, b, "#FFE1E1"); _d.Text(c, "Unknown function", b.Left + 4, b.MidY); return; }
        var selected = session.Selection.Contains(n.Id); var active = session.ActiveNode == n.Id && (session.Highlight || session.IsPaused);
        if (active) { var glow = b; glow.Inflate(7, 7); _d.Rect(c, glow, LabDrawing.Color("#FFF085")); }
        _d.Text(c, n.Label.Length > 28 ? n.Label[..26] + "…" : n.Label, b.Left, b.Top - 8, 13);
        var fill = def.IsControl || def.IsIndicator ? "#F4F1DD" : n.Kind == "simulate" ? "#D1E7F9" : def.IsStructure ? "#FAFAFA" : "#FFFFDE";
        _d.Bevel(c, b, fill, def.IsIndicator);
        if (def.IsStructure)
        {
            var inner = b; inner.Inflate(-6, -6); _d.Border(c, inner, LabDrawing.Color("#A9A9A9"), 4);
            _d.Rect(c, b.MidX - 31, b.Top - 1, 62, 17, "#EFEFEF"); _d.Text(c, def.Glyph, b.MidX, b.Top + 12, 11, "#555555", true);
            _d.Text(c, "in", b.Left + 12, b.Bottom - 12, 10, "#226AB2"); _d.Text(c, "out", b.Right - 29, b.Bottom - 12, 10, "#226AB2");
            _d.Line(c, b.Left + 30, b.MidY, b.Right - 30, b.MidY, LabDrawing.Color("#DE741B"), 2);
            _d.Bevel(c, new(b.MidX - 17, b.MidY - 15, b.MidX + 17, b.MidY + 15), "#FFFFDA"); _d.Text(c, "+", b.MidX, b.MidY + 6, 21, "#202020", true);
            _d.Text(c, $"{n.Body?.Nodes.Count ?? 0} nodes · double-click", b.MidX, b.Bottom + 16, 10, "#707070", true);
        }
        else if (def.IsControl || def.IsIndicator || n.Kind is "constant" or "bool" or "string")
        {
            var color = LabDrawing.WireColor(def.Output); _d.Rect(c, new(b.Left + 5, b.Top + 5, b.Left + 32, b.Bottom - 5), color); _d.Text(c, def.Output == ValueKind.Waveform ? "~" : def.Output == ValueKind.Boolean ? "TF" : def.Output == ValueKind.String ? "ab" : "DBL", b.Left + 18, b.MidY + 4, 10, "#FFFFFF", true);
            var value = def.IsIndicator ? session.Values.GetValueOrDefault(n.Id)?.ToString() ?? "—" : def.Output == ValueKind.Boolean ? (n.Value != 0 ? "TRUE" : "FALSE") : def.Output == ValueKind.String ? n.Text : n.Value.ToString("G5", System.Globalization.CultureInfo.InvariantCulture);
            _d.Text(c, value.Length > 9 ? value[..8] + "…" : value, b.Left + 39, b.MidY + 4, 11);
        }
        else if (n.Kind == "simulate")
        {
            _d.Rect(c, b.Left + 5, b.Top + 5, b.Width - 10, 13, "#668CA9"); _d.Text(c, "SIMULATED", b.MidX, b.Top + 15, 9, "#FFFFFF", true);
            using var path = new SKPath(); for (var i = 0; i <= 40; i++) { var x = b.Left + 10 + i * (b.Width - 20) / 40; var y = b.MidY + 11 - 14 * (float)Math.Sin(i * Math.PI / 10); if (i == 0) path.MoveTo(x, y); else path.LineTo(x, y); } _d.Path(c, path, LabDrawing.Color("#2671B4"), 2);
        }
        else _d.Text(c, def.Glyph, b.MidX, b.MidY + 7, def.Glyph.Length > 3 ? 16 : 23, "#313131", true);
        for (var i = 0; i < def.Inputs.Length; i++)
        {
            var p = DiagramGeometry.Input(n, i); var port = def.Inputs[i]; var rect = new SKRect(p.X - 4, p.Y - 4, p.X + 4, p.Y + 4);
            _d.Rect(c, rect, SKColors.White); _d.Border(c, rect, LabDrawing.WireColor(port.Kind), 1.7f);
            if (selected) _d.Text(c, port.Name, p.X + 9, p.Y - 5, 9, "#525252");
        }
        if (def.HasOutput) { var p = DiagramGeometry.Output(n); _d.Rect(c, new(p.X - 4, p.Y - 4, p.X + 4, p.Y + 4), LabDrawing.WireColor(def.Output)); }
        if (n.Breakpoint) { _d.Circle(c, b.Left - 11, b.Top - 12, 6, LabDrawing.Color("#B52922")); _d.Circle(c, b.Left - 11, b.Top - 12, 6, SKColors.White, false); }
        if (session.Diagnostics.Any(d => d.NodeId == n.Id)) { _d.Line(c, b.Left, b.Bottom + 4, b.Right, b.Bottom + 4, LabDrawing.Color("#BD2727"), 2); }
        if (selected) _d.Selection(c, b);
    }
    public void Dispose() { foreach (var w in _wires.Values) w.Path.Dispose(); _wires.Clear(); _d.Dispose(); }
}
