using LabSpace.Core;
using LabSpace.Editing;
using LabSpace.Skia;
using SkiaSharp;

namespace LabSpace.Controls;

/// <summary>Transactional diagram editor: named terminals, wire branches, bend placement and segment dragging.</summary>
public sealed class DiagramSurface : CanvasViewport
{
    public DiagramRenderer Renderer { get; }
    private string? _wiringFrom;
    private string _wiringOutput = "value";
    private readonly List<PointD> _waypoints = [];
    private PointD _start, _pointer;
    private bool _dragging, _marquee, _wireMoved;
    private Wire? _segmentWire;
    private SKPoint[] _segmentPoints = [];
    private int _segmentIndex;
    private readonly Dictionary<string, PointD> _original = [];
    public event Action<Node>? EditRequested;
    public event Action<Node?>? HoverChanged;
    public event Action<Point>? PaletteRequested;

    public DiagramSurface(InstrumentSession session, LabFonts fonts) : base(session)
    {
        Renderer = new(fonts);
        Canvas.PointerPressed += Pressed;
        Canvas.PointerMoved += Moved;
        Canvas.PointerReleased += Released;
        Canvas.PointerCanceled += (_, _) => Cancel();
        Canvas.PointerCaptureLost += (_, _) => { if (_dragging || _marquee || _segmentWire is not null) Cancel(); };
        Canvas.DoubleTapped += (_, e) =>
        {
            var node = HitNode(ToWorld(e.GetPosition(Canvas)));
            if (node is null) return;
            if (NodeCatalog.Describe(node).IsStructure) { Session.Enter(node.Id, node.PreviewAlternative); Fit(); }
            else EditRequested?.Invoke(node);
            e.Handled = true;
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Escape) { Cancel(); e.Handled = true; }
            else if (e.Key == VirtualKey.Delete) { Safe(Session.Delete); e.Handled = true; }
            else if (e.Key is VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down)
            {
                var dx = e.Key == VirtualKey.Left ? -10 : e.Key == VirtualKey.Right ? 10 : 0;
                var dy = e.Key == VirtualKey.Up ? -10 : e.Key == VirtualKey.Down ? 10 : 0;
                Safe(() => Session.Edit(() => { foreach (var n in Session.Diagram.Nodes.Where(n => Session.Selection.Contains(n.Id))) { n.X += dx; n.Y += dy; } }, false));
                e.Handled = true;
            }
        };
    }
    public Node? HitNode(PointD point) => Session.Diagram.Nodes.LastOrDefault(n => DiagramGeometry.Bounds(n).Contains((float)point.X, (float)point.Y));
    private (Node Node, int Index, bool Output)? HitPort(PointD point)
    {
        var p = new SKPoint((float)point.X, (float)point.Y);
        var tolerance = Math.Max(6, 7 / Zoom);
        foreach (var node in Session.Diagram.Nodes.AsEnumerable().Reverse())
        {
            var def = NodeCatalog.Describe(node);
            for (var i = 0; i < def.OutputPorts.Length; i++)
                if (SKPoint.Distance(DiagramGeometry.Output(node, i), p) <= tolerance) return (node, i, true);
            for (var i = 0; i < def.Inputs.Length; i++)
                if (SKPoint.Distance(DiagramGeometry.Input(node, i), p) <= tolerance) return (node, i, false);
        }
        return null;
    }
    private void Pressed(object sender, PointerRoutedEventArgs e)
    {
        Focus(FocusState.Pointer);
        if (BeginPan(e)) return;
        var point = e.GetCurrentPoint(Canvas); _pointer = _start = ToWorld(point.Position);
        var sk = new SKPoint((float)_pointer.X, (float)_pointer.Y);
        var node = HitNode(_pointer);
        if (point.Properties.IsRightButtonPressed)
        {
            if (node is not null) Session.Select(node.Id);
            else if (Renderer.HitWire(Session.Diagram, sk, 6 / Zoom) is { } wireId) Session.SelectWire(wireId);
            else Session.Select(null);
            PaletteRequested?.Invoke(point.Position); e.Handled = true; return;
        }
        if (!point.Properties.IsLeftButtonPressed) return;
        if (HitPort(_pointer) is { } port)
        {
            if (port.Output)
            {
                _wiringFrom = port.Node.Id; _wiringOutput = NodeCatalog.Describe(port.Node).OutputPorts[port.Index].Name;
                _waypoints.Clear(); _wireMoved = false; Session.Select(port.Node.Id); Canvas.CapturePointer(e.Pointer);
            }
            else if (_wiringFrom is not null) FinishWire(port);
            else Session.Message($"{port.Node.Label} · {NodeCatalog.Describe(port.Node).Inputs[port.Index].Name}: click an output terminal first.");
            Invalidate(); e.Handled = true; return;
        }
        if (_wiringFrom is not null)
        {
            if (_waypoints.Count < 120) _waypoints.Add(new(Math.Round(_pointer.X / 5) * 5, Math.Round(_pointer.Y / 5) * 5));
            Invalidate(); e.Handled = true; return;
        }
        if (node is not null)
        {
            var additive = (e.KeyModifiers & VirtualKeyModifiers.Control) != 0;
            if (!Session.Selection.Contains(node.Id) || additive) Session.Select(node.Id, additive);
            var bounds = DiagramGeometry.Bounds(node);
            if (node.Kind is "case" or "case-typed" && _pointer.Y < bounds.Top + 24 && _pointer.X > bounds.MidX - 55 && _pointer.X < bounds.MidX + 55)
            {
                Session.Edit(() => node.PreviewAlternative = !node.PreviewAlternative, false); e.Handled = true; return;
            }
            _original.Clear();
            foreach (var selected in Session.Diagram.Nodes.Where(n => Session.Selection.Contains(n.Id))) _original[selected.Id] = new(selected.X, selected.Y);
            Session.BeginGesture(); _dragging = true; Canvas.CapturePointer(e.Pointer);
        }
        else if (Renderer.HitWire(Session.Diagram, sk, 6 / Zoom) is { } wireId)
        {
            var wire = Session.Diagram.Wires.First(w => w.Id == wireId);
            var points = Renderer.WirePoints(Session.Diagram, wireId).ToArray();
            var index = ClosestSegment(points, sk);
            if ((e.KeyModifiers & VirtualKeyModifiers.Control) != 0)
            {
                _wiringFrom = wire.From; _wiringOutput = wire.Output; _waypoints.Clear();
                _waypoints.AddRange(points.Skip(1).Take(index).Select(p => new PointD(p.X, p.Y)));
                var junction = DiagramGeometry.ClosestPoint(sk, points[index], points[index + 1]);
                _waypoints.Add(new(junction.X, junction.Y)); _wireMoved = false;
                Session.Message("Branch wire: click an input terminal. Click empty space to place bends; Escape cancels.");
            }
            else if (Session.SelectedWire == wireId && index > 0 && index < points.Length - 2 && points.Length <= 124)
            {
                Session.BeginGesture(); _segmentWire = wire; _segmentPoints = points; _segmentIndex = index; Canvas.CapturePointer(e.Pointer);
            }
            else Session.SelectWire(wireId);
        }
        else { Session.Select(null); _marquee = true; Canvas.CapturePointer(e.Pointer); }
        e.Handled = true;
    }
    private static int ClosestSegment(SKPoint[] points, SKPoint point)
    {
        var best = 0; var distance = float.MaxValue;
        for (var i = 0; i + 1 < points.Length; i++)
        {
            var d = SKPoint.Distance(point, DiagramGeometry.ClosestPoint(point, points[i], points[i + 1]));
            if (d < distance) { best = i; distance = d; }
        }
        return best;
    }
    private void Moved(object sender, PointerRoutedEventArgs e)
    {
        if (MovePan(e)) return;
        _pointer = ToWorld(e.GetCurrentPoint(Canvas).Position);
        if (_wiringFrom is not null) { _wireMoved |= Math.Abs(_pointer.X - _start.X) + Math.Abs(_pointer.Y - _start.Y) > 5; Invalidate(); }
        if (_dragging)
        {
            var dx = Math.Round((_pointer.X - _start.X) / 10) * 10; var dy = Math.Round((_pointer.Y - _start.Y) / 10) * 10;
            foreach (var (id, p) in _original) if (Session.Find(id) is { } n) { n.X = p.X + dx; n.Y = p.Y + dy; }
            Invalidate();
        }
        else if (_segmentWire is not null)
        {
            var points = (SKPoint[])_segmentPoints.Clone(); var a = points[_segmentIndex]; var b = points[_segmentIndex + 1];
            if (Math.Abs(a.X - b.X) < .01) a.X = b.X = (float)(Math.Round(_pointer.X / 5) * 5);
            else a.Y = b.Y = (float)(Math.Round(_pointer.Y / 5) * 5);
            points[_segmentIndex] = a; points[_segmentIndex + 1] = b;
            _segmentWire.Waypoints = points.Skip(1).SkipLast(1).Select(p => new PointD(p.X, p.Y)).ToList(); Invalidate();
        }
        else if (_marquee) Invalidate();
        else HoverChanged?.Invoke(HitNode(_pointer));
    }
    private void Released(object sender, PointerRoutedEventArgs e)
    {
        if (EndPan()) return;
        _pointer = ToWorld(e.GetCurrentPoint(Canvas).Position);
        if (_wiringFrom is not null && _wireMoved && HitPort(_pointer) is { Output: false } port) FinishWire(port);
        if (_dragging || _segmentWire is not null) { _dragging = false; _segmentWire = null; Safe(Session.EndGesture); }
        if (_marquee)
        {
            _marquee = false; var rectangle = MarqueeBounds(); Session.Selection.Clear();
            foreach (var n in Session.Diagram.Nodes) if (DiagramGeometry.Bounds(n).IntersectsWith(rectangle)) Session.Selection.Add(n.Id);
            Session.Notify(SessionChange.Selection | SessionChange.View);
        }
        Canvas.ReleasePointerCaptures(); Invalidate(); e.Handled = true;
    }
    private void FinishWire((Node Node, int Index, bool Output) target)
    {
        var from = _wiringFrom; _wiringFrom = null;
        if (from is not null) Safe(() => Session.Connect(from, target.Node.Id, NodeCatalog.Describe(target.Node).Inputs[target.Index].Name, _wiringOutput, _waypoints));
        _waypoints.Clear();
    }
    private void Safe(Action action) { try { action(); } catch (Exception error) { Session.Message(error.Message); } }
    public void Cancel() { _dragging = false; _marquee = false; _segmentWire = null; _wiringFrom = null; _waypoints.Clear(); Panning = false; Session.CancelGesture(); Canvas.ReleasePointerCaptures(); Invalidate(); }
    private SKRect MarqueeBounds() => new((float)Math.Min(_start.X, _pointer.X), (float)Math.Min(_start.Y, _pointer.Y), (float)Math.Max(_start.X, _pointer.X), (float)Math.Max(_start.Y, _pointer.Y));
    protected override SKRect ContentBounds()
    {
        var bounds = Session.Diagram.Nodes.Select(DiagramGeometry.VisualBounds).ToArray();
        return bounds.Length == 0 ? new(0, 0, 850, 520) : new(bounds.Min(b => b.Left) - 12, bounds.Min(b => b.Top) - 12, bounds.Max(b => b.Right) + 25, bounds.Max(b => b.Bottom) + 25);
    }
    protected override void Paint(SKCanvas canvas, SKRect viewport) => Renderer.Draw(canvas, Session, viewport, _wiringFrom, new((float)_pointer.X, (float)_pointer.Y), _marquee ? MarqueeBounds() : null, _wiringOutput, _waypoints);
    public override void Dispose() { Cancel(); base.Dispose(); Renderer.Dispose(); }
}
