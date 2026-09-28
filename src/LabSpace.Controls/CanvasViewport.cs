using LabSpace.Core;
using LabSpace.Editing;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;

namespace LabSpace.Controls;

public sealed class PaintSurface : SKCanvasElement
{
    public Action<SKCanvas, Size>? Paint { get; set; }
    protected override void RenderOverride(SKCanvas canvas, Size area) => Paint?.Invoke(canvas, area);
}

/// <summary>Shared high-DPI viewport. Pointer coordinates and rendering both use device-independent units.</summary>
public abstract class CanvasViewport : UserControl, IDisposable
{
    protected readonly PaintSurface Canvas = new();
    protected readonly InstrumentSession Session;
    private Point _panStart;
    private double _panXStart, _panYStart;
    protected bool Panning;
    private bool _fitted;
    public double Zoom { get; private set; } = 1;
    public double PanX { get; private set; } = 16;
    public double PanY { get; private set; } = 16;
    public event Action? ViewportChanged;
    public SKRect WorldViewport => new((float)(-PanX / Zoom), (float)(-PanY / Zoom), (float)((ActualWidth - PanX) / Zoom), (float)((ActualHeight - PanY) / Zoom));
    protected CanvasViewport(InstrumentSession session)
    {
        Session = session; Content = Canvas; IsTabStop = true; HorizontalContentAlignment = HorizontalAlignment.Stretch; VerticalContentAlignment = VerticalAlignment.Stretch;
        Canvas.Paint = (c, size) => { c.Save(); c.ClipRect(new(0, 0, (float)size.Width, (float)size.Height)); c.Translate((float)PanX, (float)PanY); c.Scale((float)Zoom); Paint(c, WorldViewport); c.Restore(); };
        Canvas.SizeChanged += (_, _) => { if (!_fitted && ActualWidth > 100 && ActualHeight > 100) { _fitted = true; Fit(); } Invalidate(); };
        Session.Changed += SessionChanged;
        Canvas.PointerWheelChanged += (_, e) =>
        {
            var point = e.GetCurrentPoint(Canvas); var delta = point.Properties.MouseWheelDelta;
            if ((e.KeyModifiers & VirtualKeyModifiers.Shift) != 0) PanX += delta / 2.0;
            else ZoomAt(point.Position, Zoom * Math.Pow(1.12, delta / 120.0));
            Invalidate(); e.Handled = true;
        };
    }
    private void SessionChanged(SessionChange change) => Invalidate();
    public void Invalidate() => Canvas.Invalidate();
    public Point ToScreen(PointD p) => new(p.X * Zoom + PanX, p.Y * Zoom + PanY);
    public PointD ToWorld(Point p) => new((p.X - PanX) / Zoom, (p.Y - PanY) / Zoom);
    protected bool BeginPan(PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(Canvas);
        if (!point.Properties.IsMiddleButtonPressed) return false;
        Panning = true; _panStart = point.Position; _panXStart = PanX; _panYStart = PanY; Canvas.CapturePointer(e.Pointer); e.Handled = true; return true;
    }
    protected bool MovePan(PointerRoutedEventArgs e)
    {
        if (!Panning) return false;
        var point = e.GetCurrentPoint(Canvas).Position; PanX = _panXStart + point.X - _panStart.X; PanY = _panYStart + point.Y - _panStart.Y; Invalidate(); ViewportChanged?.Invoke(); e.Handled = true; return true;
    }
    protected bool EndPan() { if (!Panning) return false; Panning = false; Canvas.ReleasePointerCaptures(); return true; }
    public void ZoomAt(Point point, double zoom)
    {
        var world = ToWorld(point); Zoom = Math.Clamp(zoom, .15, 4); PanX = point.X - world.X * Zoom; PanY = point.Y - world.Y * Zoom; Invalidate(); ViewportChanged?.Invoke();
    }
    public void SetZoom(double value) => ZoomAt(new(ActualWidth / 2, ActualHeight / 2), value);
    public void Fit()
    {
        var bounds = ContentBounds();
        if (ActualWidth < 30 || ActualHeight < 30) return;
        Zoom = Math.Clamp(Math.Min((ActualWidth - 40) / Math.Max(1, bounds.Width), (ActualHeight - 45) / Math.Max(1, bounds.Height)), .15, 1.2);
        PanX = (ActualWidth - bounds.Width * Zoom) / 2 - bounds.Left * Zoom; PanY = 25 - bounds.Top * Zoom;
        Invalidate(); ViewportChanged?.Invoke();
    }
    protected abstract SKRect ContentBounds();
    protected abstract void Paint(SKCanvas canvas, SKRect viewport);
    public virtual void Dispose() { Session.Changed -= SessionChanged; Canvas.Paint = null; }
}
