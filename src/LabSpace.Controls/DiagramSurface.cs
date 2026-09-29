using LabSpace.Core;
using LabSpace.Editing;
using LabSpace.Skia;
using SkiaSharp;

namespace LabSpace.Controls;

/// <summary>Reusable node-and-wire editor with terminal wiring, transactional dragging, marquee selection and zoom.</summary>
public sealed class DiagramSurface : CanvasViewport
{
    public DiagramRenderer Renderer { get; }
    private string? _wiringFrom;
    private string _wiringOutput = "result";
    private Node? _placement;
    public string? PlacementKind => _placement?.Kind;
    public void ArmPlacement(string kind) { Cancel(); _placement = LabSpace.Documents.Examples.NewNode(kind, _pointer.X, _pointer.Y); Focus(FocusState.Programmatic); Invalidate(); }
    private PointD _start, _pointer;
    private bool _dragging, _marquee, _wireMoved;
    private readonly Dictionary<string, PointD> _original = [];
    public event Action<Node>? EditRequested;
    public event Action<Node?>? HoverChanged;
    public event Action<Point>? PaletteRequested;
    public event Action<DiagramContextRequest>? ContextRequested;
    public DiagramSurface(InstrumentSession session, LabFonts fonts) : base(session)
    {
        Renderer = new(fonts);
        Canvas.PointerPressed += Pressed; Canvas.PointerMoved += Moved; Canvas.PointerReleased += Released;
        Canvas.PointerCanceled += (_, _) => Cancel(); Canvas.PointerCaptureLost += (_, _) => { if (_dragging || _marquee) Cancel(); };
        Canvas.DoubleTapped += (_, e) =>
        {
            var world = ToWorld(e.GetPosition(Canvas)); var node = HitNode(world);
            if (node is null) return;
            if (NodeCatalog.Get(node.Kind).IsStructure) { Session.Enter(node.Id); Fit(); }
            else EditRequested?.Invoke(node);
            e.Handled = true;
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Escape) { Cancel(); e.Handled = true; }
            else if (e.Key == VirtualKey.Delete) { Safe(Session.Delete); e.Handled = true; }
            else if (e.Key is VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down)
            {
                var dx = e.Key == VirtualKey.Left ? -10 : e.Key == VirtualKey.Right ? 10 : 0; var dy = e.Key == VirtualKey.Up ? -10 : e.Key == VirtualKey.Down ? 10 : 0;
                Safe(() => Session.Edit(() => { foreach (var node in Session.Diagram.Nodes.Where(n => Session.Selection.Contains(n.Id))) { node.X += dx; node.Y += dy; } }, false)); e.Handled = true;
            }
        };
    }
    public Node? HitNode(PointD point) => Session.Diagram.Nodes.LastOrDefault(n => DiagramGeometry.Bounds(n).Contains((float)point.X, (float)point.Y));
    private (Node Node, int Index, bool Output)? HitPort(PointD point)
    {
        var p = new SKPoint((float)point.X, (float)point.Y); var tolerance = Math.Max(7, 8 / Zoom);
        foreach (var node in Session.Diagram.Nodes.AsEnumerable().Reverse())
        {
            if (!NodeCatalog.TryGet(node.Kind, out _)) continue;
            var def = NodeCatalog.Resolve(node);
            for (var i = 0; i < def.Outputs.Length; i++) if (SKPoint.Distance(DiagramGeometry.Output(node, i), p) <= tolerance) return (node, i, true);
            for (var i = 0; i < def.Inputs.Length; i++) if (SKPoint.Distance(DiagramGeometry.Input(node, i), p) <= tolerance) return (node, i, false);
        }
        return null;
    }
    private void Pressed(object sender, PointerRoutedEventArgs e)
    {
        Focus(FocusState.Pointer); if (BeginPan(e)) return;
        var point = e.GetCurrentPoint(Canvas); _pointer = _start = ToWorld(point.Position);
        if (point.Properties.IsRightButtonPressed)
        {
            var terminal = HitPort(_pointer);
            var target = terminal?.Node ?? HitNode(_pointer);
            var wire = target is null ? Renderer.HitWire(Session.Diagram, new((float)_pointer.X, (float)_pointer.Y), 6 / Zoom) : null;
            if (target is not null || wire is not null)
            {
                if (target is not null) Session.Select(target.Id); else Session.SelectWire(wire!);
                var name = terminal is { } t ? t.Output ? NodeCatalog.Resolve(t.Node).Outputs[t.Index].Name : NodeCatalog.Resolve(t.Node).Inputs[t.Index].Name : null;
                ContextRequested?.Invoke(new(target, name, terminal?.Output ?? false, wire, point.Position));
            }
            else PaletteRequested?.Invoke(point.Position);
            e.Handled = true; return;
        }
        if (!point.Properties.IsLeftButtonPressed) return;
        if (_placement is { } placement)
        {
            _placement = null; Safe(() => Session.Add(placement.Kind, Math.Round(_pointer.X / 10) * 10, Math.Round(_pointer.Y / 10) * 10));
            Invalidate(); e.Handled = true; return;
        }
        var headerNode = HitNode(_pointer);
        if (headerNode is { Frames.Count: > 0 } && DiagramGeometry.Selector(headerNode).Contains((float)_pointer.X, (float)_pointer.Y))
        {
            var header = DiagramGeometry.Selector(headerNode);
            if (_pointer.X < header.Left + 22) Session.CyclePreview(headerNode.Id, -1);
            else if (_pointer.X > header.Right - 22) Session.CyclePreview(headerNode.Id, 1);
            else { Session.EnterFrame(headerNode.Id, Session.PreviewIndex(headerNode)); Fit(); }
            Invalidate(); e.Handled = true; return;
        }
        var port = HitPort(_pointer);
        if (port.HasValue)
        {
            if (port.Value.Output) { _wiringFrom = port.Value.Node.Id; _wiringOutput = NodeCatalog.Resolve(port.Value.Node).Outputs[port.Value.Index].Name; _wireMoved = false; Session.Select(port.Value.Node.Id); Canvas.CapturePointer(e.Pointer); }
            else if (_wiringFrom is not null) { FinishWire(port.Value); }
            else
            {
                var name = NodeCatalog.Resolve(port.Value.Node).Inputs[port.Value.Index].Name;
                Session.Message($"{port.Value.Node.Label} · {name}: click an output terminal first, then this input.");
            }
            Invalidate(); e.Handled = true; return;
        }
        if (_wiringFrom is not null) { _wiringFrom = null; Invalidate(); }
        var node = HitNode(_pointer);
        if (node is not null)
        {
            var additive = (e.KeyModifiers & VirtualKeyModifiers.Control) != 0;
            if (!Session.Selection.Contains(node.Id) || additive) Session.Select(node.Id, additive);
            _original.Clear(); foreach (var selected in Session.Diagram.Nodes.Where(n => Session.Selection.Contains(n.Id))) _original[selected.Id] = new(selected.X, selected.Y);
            Session.BeginGesture(); _dragging = true; Canvas.CapturePointer(e.Pointer);
        }
        else
        {
            var wire = Renderer.HitWire(Session.Diagram, new((float)_pointer.X, (float)_pointer.Y), 6 / Zoom);
            if (wire is not null) Session.SelectWire(wire);
            else { Session.Select(null); _marquee = true; Canvas.CapturePointer(e.Pointer); }
        }
        e.Handled = true;
    }
    private void Moved(object sender, PointerRoutedEventArgs e)
    {
        if (MovePan(e)) return;
        _pointer = ToWorld(e.GetCurrentPoint(Canvas).Position);
        if (_placement is not null) { _placement.X = _pointer.X; _placement.Y = _pointer.Y; Invalidate(); return; }
        if (_wiringFrom is not null) { _wireMoved |= Math.Abs(_pointer.X - _start.X) + Math.Abs(_pointer.Y - _start.Y) > 5; Invalidate(); }
        if (_dragging)
        {
            var dx = Math.Round((_pointer.X - _start.X) / 10) * 10; var dy = Math.Round((_pointer.Y - _start.Y) / 10) * 10;
            foreach (var (id, position) in _original) if (Session.Find(id) is { } n) { n.X = position.X + dx; n.Y = position.Y + dy; }
            Invalidate();
        }
        else if (_marquee) Invalidate();
        else HoverChanged?.Invoke(HitNode(_pointer));
    }
    private void Released(object sender, PointerRoutedEventArgs e)
    {
        if (EndPan()) return;
        _pointer = ToWorld(e.GetCurrentPoint(Canvas).Position);
        if (_wiringFrom is not null && _wireMoved && HitPort(_pointer) is { Output: false } port) FinishWire(port);
        if (_dragging) { _dragging = false; Safe(Session.EndGesture); }
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
        if (from is not null) Safe(() => Session.Connect(from, target.Node.Id, NodeCatalog.Resolve(target.Node).Inputs[target.Index].Name, _wiringOutput));
    }
    private void Safe(Action action) { try { action(); } catch (Exception error) { Session.Message(error.Message); } }
    public void StartWire(string nodeId, string output)
    {
        Cancel(); var node = Session.Find(nodeId);
        if (node is null || NodeCatalog.Resolve(node).FindOutput(output) is null) return;
        _wiringFrom = nodeId; _wiringOutput = output; _wireMoved = false; _start = _pointer;
        Focus(FocusState.Programmatic); Invalidate();
    }
    public void Cancel() { _placement = null; _dragging = false; _marquee = false; _wiringFrom = null; Panning = false; Session.CancelGesture(); Canvas.ReleasePointerCaptures(); Invalidate(); }
    private SKRect MarqueeBounds() => new((float)Math.Min(_start.X, _pointer.X), (float)Math.Min(_start.Y, _pointer.Y), (float)Math.Max(_start.X, _pointer.X), (float)Math.Max(_start.Y, _pointer.Y));
    protected override SKRect ContentBounds()
    {
        var nodes = Session.Diagram.Nodes;
        return nodes.Count == 0 ? new(0, 0, 850, 520) : new((float)nodes.Min(n => n.X) - 180, (float)nodes.Min(n => n.Y) - 30, nodes.Max(n => DiagramGeometry.Bounds(n).Right) + 200, nodes.Max(n => DiagramGeometry.Bounds(n).Bottom) + 45);
    }
    protected override void Paint(SKCanvas canvas, SKRect viewport)
    {
        Renderer.Draw(canvas, Session, viewport, _wiringFrom, new((float)_pointer.X, (float)_pointer.Y), _marquee ? MarqueeBounds() : null, _wiringOutput);
        if (_placement is not null) Renderer.DrawPlacement(canvas, _placement);
    }
    public override void Dispose() { base.Dispose(); Renderer.Dispose(); }
}
