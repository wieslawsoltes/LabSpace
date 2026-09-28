using LabSpace.Core;
using LabSpace.Editing;
using SkiaSharp;

namespace LabSpace.Skia;

public sealed class DiagramRenderer(LabFonts fonts) : IDisposable
{
    private readonly LabDrawing _d = new(fonts);
    private readonly Dictionary<string, (SKPoint[] Points, SKPath Path, SKColor Color, float Width)> _wires = [];
    private int _signature;
    private readonly Dictionary<string, SKPicture> _previews = [];
    private long _previewRevision = -1;
    private Diagram? _previewDiagram;
    public void Draw(SKCanvas c, InstrumentSession session, SKRect viewport, string? wiringFrom = null, SKPoint? pointer = null, SKRect? marquee = null, string wiringOutput = "result")
    {
        c.Clear(SKColors.White); var graph = session.Diagram; EnsureGeometry(graph);
        if (_previewRevision != session.Revision || !ReferenceEquals(_previewDiagram, graph))
        {
            foreach (var picture in _previews.Values) picture.Dispose(); _previews.Clear();
            _previewRevision = session.Revision; _previewDiagram = graph;
        }
        // Visible-area grid only; work outside the viewport is never rasterized.
        var startX = Math.Floor(viewport.Left / 20) * 20; var startY = Math.Floor(viewport.Top / 20) * 20;
        for (var x = startX; x < viewport.Right; x += 20) for (var y = startY; y < viewport.Bottom; y += 20) _d.Rect(c, (float)x, (float)y, 1, 1, "#EDEDED");
        foreach (var wire in graph.Wires)
        {
            if (!_wires.TryGetValue(wire.Id, out var geometry) || !geometry.Path.Bounds.IntersectsWith(viewport)) continue;
            if (session.SelectedWire == wire.Id) _d.Path(c, geometry.Path, LabDrawing.Color("#92BDEC"), 6);
            _d.Path(c, geometry.Path, geometry.Color, geometry.Width);
            var points = geometry.Points;
            if (wire.Probe)
            {
                var p = points[Math.Min(2, points.Length - 1)]; var value = session.OutputValue(wire.From, wire.Output)?.ToString() ?? "not executed";
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
            if (n is not null) { var points = DiagramGeometry.Route(DiagramGeometry.Output(n, wiringOutput), pointer.Value); using var path = BuildPath(points); _d.Path(c, path, LabDrawing.WireColor(NodeCatalog.Resolve(n).FindOutput(wiringOutput)?.Kind ?? ValueKind.Number), 2); _d.Circle(c, pointer.Value.X, pointer.Value.Y, 4, LabDrawing.Color("#277BC0"), false); }
        }
        if (marquee.HasValue) { _d.Rect(c, marquee.Value, new SKColor(50, 120, 210, 22)); _d.Border(c, marquee.Value, LabDrawing.Color("#3175C4")); }
        if (graph.Nodes.Count == 0) { _d.Text(c, "Build your first virtual instrument", viewport.MidX, viewport.MidY - 16, 23, "#747474", true); _d.Text(c, "Choose a function from the palette, then connect its terminals.", viewport.MidX, viewport.MidY + 15, 14, "#858585", true); }
    }
    private void EnsureGeometry(Diagram graph)
    {
        var hash = new HashCode(); hash.Add(graph);
        foreach (var n in graph.Nodes) { hash.Add(n.Id); hash.Add(n.X); hash.Add(n.Y); hash.Add(n.Kind); hash.Add(n.Contract); hash.Add(n.DataType); }
        foreach (var w in graph.Wires) { hash.Add(w.Id); hash.Add(w.From); hash.Add(w.To); hash.Add(w.Input); hash.Add(w.Output); }
        var signature = hash.ToHashCode(); if (signature == _signature) return; _signature = signature;
        foreach (var wire in _wires.Values) wire.Path.Dispose(); _wires.Clear();
        var nodes = graph.Nodes.ToDictionary(n => n.Id);
        foreach (var w in graph.Wires)
        {
            if (!nodes.TryGetValue(w.From, out var from) || !nodes.TryGetValue(w.To, out var to) || !NodeCatalog.TryGet(from.Kind, out var source) || !NodeCatalog.TryGet(to.Kind, out var target)) continue;
            source = NodeCatalog.Resolve(from); target = NodeCatalog.Resolve(to);
            var output = source.FindOutput(w.Output); if (output is null) continue;
            var index = Array.FindIndex(target.Inputs, p => p.Name == w.Input); if (index < 0) continue;
            var points = DiagramGeometry.Route(DiagramGeometry.Output(from, w.Output), DiagramGeometry.Input(to, index));
            _wires[w.Id] = (points, BuildPath(points), output.Kind == target.Inputs[index].Kind ? LabDrawing.WireColor(output.Kind) : SKColors.Red, output.Kind is ValueKind.Array or ValueKind.Waveform ? 3.5f : 1.5f);
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
        def = NodeCatalog.Resolve(n);
        var selected = session.Selection.Contains(n.Id); var active = session.ActiveNode == n.Id && (session.Highlight || session.IsPaused);
        if (active) { var glow = b; glow.Inflate(7, 7); _d.Rect(c, glow, LabDrawing.Color("#FFF085")); }
        var label = n.Label.Length > 28 ? n.Label[..26] + "…" : n.Label;
        var labelX = def.IsControl ? b.Left - 8 - _d.Font(13).MeasureText(label) : def.IsIndicator ? b.Right + 8 : b.Left;
        _d.Text(c, label, labelX, def.IsControl || def.IsIndicator ? b.MidY + 4 : b.Top - 8, 13);
        var fill = def.IsControl || def.IsIndicator ? "#F4F1DD" : n.Kind == "simulate" ? "#D1E7F9" : def.IsStructure ? "#FAFAFA" : "#FFFFDE";
        _d.Bevel(c, b, fill, def.IsIndicator);
        if (def.IsStructure)
        {
            var inner = b; inner.Inflate(-6, -6); _d.Border(c, inner, LabDrawing.Color("#A9A9A9"), 4);
            _d.Rect(c, b.MidX - 31, b.Top - 1, 62, 17, "#EFEFEF"); _d.Text(c, def.Glyph, b.MidX, b.Top + 12, 11, "#555555", true);
            DrawPreview(c, n, new(b.Left + 28, b.Top + 23, b.Right - 28, b.Bottom - 23));
            _d.Text(c, n.Kind is "for" or "while" ? "i" : "VI", b.Left + 12, b.Bottom - 10, 11, "#226AB2");
            if (n.Kind == "while" || n.Contract?.ConditionalFor == true) _d.Circle(c, b.Right - 15, b.Bottom - 14, 5, LabDrawing.Color("#B63028"));
            _d.Text(c, $"{n.Body?.Nodes.Count ?? 0} nodes · double-click to edit", b.MidX, b.Bottom + 16, 10, "#707070", true);
        }
        else if (def.IsControl || def.IsIndicator)
        {
            var inside = b; inside.Inflate(-4, -4); _d.Border(c, inside, LabDrawing.WireColor(def.Output), def.IsControl ? 2 : 1);
            _d.Text(c, def.Output == ValueKind.Waveform ? "~" : def.Output == ValueKind.Boolean ? "TF" : def.Output == ValueKind.String ? "abc" : def.Output == ValueKind.Array ? "[ ]" : "DBL", b.MidX, b.MidY + 4, 11, LabDrawing.WireColor(def.Output).ToString(), true);
        }
        else if (n.Kind is "constant" or "bool" or "string")
        {
            var value = n.Kind == "bool" ? (n.Value != 0 ? "TRUE" : "FALSE") : n.Kind == "string" ? n.Text : n.Value.ToString("G5", System.Globalization.CultureInfo.InvariantCulture);
            _d.Text(c, value.Length > 10 ? value[..9] + "…" : value, b.Left + 5, b.MidY + 4, 12);
            _d.Border(c, b, LabDrawing.WireColor(def.Output));
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
            if (port.Name.StartsWith("initial:", StringComparison.Ordinal)) DrawRegister(c, p, port.Kind);
            else if (n.Contract?.Inputs.Any(t => t.Name == port.Name && t.Indexing) == true) _d.Line(c, p.X - 2, p.Y, p.X + 2, p.Y, LabDrawing.WireColor(port.Kind), 2);
            if (selected || def.IsStructure) _d.Text(c, port.Name, p.X + 9, p.Y - 5, 9, "#525252");
        }
        for (var i = 0; i < def.Outputs.Length; i++)
        {
            var p = DiagramGeometry.Output(n, i); var output = def.Outputs[i];
            _d.Rect(c, new(p.X - 4, p.Y - 4, p.X + 4, p.Y + 4), LabDrawing.WireColor(output.Kind));
            if (n.Contract?.Registers.Any(r => r.Name == output.Name) == true) DrawRegister(c, p, output.Kind);
            if (def.IsStructure) _d.Text(c, output.Name, p.X - 9 - _d.Font(9).MeasureText(output.Name), p.Y - 6, 9, "#525252");
        }
        if (n.Breakpoint) { _d.Circle(c, b.Left - 11, b.Top - 12, 6, LabDrawing.Color("#B52922")); _d.Circle(c, b.Left - 11, b.Top - 12, 6, SKColors.White, false); }
        if (session.Diagnostics.Any(d => d.NodeId == n.Id)) { _d.Line(c, b.Left, b.Bottom + 4, b.Right, b.Bottom + 4, LabDrawing.Color("#BD2727"), 2); }
        if (selected) _d.Selection(c, b);
    }
    private void DrawRegister(SKCanvas canvas, SKPoint point, ValueKind kind)
    {
        using var triangle = new SKPath(); triangle.MoveTo(point.X - 5, point.Y + 5); triangle.LineTo(point.X, point.Y - 5); triangle.LineTo(point.X + 5, point.Y + 5); triangle.Close();
        _d.Path(canvas, triangle, SKColors.White, fill: true); _d.Path(canvas, triangle, LabDrawing.WireColor(kind), 1.5f);
    }
    private void DrawPreview(SKCanvas canvas, Node owner, SKRect area)
    {
        if (owner.Body is not { Nodes.Count: > 0 } body) return;
        if (!_previews.TryGetValue(owner.Id, out var picture))
        {
            using var recorder = new SKPictureRecorder();
            var target = recorder.BeginRecording(new(0, 0, area.Width, area.Height));
            var nodes = body.Nodes.Take(256).ToDictionary(n => n.Id);
            var left = nodes.Values.Min(n => n.X) - 10; var top = nodes.Values.Min(n => n.Y) - 24;
            var right = nodes.Values.Max(n => DiagramGeometry.Bounds(n).Right) + 10; var bottom = nodes.Values.Max(n => DiagramGeometry.Bounds(n).Bottom) + 10;
            var scale = (float)Math.Min(area.Width / Math.Max(1, right - left), area.Height / Math.Max(1, bottom - top));
            target.Translate((area.Width - (float)(right - left) * scale) / 2, (area.Height - (float)(bottom - top) * scale) / 2);
            target.Scale(scale); target.Translate(-(float)left, -(float)top);
            foreach (var wire in body.Wires.Take(1024))
            {
                if (!nodes.TryGetValue(wire.From, out var from) || !nodes.TryGetValue(wire.To, out var to)) continue;
                var source = NodeCatalog.Resolve(from).FindOutput(wire.Output); var index = Array.FindIndex(NodeCatalog.Resolve(to).Inputs, p => p.Name == wire.Input);
                if (source is null || index < 0) continue;
                using var path = BuildPath(DiagramGeometry.Route(DiagramGeometry.Output(from, wire.Output), DiagramGeometry.Input(to, index)));
                _d.Path(target, path, LabDrawing.WireColor(source.Kind), source.Kind == ValueKind.Array ? 4 : 2);
            }
            foreach (var node in nodes.Values)
            {
                var b = DiagramGeometry.Bounds(node); var d = NodeCatalog.Resolve(node); _d.Bevel(target, b, d.IsStructure ? "#E5E5E5" : "#FFFFDF");
                _d.Text(target, node.Label, b.Left, b.Top - 6, 13);
                var text = node.Kind == "constant" ? node.Value.ToString("G4", System.Globalization.CultureInfo.InvariantCulture) : d.Glyph;
                _d.Text(target, text, b.MidX, b.MidY + 7, 19, "#333333", true);
                for (var i = 0; i < d.Inputs.Length; i++) { var p = DiagramGeometry.Input(node, i); _d.Rect(target, new(p.X - 3, p.Y - 3, p.X + 3, p.Y + 3), LabDrawing.WireColor(d.Inputs[i].Kind)); }
                for (var i = 0; i < d.Outputs.Length; i++) { var p = DiagramGeometry.Output(node, i); _d.Rect(target, new(p.X - 3, p.Y - 3, p.X + 3, p.Y + 3), LabDrawing.WireColor(d.Outputs[i].Kind)); }
            }
            picture = recorder.EndRecording(); _previews[owner.Id] = picture;
        }
        canvas.Save(); canvas.ClipRect(area); canvas.Translate(area.Left, area.Top); canvas.DrawPicture(picture); canvas.Restore();
    }
    public void DrawPlacement(SKCanvas canvas, Node node)
    {
        var b = DiagramGeometry.Bounds(node); _d.Rect(canvas, b, new SKColor(80, 135, 205, 25)); _d.Border(canvas, b, LabDrawing.Color("#3977B4"));
        _d.Text(canvas, NodeCatalog.Get(node.Kind).Title, b.Left, b.Top - 7, 12, "#3977B4");
    }
    public void Dispose() { foreach (var picture in _previews.Values) picture.Dispose(); _previews.Clear(); foreach (var w in _wires.Values) w.Path.Dispose(); _wires.Clear(); _d.Dispose(); }
}
