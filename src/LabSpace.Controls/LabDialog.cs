using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Automation;

namespace LabSpace.Controls;

/// <summary>Classic modal Uno dialog; owns its chrome, buttons, keyboard behavior and focus restoration.</summary>
public sealed class LabDialog
{
    public Grid Root { get; } = new();
    public FrameworkElement Body { get; }
    public Func<bool>? Validate { get; set; }
    public Action? Opened { get; set; }
    public event Action? Closed;
    private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Popup _popup = new() { IsLightDismissEnabled = false };
    private Control? _previousFocus;
    private bool _closed;
    public LabDialog(string title, FrameworkElement content, string accept = "Apply", double width = 620)
    {
        Body = content;
        AutomationProperties.SetAutomationId(Root, "lab-dialog");
        Root.Background = LabTheme.Brush("#66000000");
        var window = new Grid { Width = width, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Background = LabTheme.Brush("#EFEFEF"), BorderBrush = LabTheme.Brush("#666666"), BorderThickness = new(1) };
        window.RowDefinitions.Add(new() { Height = new(30) }); window.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); window.RowDefinitions.Add(new() { Height = new(44) });
        var caption = new Grid { Background = LabTheme.Brush("#E0E0E0"), BorderBrush = LabTheme.Brush("#AAAAAA"), BorderThickness = new Thickness(0, 0, 0, 1) };
        var label = LabTheme.Text(title, 13); label.Margin = new(10, 0, 40, 0); caption.Children.Add(label);
        var close = new LabButton("×", () => Finish(false)) { Width = 30, HorizontalAlignment = HorizontalAlignment.Right }; caption.Children.Add(close); window.Children.Add(caption);
        var scroll = new ScrollViewer { Content = content, Padding = new Thickness(12), HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 1); window.Children.Add(scroll);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(10, 5, 10, 9) };
        var ok = new LabButton(accept, () => { if (Validate?.Invoke() != false) Finish(true); }, flat: false) { MinWidth = 84 };
        AutomationProperties.SetAutomationId(ok, "dialog-apply");
        var cancel = new LabButton("Cancel", () => Finish(false), flat: false) { MinWidth = 84 };
        AutomationProperties.SetAutomationId(cancel, "dialog-cancel"); footer.Children.Add(ok); footer.Children.Add(cancel); Grid.SetRow(footer, 2); window.Children.Add(footer); Root.Children.Add(window);
        Root.KeyDown += (_, e) => { if (e.Key == VirtualKey.Escape) { Finish(false); e.Handled = true; } };
        Root.Loaded += (_, _) => { Opened?.Invoke(); };
        Root.SizeChanged += (_, _) => { window.Width = Math.Max(260, Math.Min(width, Root.ActualWidth - 32)); window.MaxHeight = Math.Max(160, Root.ActualHeight - 32); };
        _popup.Closed += (_, _) => { if (!_closed) Finish(false); };
    }
    public Task<bool> ShowAsync(XamlRoot root)
    {
        _previousFocus = FocusManager.GetFocusedElement(root) as Control;
        Root.Width = root.Size.Width; Root.Height = root.Size.Height; _popup.XamlRoot = root; _popup.Child = Root;
        root.Changed += Resized;
        Closed += () => root.Changed -= Resized;
        void Resized(XamlRoot sender, XamlRootChangedEventArgs args) { Root.Width = sender.Size.Width; Root.Height = sender.Size.Height; }
        _popup.IsOpen = true; return _completion.Task;
    }
    public void Accept() { if (Validate?.Invoke() != false) Finish(true); }
    public void Cancel() => Finish(false);
    private void Finish(bool accepted)
    {
        if (_closed) return; _closed = true; _popup.IsOpen = false; _popup.Child = null;
        _previousFocus?.Focus(FocusState.Programmatic); Closed?.Invoke(); _completion.TrySetResult(accepted);
    }
}
