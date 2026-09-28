using LabSpace.Core;
using LabSpace.Editing;
using LabSpace.Skia;
using SkiaSharp;

namespace LabSpace.Controls;

public sealed class FrontPanelSurface : CanvasViewport
{
    public PanelRenderer Renderer { get; }
    private PanelItem? _item;
    private PointD _start;
    private RectD _bounds;
    private double _value;
    private bool _resize, _moving, _operating;
    public event Action<Node>? EditRequested;
    public FrontPanelSurface(InstrumentSession session, LabFonts fonts) : base(session)
    {
        Renderer = new(fonts); Canvas.PointerPressed += Pressed; Canvas.PointerMoved += Moved; Canvas.PointerReleased += Released;
        Canvas.PointerCanceled += (_, _) => Cancel(); Canvas.PointerCaptureLost += (_, _) => { if (_moving || _operating) Cancel(); };
        Canvas.DoubleTapped += (_, e) => { var p = ToWorld(e.GetPosition(Canvas)); var item = Hit(p); if (item is not null && Session.Instrument.Diagram.Nodes.FirstOrDefault(n => n.Id == item.NodeId) is { } node) EditRequested?.Invoke(node); e.Handled = true; };
        KeyDown += (_, e) => { if (e.Key == VirtualKey.Escape) { Cancel(); e.Handled = true; } else if (e.Key == VirtualKey.Delete && Session.PanelEditMode) { Session.Delete(); e.Handled = true; } };
    }
    private PanelItem? Hit(PointD p) => Session.Instrument.Panel.LastOrDefault(item => item.Bounds.Contains(p));
    private void Pressed(object sender, PointerRoutedEventArgs e)
    {
        Focus(FocusState.Pointer); if (BeginPan(e)) return;
        var point = e.GetCurrentPoint(Canvas); if (!point.Properties.IsLeftButtonPressed) return;
        _start = ToWorld(point.Position); _item = Hit(_start);
        if (_item is null) { Session.Select(null); return; }
        Session.Select(_item.NodeId); var node = Session.Instrument.Diagram.Nodes.First(n => n.Id == _item.NodeId); var def = NodeCatalog.Describe(node); _bounds = _item.Bounds; _value = Session.DisplayValue(node).Number;
        if (Session.PanelEditMode)
        {
            _resize = Math.Abs(_start.X - _bounds.Right) < 15 / Zoom && Math.Abs(_start.Y - _bounds.Bottom) < 15 / Zoom;
            Session.BeginGesture(); _moving = true; Canvas.CapturePointer(e.Pointer);
        }
        else if (_item.Widget is "Graph" or "Chart")
        {
            Renderer.Cursors[_item.Id] = Math.Clamp((_start.X - _bounds.X - 43) / Math.Max(1, _bounds.Width - 57), 0, 1); Invalidate();
        }
        else if (def.IsControl)
        {
            if (node.Kind == "typed-control")
            {
                var current = Session.DisplayValue(node);
                if (current.Kind == ValueKind.Boolean) Safe(() => Session.SetText(node.Id, current.Boolean ? "false" : "true"));
                else if (current.Kind == ValueKind.Enum) Safe(() => Session.SetText(node.Id, ((current.Integer + 1) % current.Type.Labels.Length).ToString(System.Globalization.CultureInfo.InvariantCulture)));
                else if (current.Type.IsNumeric && _item.Widget is "Knob" or "Slider") { Session.BeginGesture(); _operating = true; Canvas.CapturePointer(e.Pointer); }
                else if (current.Type.IsNumeric && _item.Widget == "Numeric" && _start.X < _item.Bounds.X + 16)
                {
                    var delta = _start.Y < _item.Bounds.Y + 43 ? 1 : -1;
                    Safe(() =>
                    {
                        if (current.Type.IsInteger)
                        {
                            var (min, max) = ValueConversion.Range(current.Type);
                            var exact = System.Numerics.BigInteger.Clamp(ValueConversion.ExactInteger(current) + delta, min, max);
                            Session.SetText(node.Id, exact.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        }
                        else Session.SetText(node.Id, (current.Number + delta).ToString("G17", System.Globalization.CultureInfo.InvariantCulture));
                    });
                }
                else EditRequested?.Invoke(node);
            }
            else if (node.Kind == "bool-control") Safe(() => Session.SetValue(node.Id, node.Value == 0 ? 1 : 0));
            else if (node.Kind == "string-control") EditRequested?.Invoke(node);
            else if (_item.Widget is "Knob" or "Slider") { Session.BeginGesture(); _operating = true; Canvas.CapturePointer(e.Pointer); }
            else if (_start.X < _bounds.X + 15)
            {
                var increment = _start.Y < _bounds.Y + 48 ? 1 : -1; Safe(() => Session.SetValue(node.Id, Math.Clamp(node.Value + increment, _item.Minimum, _item.Maximum)));
            }
            else EditRequested?.Invoke(node);
        }
        e.Handled = true;
    }
    private void Moved(object sender, PointerRoutedEventArgs e)
    {
        if (MovePan(e) || _item is null) return;
        var p = ToWorld(e.GetCurrentPoint(Canvas).Position); var dx = p.X - _start.X; var dy = p.Y - _start.Y;
        if (_moving)
        {
            _item.Bounds = _resize ? _bounds with { Width = Math.Max(80, Math.Round((_bounds.Width + dx) / 10) * 10), Height = Math.Max(_item.Widget is "Knob" or "Gauge" or "Graph" or "Chart" ? 140 : 70, Math.Round((_bounds.Height + dy) / 10) * 10) } : _bounds with { X = Math.Round((_bounds.X + dx) / 10) * 10, Y = Math.Round((_bounds.Y + dy) / 10) * 10 };
            Invalidate();
        }
        else if (_operating)
        {
            var node = Session.Instrument.Diagram.Nodes.First(n => n.Id == _item.NodeId);
            var next = Math.Clamp(_item.Widget == "Slider" ? _item.Minimum + (p.X - _bounds.X) / _bounds.Width * (_item.Maximum - _item.Minimum) : _value + (dx - dy) / 180 * (_item.Maximum - _item.Minimum), _item.Minimum, _item.Maximum);
            if (node.Kind == "typed-control" && node.Type is { } type)
                node.Text = ValueConversion.Convert(Value.Numeric(next), type).ToString();
            else node.Value = next;
            Session.Notify(SessionChange.View);
        }
    }
    private void Released(object sender, PointerRoutedEventArgs e)
    {
        if (EndPan()) return;
        if (_moving || _operating) { _moving = false; _operating = false; Safe(Session.EndGesture); }
        _item = null; Canvas.ReleasePointerCaptures(); e.Handled = true;
    }
    private void Safe(Action action) { try { action(); } catch (Exception error) { Session.Message(error.Message); } }
    public void Cancel() { _moving = false; _operating = false; _item = null; Panning = false; Session.CancelGesture(); Canvas.ReleasePointerCaptures(); Invalidate(); }
    protected override SKRect ContentBounds()
    {
        var items = Session.Instrument.Panel; return items.Count == 0 ? new(0, 0, 1000, 600) : new(0, 0, (float)items.Max(i => i.Bounds.Right) + 30, (float)items.Max(i => i.Bounds.Bottom) + 30);
    }
    protected override void Paint(SKCanvas canvas, SKRect viewport) => Renderer.Draw(canvas, Session, viewport);
    public override void Dispose() { base.Dispose(); Renderer.Dispose(); }
}
