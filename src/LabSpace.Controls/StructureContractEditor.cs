using System.Collections.Immutable;
using System.Globalization;
using LabSpace.Core;

namespace LabSpace.Controls;

/// <summary>Staged connector editor. ReadContract returns a new immutable contract; canceling does not alter model objects.</summary>
public sealed class StructureContractEditor : ScrollViewer
{
    private sealed record InputRow(LabTextBox Name, ComboBox Type, CheckBox Indexing, CheckBox Required);
    private sealed record OutputRow(LabTextBox Name, ComboBox Type, ComboBox Mode, CheckBox Default, LabTextBox Condition);
    private sealed record RegisterRow(LabTextBox Name, ComboBox Type, CheckBox Initialized, LabTextBox Depth);
    private readonly StackPanel _root = new() { Spacing = 8, Padding = new Thickness(10) };
    private readonly StackPanel _inputs = new() { Spacing = 5 }, _outputs = new() { Spacing = 5 }, _registers = new() { Spacing = 5 };
    private readonly List<InputRow> _inputRows = [];
    private readonly List<OutputRow> _outputRows = [];
    private readonly List<RegisterRow> _registerRows = [];
    private readonly LabTextBox _primary;
    private readonly CheckBox _conditional = new() { Content = "Enable conditional For terminal", FontSize = 12 };
    private readonly CheckBox _continue = new() { Content = "Continue while condition is TRUE", FontSize = 12 };
    private readonly bool _loop;
    private readonly StructureContract _draft;
    public Dictionary<string, FrameworkElement> Fields { get; } = [];
    public StructureContractEditor(string kind, StructureContract draft)
    {
        _draft = draft; Content = _root; MaxHeight = 490; HorizontalScrollBarVisibility = ScrollBarVisibility.Auto; _loop = kind is "for" or "while";
        var note = LabTheme.Text("Declare the structure interface, then wire its connector nodes inside the body. Removing or renaming a terminal removes its attached wires; Undo restores the complete edit.", 12, "#555555"); note.TextWrapping = TextWrapping.Wrap; _root.Children.Add(note);
        _primary = new(draft.PrimaryOutput, "Primary output"); Fields["primary"] = _primary;
        _root.Children.Add(Row(LabTheme.Text("Primary output", 12), _primary));
        _root.Children.Add(LabTheme.Text("Input tunnels — name / type / indexing / required", 13)); _root.Children.Add(_inputs);
        foreach (var t in draft.Inputs) AddInput(t);
        var addIn = new LabButton("Add input tunnel", () => AddInput(new() { Name = "input" + (_inputRows.Count + 1) }), flat: false); Fields["add-input"] = addIn; _root.Children.Add(addIn);
        _root.Children.Add(LabTheme.Text("Output tunnels — name / type / collection / unwired default / condition name", 13)); _root.Children.Add(_outputs);
        foreach (var t in draft.Outputs) AddOutput(t);
        var addOut = new LabButton("Add output tunnel", () => AddOutput(new() { Name = "output" + (_outputRows.Count + 1) }), flat: false); Fields["add-output"] = addOut; _root.Children.Add(addOut);
        if (_loop)
        {
            _root.Children.Add(LabTheme.Text("Shift registers — name / type / initialized / history depth", 13)); _root.Children.Add(_registers);
            foreach (var r in draft.Registers) AddRegister(r);
            var add = new LabButton("Add shift register", () => AddRegister(new() { Name = "state" + (_registerRows.Count + 1) }), flat: false); Fields["add-register"] = add; _root.Children.Add(add);
            _conditional.IsChecked = draft.ConditionalFor; _conditional.Visibility = kind == "for" ? Visibility.Visible : Visibility.Collapsed;
            _continue.IsChecked = draft.ContinueWhenTrue; Fields["conditional"] = _conditional; Fields["continue"] = _continue;
            _root.Children.Add(_conditional); _root.Children.Add(_continue);
        }
    }
    private static StackPanel Row(params UIElement[] items) { var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 }; foreach (var item in items) row.Children.Add(item); return row; }
    private static ComboBox Choice<T>(T value) where T : struct, Enum => new() { ItemsSource = Enum.GetNames<T>(), SelectedItem = value.ToString(), MinWidth = 100, FontSize = 12, MinHeight = 28 };
    private static T Selected<T>(ComboBox box) where T : struct, Enum => Enum.Parse<T>((string)box.SelectedItem);
    private static LabTextBox NameBox(string name) => new(name, "Terminal name") { Width = 112 };
    private void AddInput(InputTunnel value)
    {
        if (_inputRows.Count >= 32) return;
        var r = new InputRow(NameBox(value.Name), Choice(value.Type), new() { IsChecked = value.Indexing, Content = "Index", IsEnabled = _loop, FontSize = 12 }, new() { IsChecked = value.Required, Content = "Required", FontSize = 12 });
        StackPanel? row = null; var remove = new LabButton("×", () => { _inputRows.Remove(r); _inputs.Children.Remove(row!); }); row = Row(r.Name, r.Type, r.Indexing, r.Required, remove);
        var index = _inputRows.Count; Fields["input-name-" + index] = r.Name; Fields["input-index-" + index] = r.Indexing;
        _inputRows.Add(r); _inputs.Children.Add(row);
    }
    private void AddOutput(OutputTunnel value)
    {
        if (_outputRows.Count >= 32) return;
        var r = new OutputRow(NameBox(value.Name), Choice(value.Type), Choice(value.Mode), new() { IsChecked = value.UseDefaultIfUnwired, Content = "Default", FontSize = 12 }, NameBox(value.Condition)); r.Mode.IsEnabled = _loop;
        StackPanel? row = null; var remove = new LabButton("×", () => { _outputRows.Remove(r); _outputs.Children.Remove(row!); }); row = Row(r.Name, r.Type, r.Mode, r.Default, r.Condition, remove);
        var index = _outputRows.Count; Fields["output-name-" + index] = r.Name; Fields["output-mode-" + index] = r.Mode;
        _outputRows.Add(r); _outputs.Children.Add(row);
    }
    private void AddRegister(ShiftRegister value)
    {
        if (_registerRows.Count >= 16) return;
        var r = new RegisterRow(NameBox(value.Name), Choice(value.Type), new() { IsChecked = value.Initialized, Content = "Initialized", FontSize = 12 }, new(value.HistoryDepth.ToString(CultureInfo.InvariantCulture), "History depth") { Width = 54 });
        StackPanel? row = null; var remove = new LabButton("×", () => { _registerRows.Remove(r); _registers.Children.Remove(row!); }); row = Row(r.Name, r.Type, r.Initialized, r.Depth, remove);
        var index = _registerRows.Count; Fields["register-name-" + index] = r.Name; Fields["register-initialized-" + index] = r.Initialized; Fields["register-depth-" + index] = r.Depth;
        _registerRows.Add(r); _registers.Children.Add(row);
    }
    public StructureContract ReadContract() => _draft with
    {
        PrimaryOutput = _primary.Text.Trim(), ConditionalFor = _conditional.IsChecked == true, ContinueWhenTrue = _continue.IsChecked == true,
        Inputs = _inputRows.Select(r => new InputTunnel { Name = r.Name.Text.Trim(), Type = Selected<ValueKind>(r.Type), Indexing = r.Indexing.IsChecked == true, Required = r.Required.IsChecked == true }).ToImmutableArray(),
        Outputs = _outputRows.Select(r => new OutputTunnel { Name = r.Name.Text.Trim(), Type = Selected<ValueKind>(r.Type), Mode = Selected<TunnelMode>(r.Mode), UseDefaultIfUnwired = r.Default.IsChecked == true, Condition = r.Condition.Text.Trim() }).ToImmutableArray(),
        Registers = _registerRows.Select(r => new ShiftRegister { Name = r.Name.Text.Trim(), Type = Selected<ValueKind>(r.Type), Initialized = r.Initialized.IsChecked == true, HistoryDepth = int.TryParse(r.Depth.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var depth) ? depth : throw new ArgumentException("History depth must be an integer from 1 to 16.") }).ToImmutableArray()
    };
}
