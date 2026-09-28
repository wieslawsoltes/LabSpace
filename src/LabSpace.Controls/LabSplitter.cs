namespace LabSpace.Controls;

/// <summary>Small reusable splitter with pointer capture and cancellation-safe incremental deltas.</summary>
public sealed class LabSplitter : Border
{
    private bool _dragging;
    private Point _previous;
    public event Action<double>? Delta;
    public Orientation Orientation { get; init; } = Orientation.Vertical;
    public LabSplitter()
    {
        Background = LabTheme.Brush("#C3C3C3");
        PointerEntered += (_, _) => Background = LabTheme.Brush("#8CA9C2");
        PointerExited += (_, _) => { if (!_dragging) Background = LabTheme.Brush("#C3C3C3"); };
        PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            _dragging = true; _previous = e.GetCurrentPoint(null).Position; CapturePointer(e.Pointer); e.Handled = true;
        };
        PointerMoved += (_, e) =>
        {
            if (!_dragging) return; var point = e.GetCurrentPoint(null).Position;
            Delta?.Invoke(Orientation == Orientation.Vertical ? point.X - _previous.X : point.Y - _previous.Y); _previous = point; e.Handled = true;
        };
        PointerReleased += (_, _) => { _dragging = false; ReleasePointerCaptures(); };
        PointerCanceled += (_, _) => _dragging = false;
        PointerCaptureLost += (_, _) => _dragging = false;
    }
}
