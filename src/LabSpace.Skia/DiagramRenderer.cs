using LabSpace.Core;
using LabSpace.Editing;
using SkiaSharp;

namespace LabSpace.Skia;

/// <summary>Classic compact G-style drawing, structural port colors, real structure previews and retained wire geometry.</summary>
public sealed class DiagramRenderer(LabFonts fonts) : IDisposable
{
    private sealed record WireVisual(SKPoint[] Points, SKPath Path, LabType Type, bool Broken, bool Coercion, bool ObstacleFree);
    private readonly LabDrawing _d = new(fonts);
    private readonly Dictionary<string, WireVisual> _wires = [];
    private int _signature;
    private bool _hasSignature;
    public long RoutingPasses { get; private set; }
    public int ObstructedRoutes { get; private set; }

    public void Draw(SKCanvas c, InstrumentSession session, SKRect viewport, string? wiringFrom = null,
        SKPoint? pointer = null, SKRect? marquee = null, string wiringOutput = "value", IReadOnlyList<PointD>? waypoints = null)
    {
        c.Clear(SKColors.White); var graph = session.Diagram; EnsureGeometry(graph, session.IsGestureActive);
        foreach (var wire in graph.Wires)
        {
            if (!_wires.TryGetValue(wire.Id, out var geometry)) continue;
            var bounds = geometry.Path.Bounds; bounds.Inflate(8, 8);
            if (!bounds.IntersectsWith(viewport)) continue;
            var thickness = geometry.Type.Kind is ValueKind.Array or ValueKind.Waveform ? 3 : 1.8f;
            if (session.SelectedWire == wire.Id) _d.Path(c, geometry.Path, LabDrawing.Color("#9BC5EE"), thickness + 5);
            if (geometry.Broken)
            {
                _d.DashedPath(c, geometry.Path, SKColors.Black, 1.5f);
                var p = geometry.Points[geometry.Points.Length / 2];
                _d.Text(c, "×", p.X, p.Y + 4, 18, "#C42121", true);
            }
            else
            {
                _d.Path(c, geometry.Path, LabDrawing.WireColor(geometry.Type), thickness);
                if (geometry.Type.Kind == ValueKind.Error) _d.DashedPath(c, geometry.Path, LabDrawing.Color("#DFC444"), 1);
                if (geometry.Coercion) { var p = geometry.Points[^1]; _d.Circle(c, p.X - 4, p.Y, 2.5f, LabDrawing.Color("#B72A2A")); }
            }
            if (wire.Probe)
            {
                var p = geometry.Points[Math.Min(2, geometry.Points.Length - 1)]; var value = session.WireValue(wire)?.ToString() ?? "not executed";
                var box = new SKRect(p.X + 7, p.Y - 27, p.X + 190, p.Y - 4); _d.Bevel(c, box, "#FFFFDA");
                _d.Text(c, Trim(value, 31), box.Left + 5, box.Top + 15, 11);
            }
        }
        foreach (var group in graph.Wires.GroupBy(w => new PortAddress(w.From, w.Output)).Where(g => g.Count() > 1))
            if (graph.Nodes.FirstOrDefault(n => n.Id == group.Key.NodeId) is { } node && NodeCatalog.Describe(node).FindOutput(group.Key.Port) is { } branchPort)
            { var p = DiagramGeometry.Output(node, group.Key.Port); _d.Circle(c, p.X + 16, p.Y, 3, LabDrawing.WireColor(branchPort.DataType)); }
        foreach (var n in graph.Nodes)
            if (DiagramGeometry.VisualBounds(n).IntersectsWith(viewport)) DrawNode(c, n, session, false, 0);
        if (wiringFrom is not null && pointer.HasValue && graph.Nodes.FirstOrDefault(n => n.Id == wiringFrom) is { } source)
        {
            var points = ManualRoute(DiagramGeometry.Output(source, wiringOutput), pointer.Value, waypoints ?? []);
            using var path = BuildPath(points); _d.Path(c, path, LabDrawing.WireColor(NodeCatalog.Describe(source).FindOutput(wiringOutput)!.DataType), 2);
            _d.Circle(c, pointer.Value.X, pointer.Value.Y, 4, LabDrawing.Color("#277BC0"), false);
        }
        if (marquee.HasValue) { _d.Rect(c, marquee.Value, new SKColor(50, 120, 210, 22)); _d.Border(c, marquee.Value, LabDrawing.Color("#3175C4")); }
        if (graph.Nodes.Count == 0)
        { _d.Text(c, "Block Diagram", viewport.MidX, viewport.MidY - 15, 22, "#777777", true); _d.Text(c, "Right-click for Functions · Ctrl+Space for Quick Drop", viewport.MidX, viewport.MidY + 13, 13, "#888888", true); }
    }

    private void EnsureGeometry(Diagram graph, bool preview = false)
    {
        var hash = new HashCode(); hash.Add(graph); hash.Add(preview);
        foreach (var n in graph.Nodes) { hash.Add(n.Id); hash.Add(n.X); hash.Add(n.Y); hash.Add(n.Kind); hash.Add(n.Type); hash.Add(n.Contract); }
        foreach (var w in graph.Wires) { hash.Add(w.Id); hash.Add(w.From); hash.Add(w.To); hash.Add(w.Input); hash.Add(w.Output); foreach (var p in w.Waypoints) hash.Add(p); }
        var signature = hash.ToHashCode(); if (_hasSignature && signature == _signature) return;
        _signature = signature; _hasSignature = true; RoutingPasses++; ObstructedRoutes = 0;
        foreach (var wire in _wires.Values) wire.Path.Dispose(); _wires.Clear();
        var nodes = graph.Nodes.ToDictionary(n => n.Id);
        foreach (var w in graph.Wires)
        {
            if (!nodes.TryGetValue(w.From, out var from) || !nodes.TryGetValue(w.To, out var to)) continue;
            var source = NodeCatalog.Describe(from); var target = NodeCatalog.Describe(to);
            var output = source.FindOutput(w.Output); var index = System.Array.FindIndex(target.Inputs, p => p.Name == w.Input);
            if (output is null || index < 0) continue;
            var a = DiagramGeometry.Output(from, w.Output); var b = DiagramGeometry.Input(to, index);
            SKPoint[] points; var clear = true;
            if (w.Waypoints.Count > 0) points = ManualRoute(a, b, w.Waypoints);
            else if (preview) points = DiagramGeometry.Route(a, b);
            else
            {
                var obstacles = graph.Nodes.Where(n => n.Id != from.Id && n.Id != to.Id).Select(n => { var r = DiagramGeometry.Bounds(n); return new RectD(r.Left, r.Top, r.Width, r.Height); }).ToArray();
                var route = OrthogonalRouter.Route(new(a.X, a.Y), new(b.X, b.Y), obstacles);
                points = route.Points.Select(p => new SKPoint((float)p.X, (float)p.Y)).ToArray(); clear = route.ObstacleFree;
            }
            if (!clear) ObstructedRoutes++;
            _wires[w.Id] = new(points, BuildPath(points), output.DataType, !ValueConversion.CanConvert(output.DataType, target.Inputs[index].DataType), !output.DataType.Equals(target.Inputs[index].DataType), clear);
        }
    }
    private static SKPoint[] ManualRoute(SKPoint from, SKPoint to, IReadOnlyList<PointD> waypoints)
    {
        var points = new List<PointD> { new(from.X, from.Y) }; var previous = from;
        foreach (var p in waypoints.Select(p => new SKPoint((float)p.X, (float)p.Y)).Append(to))
        {
            // Persisted manual vertices retain their bends; only diagonal gaps gain an orthogonal elbow.
            if (previous.X != p.X && previous.Y != p.Y) points.Add(new(p.X, previous.Y));
            points.Add(new(p.X, p.Y)); previous = p;
        }
        return OrthogonalRouter.Simplify(points).Select(p => new SKPoint((float)p.X, (float)p.Y)).ToArray();
    }
    private static SKPath BuildPath(IReadOnlyList<SKPoint> points)
    { var p = new SKPath(); if (points.Count == 0) return p; p.MoveTo(points[0]); for (var i = 1; i < points.Count; i++) p.LineTo(points[i]); return p; }
    public IReadOnlyList<SKPoint> WirePoints(Diagram graph, string id) { EnsureGeometry(graph); return _wires.TryGetValue(id, out var v) ? v.Points : []; }
    public string? HitWire(Diagram graph, SKPoint point, double tolerance)
    {
        EnsureGeometry(graph);
        foreach (var (id, entry) in _wires) for (var i = 1; i < entry.Points.Length; i++) if (DiagramGeometry.Distance(point, entry.Points[i - 1], entry.Points[i]) <= tolerance) return id;
        return null;
    }
    private void DrawNode(SKCanvas c, Node n, InstrumentSession session, bool miniature, int depth)
    {
        var b = DiagramGeometry.Bounds(n); var def = NodeCatalog.Describe(n); var type = def.DataType;
        var selected = !miniature && session.Selection.Contains(n.Id);
        if (!miniature && session.ActiveNode == n.Id && (session.Highlight || session.IsPaused)) { var glow = b; glow.Inflate(6, 6); _d.Rect(c, glow, LabDrawing.Color("#FFF085")); }
        if (!miniature)
        {
            if (def.IsControl) _d.TextRight(c, Trim(n.Label, 38), b.Left - 9, b.MidY + 4, 13);
            else if (def.IsIndicator) _d.Text(c, Trim(n.Label, 38), b.Right + 9, b.MidY + 4, 13);
            else _d.Text(c, Trim(n.Label, 36), b.Left, b.Top - 7, 12);
        }
        if (def.IsStructure) DrawStructure(c, n, b, session, miniature, depth);
        else if (def.IsControl || def.IsIndicator)
        {
            var color = LabDrawing.WireColor(type); _d.Rect(c, b, color); var inner = b; inner.Inflate(-3, -3); _d.Rect(c, inner, SKColors.White);
            if (def.IsIndicator) { inner.Inflate(-2, -2); _d.Border(c, inner, color); }
            _d.Text(c, LabDrawing.TypeGlyph(type), b.MidX, b.MidY + 4, 10, color.ToString(), true);
        }
        else if (DiagramGeometry.IsConstant(n))
        {
            _d.Rect(c, b, SKColors.White); _d.Border(c, b, LabDrawing.WireColor(type));
            string text;
            try { text = session.DisplayValue(n).ToString(); } catch { text = "invalid"; }
            _d.Text(c, Trim(text, 10), b.Left + 4, b.MidY + 4, 11, LabDrawing.WireColor(type).ToString());
        }
        else if (DiagramGeometry.IsArithmetic(n))
        {
            using var triangle = new SKPath(); triangle.MoveTo(b.Left, b.Top); triangle.LineTo(b.Right, b.MidY); triangle.LineTo(b.Left, b.Bottom); triangle.Close();
            _d.Path(c, triangle, LabDrawing.Color("#FFFDC6"), fill: true); _d.Path(c, triangle, SKColors.Black);
            _d.Text(c, def.Glyph, b.Left + b.Width * .38f, b.MidY + 6, def.Glyph.Length > 1 ? 10 : 19, "#222222", true);
        }
        else if (n.Kind == "simulate")
        {
            _d.Bevel(c, b, "#D6E9FA"); _d.Rect(c, b.Left + 2, b.Top + 2, b.Width - 4, 10, "#6893B6");
            using var path = new SKPath(); for (var i = 0; i <= 36; i++) { var x = b.Left + 7 + i * (b.Width - 14) / 36; var y = b.MidY + 5 - 12 * (float)Math.Sin(i * Math.PI / 9); if (i == 0) path.MoveTo(x, y); else path.LineTo(x, y); } _d.Path(c, path, LabDrawing.Color("#2160AC"), 1.7f);
        }
        else if (n.Kind is "bundle" or "unbundle" or "error-fields")
        {
            _d.Bevel(c, b, "#F2D5A9"); var fields = n.Kind == "bundle" ? def.Inputs : def.OutputPorts;
            for (var i = 0; i < fields.Length; i++) { var y = b.Top + b.Height * (i + 1) / (fields.Length + 1); _d.Text(c, Trim(fields[i].Name, 15), b.Left + 9, y + 4, 10); if (i > 0) _d.Line(c, b.Left + 2, y - 10, b.Right - 2, y - 10, LabDrawing.Color("#C1A176")); }
        }
        else if (n.Kind is "tunnel-in" or "tunnel-out" or "shift-read" or "shift-write" or "iteration" or "loop-count")
        {
            _d.Bevel(c, b, "#F6F6F6"); _d.Border(c, b, LabDrawing.WireColor(type), 2);
            _d.Text(c, def.Glyph, b.MidX, b.MidY + 5, 15, LabDrawing.WireColor(type).ToString(), true);
        }
        else { _d.Bevel(c, b, "#FFFFD5"); _d.Text(c, def.Glyph, b.MidX, b.MidY + 5, def.Glyph.Length > 2 ? 10 : 17, "#202020", true); }
        DrawPorts(c, n, def, selected);
        if (!miniature && n.Breakpoint) { _d.Circle(c, b.Left - 7, b.Top - 9, 5, LabDrawing.Color("#B52922")); }
        if (!miniature && session.Diagnostics.Any(d => d.NodeId == n.Id || d.StructureId == n.Id)) _d.Text(c, "×", b.Right + 8, b.Top + 8, 20, "#BD2727");
        if (selected) _d.Selection(c, b);
    }
    private void DrawPorts(SKCanvas c, Node n, NodeDefinition def, bool labels)
    {
        for (var i = 0; i < def.Inputs.Length; i++) Port(DiagramGeometry.Input(n, i), def.Inputs[i], false);
        for (var i = 0; i < def.OutputPorts.Length; i++) Port(DiagramGeometry.Output(n, i), def.OutputPorts[i], true);
        void Port(SKPoint p, PortDefinition port, bool output)
        {
            var color = LabDrawing.WireColor(port.DataType); var b = new SKRect(p.X - 3, p.Y - 3, p.X + 3, p.Y + 3);
            _d.Rect(c, b, output ? color : SKColors.White); _d.Border(c, b, color);
            if (def.IsStructure && port.Name.StartsWith(output ? "shift:" : "init:", StringComparison.Ordinal)) _d.Text(c, output ? "↑" : "↓", p.X, p.Y + 4, 12, color.ToString(), true);
            else if (def.IsStructure && n.Contract is { } contract && (output ? contract.Outputs : contract.Inputs).Any(t => t.Name == port.Name && t.Mode == TunnelMode.Indexing))
            { _d.Rect(c, new(p.X - 5, p.Y - 5, p.X + 5, p.Y + 5), SKColors.White); _d.Border(c, new(p.X - 5, p.Y - 5, p.X + 5, p.Y + 5), color); _d.Text(c, "[ ]", p.X, p.Y + 3, 7, color.ToString(), true); }
            if (labels && def.IsStructure) { if (output) _d.TextRight(c, port.Name, p.X - 9, p.Y - 6, 9); else _d.Text(c, port.Name, p.X + 9, p.Y - 6, 9); }
        }
    }
    private void DrawStructure(SKCanvas c, Node n, SKRect b, InstrumentSession session, bool miniature, int depth)
    {
        _d.Bevel(c, b, "#C6C6C6"); var inside = new SKRect(b.Left + 6, b.Top + 6, b.Right - 6, b.Bottom - 6); _d.Rect(c, inside, SKColors.White); _d.Border(c, inside, LabDrawing.Color("#999999"));
        var title = n.Kind is "case" or "case-typed" ? (n.PreviewAlternative ? "◀  False  ▶" : "◀  True  ▶") : NodeCatalog.Get(n.Kind).Glyph;
        var header = new SKRect(b.MidX - 46, b.Top - 2, b.MidX + 46, b.Top + 15); _d.Bevel(c, header, "#E9E9E9"); _d.Text(c, title, b.MidX, b.Top + 10, 10, "#202020", true);
        if (n.Kind is "for" or "for-loop") { _d.Text(c, "N", b.Left + 13, b.Top + 21, 12, "#174BB5"); _d.Text(c, "i", b.Left + 13, b.Bottom - 12, 12, "#174BB5"); }
        if (n.Kind is "while" or "while-loop") { _d.Text(c, "i", b.Left + 13, b.Bottom - 12, 12, "#174BB5"); _d.Circle(c, b.Right - 16, b.Bottom - 16, 5, LabDrawing.Color("#B12D27")); }
        var body = n.PreviewAlternative ? n.Alternative : n.Body;
        if (body is not null && body.Nodes.Count > 0 && depth < 2)
        {
            var visible = body.Nodes.Take(80).ToArray(); var boxes = visible.Select(DiagramGeometry.Bounds).ToArray();
            var source = new SKRect(boxes.Min(r => r.Left), boxes.Min(r => r.Top), boxes.Max(r => r.Right), boxes.Max(r => r.Bottom));
            var target = new SKRect(b.Left + 26, b.Top + 30, b.Right - 26, b.Bottom - 29);
            var scale = Math.Min(target.Width / Math.Max(1, source.Width), target.Height / Math.Max(1, source.Height));
            c.Save(); c.ClipRect(target); c.Translate(target.MidX - source.MidX * scale, target.MidY - source.MidY * scale); c.Scale(scale);
            var map = visible.ToDictionary(x => x.Id);
            foreach (var wire in body.Wires)
            {
                if (!map.TryGetValue(wire.From, out var from) || !map.TryGetValue(wire.To, out var to)) continue;
                var output = NodeCatalog.Describe(from).FindOutput(wire.Output); var index = System.Array.FindIndex(NodeCatalog.Describe(to).Inputs, p => p.Name == wire.Input);
                if (output is null || index < 0) continue;
                using var path = BuildPath(DiagramGeometry.Route(DiagramGeometry.Output(from, wire.Output), DiagramGeometry.Input(to, index)));
                _d.Path(c, path, LabDrawing.WireColor(output.DataType), 2);
            }
            foreach (var node in visible) DrawNode(c, node, session, true, depth + 1);
            c.Restore();
        }
        if (!miniature) _d.Text(c, "Double-click to edit body", b.MidX, b.Bottom + 15, 10, "#777777", true);
    }
    private static string Trim(string value, int maximum) => value.Length > maximum ? value[..(maximum - 1)] + "…" : value;
    public void Dispose() { foreach (var wire in _wires.Values) wire.Path.Dispose(); _wires.Clear(); _d.Dispose(); }
}
