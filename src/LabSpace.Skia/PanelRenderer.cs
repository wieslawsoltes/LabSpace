using System.Globalization;
using LabSpace.Core;
using LabSpace.Editing;
using SkiaSharp;

namespace LabSpace.Skia;

public sealed class PanelRenderer : IDisposable
{
    private readonly LabDrawing _d;
    private readonly PlotRenderer _plots;
    private readonly Dictionary<string, (long Frame, double[] Samples)> _history = [];
    public Dictionary<string, double> Cursors { get; } = [];
    public PanelRenderer(LabFonts fonts) { _d = new(fonts); _plots = new(_d); }
    public void Draw(SKCanvas c, InstrumentSession session, SKRect viewport)
    {
        c.Clear(LabDrawing.Color("#C6C6C6"));
        for (var x = Math.Floor(viewport.Left / 10) * 10; x < viewport.Right; x += 10) _d.Line(c, (float)x, viewport.Top, (float)x, viewport.Bottom, LabDrawing.Color("#BDBDBD"));
        for (var y = Math.Floor(viewport.Top / 10) * 10; y < viewport.Bottom; y += 10) _d.Line(c, viewport.Left, (float)y, viewport.Right, (float)y, LabDrawing.Color("#BDBDBD"));
        _d.Text(c, session.Instrument.Name.Replace(".vi", "").ToUpperInvariant(), 30, 25, 17, "#414141");
        _d.Text(c, "SIMULATED INSTRUMENT  /  " + (session.PanelEditMode ? "EDIT CONTROLS" : "OPERATE CONTROLS"), 620, 25, 10, "#676767");
        var nodes = session.Instrument.Diagram.Nodes.ToDictionary(n => n.Id);
        foreach (var item in session.Instrument.Panel)
        {
            if (!nodes.TryGetValue(item.NodeId, out var node)) continue;
            var r = Rect(item.Bounds); if (!r.IntersectsWith(viewport)) continue;
            var def = NodeCatalog.Describe(node); var output = session.DisplayValue(node);
            var value = output.Number; var boolean = output.Boolean; var text = output.Text;
            _d.Text(c, node.Label, r.Left + 2, r.Top + 13, 13);
            var body = new SKRect(r.Left, r.Top + 22, r.Right, r.Bottom);
            switch (item.Widget)
            {
                case "Graph": case "Chart":
                    double[]? history = null;
                    if (node.Kind == "chart" && session.ChartHistory.TryGetValue(node.Id, out var queue))
                    {
                        if (!_history.TryGetValue(node.Id, out var cached) || cached.Frame != session.Frames) _history[node.Id] = cached = (session.Frames, queue.ToArray());
                        history = cached.Samples;
                    }
                    var source = session.Instrument.Diagram.Wires.FirstOrDefault(w => w.To == node.Id)?.From;
                    var spectrum = source is not null && nodes.TryGetValue(source, out var sourceNode) && sourceNode.Kind == "fft";
                    _plots.Draw(c, body, output, history, spectrum, Cursors.TryGetValue(item.Id, out var cursor) ? cursor : null); break;
                case "Knob": DrawKnob(c, body, item, value); break;
                case "Meter": case "Gauge": DrawGauge(c, body, item, value); break;
                case "Slider": DrawSlider(c, body, item, value); break;
                case "Thermometer": case "Tank": DrawVertical(c, body, item, value, item.Widget == "Tank"); break;
                case "Cluster": case "Error": DrawStructured(c, body, output); break;
                case "Enum":
                    _d.Bevel(c, body, "#FFFFFF", true); _d.Text(c, output.ToString(), body.Left + 7, body.Top + 23, 14); _d.Text(c, "▾", body.Right - 12, body.Top + 23, 12, center: true); break;
                case "LED":
                    var radius = Math.Min(19, body.Height / 3); _d.Circle(c, body.MidX, body.MidY, radius + 3, LabDrawing.Color("#686868")); _d.Circle(c, body.MidX, body.MidY, radius, LabDrawing.Color(boolean ? "#5DD14A" : "#304C2C")); _d.Circle(c, body.MidX - radius / 3, body.MidY - radius / 3, radius / 4, LabDrawing.Color(boolean ? "#B7F29E" : "#597454")); break;
                case "Switch":
                    var sw = new SKRect(body.Left + 8, body.Top + 5, body.Right - 8, Math.Min(body.Bottom, body.Top + 37)); _d.Bevel(c, sw, boolean ? "#81BD73" : "#B3B3B3", boolean); _d.Text(c, boolean ? "ON" : "OFF", sw.MidX, sw.MidY + 5, 13, "#202020", true); break;
                case "String":
                    _d.Bevel(c, body, "#FFFFFF", true); c.Save(); c.ClipRect(body); _d.Text(c, text, body.Left + 7, body.Top + 22, 14); c.Restore(); break;
                case "Array":
                    _d.Bevel(c, body, "#FFFFFF", true); c.Save(); c.ClipRect(body);
                    _d.Text(c, TypeSyntax.Format(output.Type) + "  " + string.Join(" × ", output.Shape), body.Left + 5, body.Top + 15, 10, "#777777");
                    for (var i = 0; i < Math.Min(output.Count, Math.Max(0, (int)(body.Height - 22) / 19)); i++)
                    { _d.Text(c, $"[{i}]", body.Left + 5, body.Top + 34 + i * 19, 11, "#888888"); _d.Text(c, output.ElementAt(i).ToString(), body.Left + 45, body.Top + 34 + i * 19, 12); }
                    c.Restore(); break;
                default:
                    var field = new SKRect(body.Left + (def.IsControl ? 15 : 0), body.Top + 6, body.Right, Math.Min(body.Bottom, body.Top + 45)); _d.Bevel(c, field, def.IsControl ? "#FFFFFF" : "#D9D9D9", true); c.Save(); c.ClipRect(field); _d.Text(c, output.ToString(), field.Left + 7, field.Top + 26, output.ToString().Length > 14 ? 14 : 21); c.Restore();
                    if (def.IsControl) { _d.Bevel(c, new(body.Left, field.Top, body.Left + 14, field.MidY)); _d.Text(c, "+", body.Left + 7, field.Top + 14, 11, center: true); _d.Bevel(c, new(body.Left, field.MidY, body.Left + 14, field.Bottom)); _d.Text(c, "−", body.Left + 7, field.Bottom - 5, 11, center: true); }
                    break;
            }
            if (session.Selection.Contains(node.Id)) _d.Selection(c, r);
        }
        if (session.Instrument.Panel.Count == 0) { _d.Text(c, "Front Panel", viewport.MidX, viewport.MidY - 14, 26, "#767676", true); _d.Text(c, "Add a control or indicator from the Controls palette.", viewport.MidX, viewport.MidY + 17, 14, "#6A6A6A", true); }
    }
    public static SKRect Rect(RectD b) => new((float)b.X, (float)b.Y, (float)b.Right, (float)b.Bottom);
    private static double Fraction(PanelItem item, double value) => Math.Clamp((value - item.Minimum) / (item.Maximum - item.Minimum), 0, 1);
    private void DrawKnob(SKCanvas c, SKRect b, PanelItem item, double value)
    {
        var cx = b.MidX; var cy = b.Top + Math.Min(b.Height - 36, b.Width) / 2 + 7; var radius = Math.Min(b.Width, b.Height - 27) * .31f;
        for (var i = 0; i <= 10; i++)
        {
            var angle = (135 + 270.0 * i / 10) * Math.PI / 180; var x = (float)Math.Cos(angle); var y = (float)Math.Sin(angle);
            _d.Line(c, cx + x * (radius + 8), cy + y * (radius + 8), cx + x * (radius + 14), cy + y * (radius + 14), LabDrawing.Color("#545454"));
            if (i % 2 == 0) _d.Text(c, (item.Minimum + (item.Maximum - item.Minimum) * i / 10).ToString("G3", CultureInfo.InvariantCulture), cx + x * (radius + 25), cy + y * (radius + 25) + 4, 10, center: true);
        }
        _d.Circle(c, cx + 1, cy + 2, radius + 2, LabDrawing.Color("#737373")); _d.Circle(c, cx, cy, radius, LabDrawing.Color("#EEEEEE")); _d.Circle(c, cx, cy, radius - 5, LabDrawing.Color("#C6C6C6"));
        var a = (135 + Fraction(item, value) * 270) * Math.PI / 180;
        _d.Line(c, cx, cy, cx + (float)Math.Cos(a) * (radius - 7), cy + (float)Math.Sin(a) * (radius - 7), LabDrawing.Color("#444444"), 4);
        var field = new SKRect(b.MidX - 37, b.Bottom - 25, b.MidX + 37, b.Bottom - 1); _d.Bevel(c, field, "#FFFFFF", true); _d.Text(c, value.ToString("G5", CultureInfo.InvariantCulture), b.MidX, b.Bottom - 7, 15, center: true);
    }
    private void DrawGauge(SKCanvas c, SKRect b, PanelItem item, double value)
    {
        _d.Bevel(c, b, "#D1D1D1"); var cx = b.MidX; var cy = b.Bottom - 28; var radius = Math.Min(b.Width * .41f, b.Height - 35);
        using var path = new SKPath(); path.MoveTo(cx - radius, cy); path.ArcTo(new(cx - radius, cy - radius, cx + radius, cy + radius), 180, 180, false); path.LineTo(cx, cy); path.Close(); _d.Path(c, path, LabDrawing.Color("#F7F6E9"), fill: true);
        for (var i = 0; i <= 10; i++)
        {
            var a = Math.PI + i * Math.PI / 10; var x = (float)Math.Cos(a); var y = (float)Math.Sin(a);
            _d.Line(c, cx + x * (radius - 4), cy + y * (radius - 4), cx + x * (radius - 12), cy + y * (radius - 12), LabDrawing.Color("#505050"));
            if (i % 2 == 0) _d.Text(c, (item.Minimum + (item.Maximum - item.Minimum) * i / 10).ToString("G2", CultureInfo.InvariantCulture), cx + x * (radius - 25), cy + y * (radius - 25) + 4, 10, center: true);
        }
        var angle = Math.PI + Fraction(item, value) * Math.PI; _d.Line(c, cx, cy, cx + (float)Math.Cos(angle) * (radius - 14), cy + (float)Math.Sin(angle) * (radius - 14), LabDrawing.Color("#B23128"), 2); _d.Circle(c, cx, cy, 5, LabDrawing.Color("#555555"));
        _d.Text(c, value.ToString("F4", CultureInfo.InvariantCulture) + " V", cx, b.Bottom - 7, 14, center: true);
    }
    private void DrawSlider(SKCanvas c, SKRect b, PanelItem item, double value)
    {
        var cy = b.MidY; _d.Bevel(c, new(b.Left + 8, cy - 4, b.Right - 8, cy + 4), "#777777", true);
        var x = b.Left + 8 + (float)Fraction(item, value) * (b.Width - 16); _d.Bevel(c, new(x - 7, cy - 16, x + 7, cy + 16));
        _d.Text(c, value.ToString("G5", CultureInfo.InvariantCulture), b.MidX, b.Bottom - 3, 12, center: true);
    }
    private void DrawStructured(SKCanvas c, SKRect b, Value value)
    {
        _d.Bevel(c, b, "#D7D7D7"); c.Save(); c.ClipRect(b); var y = b.Top + 19;
        if (value.Kind == ValueKind.Error)
        {
            _d.Circle(c, b.Left + 14, y - 4, 6, LabDrawing.Color(value.Boolean ? "#CA2828" : "#53A652"));
            _d.Text(c, value.Boolean ? "Error" : "No error", b.Left + 28, y, 12); y += 24;
            _d.Text(c, "code: " + value.Integer, b.Left + 8, y, 12); y += 24; _d.Text(c, "source: " + value.Text, b.Left + 8, y, 12);
        }
        else foreach (var field in value.Type.Fields)
        {
            if (y > b.Bottom - 5) break;
            _d.Text(c, field.Name, b.Left + 8, y, 11, "#666666");
            _d.Text(c, value.Fields[field.Name].ToString(), b.Left + Math.Min(105, b.Width / 2), y, 12); y += 25;
        }
        c.Restore();
    }
    private void DrawVertical(SKCanvas c, SKRect b, PanelItem item, double value, bool tank)
    {
        var width = tank ? Math.Min(70, b.Width / 2) : 14;
        var track = new SKRect(b.Left + 22, b.Top + 7, b.Left + 22 + width, b.Bottom - 29);
        _d.Bevel(c, track, "#F4F4F4", true);
        var liquid = new SKRect(track.Left + 3, track.Bottom - 3 - (float)Fraction(item, value) * (track.Height - 6), track.Right - 3, track.Bottom - 3);
        _d.Rect(c, liquid, LabDrawing.Color(tank ? "#2E80B8" : "#CB3229"));
        if (!tank) _d.Circle(c, track.MidX, track.Bottom, 11, LabDrawing.Color("#CB3229"));
        for (var i = 0; i <= 10; i++)
        {
            var y = track.Bottom - i * track.Height / 10; _d.Line(c, track.Right + 5, y, track.Right + (i % 2 == 0 ? 13 : 9), y, SKColors.Black);
            if (i % 2 == 0) _d.Text(c, (item.Minimum + (item.Maximum - item.Minimum) * i / 10).ToString("G3", CultureInfo.InvariantCulture), track.Right + 17, y + 4, 10);
        }
        _d.Text(c, value.ToString("G5", CultureInfo.InvariantCulture), b.MidX, b.Bottom - 3, 13, center: true);
    }
    public void Dispose() { _history.Clear(); _d.Dispose(); }
}
