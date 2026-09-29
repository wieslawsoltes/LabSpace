using LabSpace.Core;
using LabSpace.Editing;

namespace LabSpace.Controls;

/// <summary>Owns a deep draft of structure frames. Reordering and removal never mutate the live VI.</summary>
public sealed class FrameEditorControl : StackPanel
{
    private readonly Node _draft;
    private readonly StackPanel _rows = new() { Spacing = 5 };
    private readonly LabButton _type;
    private readonly CheckBox _ignore = new() { Content = "Ignore case for string selectors", FontSize = 12 };
    private static readonly ValueKind[] SelectorKinds = [ValueKind.Boolean, ValueKind.Number, ValueKind.String, ValueKind.Error];
    public Dictionary<string, FrameworkElement> Fields { get; } = [];
    public IReadOnlyList<StructureFrame> Frames => _draft.Frames;
    public ValueKind SelectorType => _draft.DataType;
    public bool IgnoreCase => _ignore.IsChecked == true;
    public FrameEditorControl(Node node)
    {
        Spacing = 8; Padding = new(10);
        _draft = new() { Kind = node.Kind, DataType = node.DataType, Contract = node.Contract, Frames = InstrumentSession.CloneFrames(node) };
        var sequence = node.Kind == "sequence";
        var note = LabTheme.Text(sequence
            ? "Frames execute left to right. A sequence local has one writer; only later frames can read it. Moving a frame preserves its diagram and can expose an invalid dependency."
            : "Only the selected case executes. Separate labels with commas. Numeric ranges are inclusive; string ranges exclude the upper endpoint. Quote string labels containing commas. A Default case handles other values.", 12, "#555555");
        note.TextWrapping = TextWrapping.Wrap; Children.Add(note);
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        _type = new("Selector: " + SelectorType, () =>
        {
            _draft.DataType = SelectorKinds[(Array.IndexOf(SelectorKinds, _draft.DataType) + 1) % SelectorKinds.Length];
            _type!.Content = "Selector: " + SelectorType;
        }, flat: false);
        _ignore.IsChecked = node.Parameter("caseInsensitive", 0) != 0;
        if (!sequence) { header.Children.Add(_type); header.Children.Add(_ignore); Children.Add(header); }
        var add = new LabButton(sequence ? "Add frame" : "Add case", () =>
        {
            if (_draft.Frames.Count >= 64) return;
            _draft.Frames.Add(new() { Label = sequence ? _draft.Frames.Count.ToString() : _draft.DataType == ValueKind.String ? "\"case" + _draft.Frames.Count + "\"" : _draft.Frames.Count.ToString() });
            Rebuild();
        }, flat: false);
        Fields["frames-type"] = _type; Fields["frames-ignore"] = _ignore; Fields["frames-add"] = add;
        Children.Add(new ScrollViewer { Content = _rows, MaxHeight = 360, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }); Children.Add(add); Rebuild();
    }
    private void Rebuild()
    {
        _rows.Children.Clear();
        foreach (var key in Fields.Keys.Where(k => k.StartsWith("frame-", StringComparison.Ordinal)).ToArray()) Fields.Remove(key);
        for (var i = 0; i < _draft.Frames.Count; i++)
        {
            var index = i; var frame = _draft.Frames[i];
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
            row.Children.Add(new TextBlock { Text = i.ToString(), Width = 24, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            var label = new LabTextBox(frame.Label, "Frame selector or label") { Width = 265 };
            label.RegisterPropertyChangedCallback(TextBox.TextProperty, (_, _) => frame.Label = label.Text);
            var fallback = new CheckBox { Content = "Default", IsChecked = frame.IsDefault, FontSize = 12, IsEnabled = _draft.Kind != "sequence", MinWidth = 92 };
            fallback.Click += (_, _) => { foreach (var f in _draft.Frames) f.IsDefault = f == frame && fallback.IsChecked == true; Rebuild(); };
            var up = new LabButton("↑", () => Move(index, -1)) { IsEnabled = index > 0 };
            var down = new LabButton("↓", () => Move(index, 1)) { IsEnabled = index < _draft.Frames.Count - 1 };
            var copy = new LabButton("Duplicate", () =>
            {
                if (_draft.Frames.Count >= 64) return;
                var clone = InstrumentSession.CloneFrames(_draft)[index]; clone.Id = Guid.NewGuid().ToString("N"); clone.IsDefault = false;
                _draft.Frames.Insert(index + 1, clone); Rebuild();
            });
            var remove = new LabButton("×", () => { _draft.Frames.RemoveAt(index); Rebuild(); }) { IsEnabled = _draft.Frames.Count > 1 };
            foreach (var element in new UIElement[] { label, fallback, up, down, copy, remove }) row.Children.Add(element);
            Fields["frame-label-" + i] = label; Fields["frame-default-" + i] = fallback; Fields["frame-up-" + i] = up; Fields["frame-down-" + i] = down; Fields["frame-copy-" + i] = copy; Fields["frame-remove-" + i] = remove;
            _rows.Children.Add(row);
        }
    }
    private void Move(int index, int delta)
    {
        var next = index + delta; if (next < 0 || next >= _draft.Frames.Count) return;
        (_draft.Frames[index], _draft.Frames[next]) = (_draft.Frames[next], _draft.Frames[index]); Rebuild();
    }
}
