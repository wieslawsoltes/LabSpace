using LabSpace.Core;

namespace LabSpace.Controls;

/// <summary>Compact previous/current/next selector for real editable Case and Sequence subdiagrams.</summary>
public sealed class StructureFrameSelector : StackPanel
{
    private readonly ComboBox _choice = new() { MinHeight = 27, Width = 225, FontSize = 12 };
    private bool _updating;
    private string _signature = "";
    public LabButton Previous { get; }
    public LabButton Next { get; }
    public ComboBox Choice => _choice;
    public event Action<int>? FrameSelected;

    public StructureFrameSelector()
    {
        Orientation = Orientation.Horizontal; Spacing = 1; Margin = new(8, 0, 0, 0);
        Previous = new("◀", () => Select(-1)); Next = new("▶", () => Select(1));
        Children.Add(Previous); Children.Add(_choice); Children.Add(Next);
        _choice.SelectionChanged += (_, _) => { if (!_updating && _choice.SelectedIndex >= 0) FrameSelected?.Invoke(_choice.SelectedIndex); };
    }
    private void Select(int direction)
    {
        if (_choice.Items.Count == 0) return;
        FrameSelected?.Invoke((_choice.SelectedIndex + direction + _choice.Items.Count) % _choice.Items.Count);
    }
    public void Configure(Node? owner, string? frameId)
    {
        Visibility = owner is { Frames.Count: > 0 } ? Visibility.Visible : Visibility.Collapsed;
        if (owner is not { Frames.Count: > 0 }) return;
        _updating = true;
        try
        {
            var labels = owner.Frames.Select((f, i) => (owner.Kind == "sequence" ? $"{i} [{0}..{owner.Frames.Count - 1}] " : "") + f.Selector + (f.IsDefault ? ", Default" : "")).ToArray();
            var signature = string.Join('\u001f', labels);
            if (_signature != signature) { _signature = signature; _choice.ItemsSource = labels; }
            _choice.SelectedIndex = Math.Max(0, owner.Frames.FindIndex(f => f.Id == frameId));
        }
        finally { _updating = false; }
    }
}
