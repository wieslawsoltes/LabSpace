using LabSpace.Core;
using LabSpace.Editing;
using LabSpace.Skia;
using SkiaSharp;

namespace LabSpace.Controls;

public sealed class FrontPanelSurface : CanvasViewport
{
    public PanelRenderer Renderer { get; }
    private PanelItem? _item;
    private string? _placementKind, _placementWidget;
    private PointD _pointer;
    public string? PlacementKind => _placementKind;
    public event Action<Point>? PaletteRequested;
    public void ArmPlacement(string kind, string? widget) { Cancel(); _placementKind = kind; _placementWidget = widget; Focus(FocusState.Programmatic); Invalidate(); }
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
        var point = e.GetCurrentPoint(Canvas);
        if (point.Properties.IsRightButtonPressed) { PaletteRequested?.Invoke(point.Position); e.Handled = true; return; }
        if (!point.Properties.IsLeftButtonPressed) return;
        if (_placementKind is { } kind)
        {
            var position = ToWorld(point.Position); var widget = _placementWidget; _placementKind = null;
            Safe(() => Session.Add(kind, 80 + Session.Diagram.Nodes.Count % 4 * 180, 80 + Session.Diagram.Nodes.Count / 4 * 100, widget, new(Math.Round(position.X / 10) * 10, Math.Round(position.Y / 10) * 10)));
            Session.PanelEditMode = true; Invalidate(); e.Handled = true; return;
        }
        _start = ToWorld(point.Position); _item = Hit(_start);
        if (_item is null) { Session.Select(null); return; }
        Session.Select(_item.NodeId); var node = Session.Instrument.Diagram.Nodes.First(n => n.Id == _item.NodeId); var def = NodeCatalog.Get(node.Kind); _bounds = _item.Bounds; _value = node.Value;
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
            if (node.Kind == "bool-control") Safe(() => Session.SetValue(node.Id, node.Value == 0 ? 1 : 0));
            else if (node.Kind is "string-control" or "error-control" or "complex-control") EditRequested?.Invoke(node);
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
        if (MovePan(e)) return;
        _pointer = ToWorld(e.GetCurrentPoint(Canvas).Position);
        if (_placementKind is not null) { Invalidate(); return; }
        if (_item is null) return;
        var p = ToWorld(e.GetCurrentPoint(Canvas).Position); var dx = p.X - _start.X; var dy = p.Y - _start.Y;
        if (_moving)
        {
            _item.Bounds = _resize ? _bounds with { Width = Math.Max(80, Math.Round((_bounds.Width + dx) / 10) * 10), Height = Math.Max(_item.Widget is "Knob" or "Gauge" or "Graph" or "Chart" ? 140 : 70, Math.Round((_bounds.Height + dy) / 10) * 10) } : _bounds with { X = Math.Round((_bounds.X + dx) / 10) * 10, Y = Math.Round((_bounds.Y + dy) / 10) * 10 };
            Invalidate();
        }
        else if (_operating)
        {
            var node = Session.Instrument.Diagram.Nodes.First(n => n.Id == _item.NodeId);
            node.Value = Math.Clamp(_item.Widget == "Slider" ? _item.Minimum + (p.X - _bounds.X) / _bounds.Width * (_item.Maximum - _item.Minimum) : _value + (dx - dy) / 180 * (_item.Maximum - _item.Minimum), _item.Minimum, _item.Maximum);
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
    public void Cancel() { _placementKind = null; _placementWidget = null; _moving = false; _operating = false; _item = null; Panning = false; Session.CancelGesture(); Canvas.ReleasePointerCaptures(); Invalidate(); }
    protected override SKRect ContentBounds()
    {
        var items = Session.Instrument.Panel; return items.Count == 0 ? new(0, 0, 1000, 600) : new(0, 0, (float)items.Max(i => i.Bounds.Right) + 30, (float)items.Max(i => i.Bounds.Bottom) + 30);
    }
    protected override void Paint(SKCanvas canvas, SKRect viewport)
    {
        Renderer.Draw(canvas, Session, viewport);
        if (_placementKind is not null)
        {
            using var pen = new SKPaint { Color = SKColor.Parse("#3977B4"), Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = true };
            canvas.DrawRect((float)_pointer.X, (float)_pointer.Y, _placementWidget is "Graph" or "Chart" ? 410 : 160, _placementWidget is "Graph" or "Chart" ? 240 : _placementWidget == "Knob" ? 175 : 95, pen);
        }
    }
    public override void Dispose() { base.Dispose(); Renderer.Dispose(); }
}
