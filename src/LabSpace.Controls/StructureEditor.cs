using LabSpace.Core;
using Microsoft.UI.Xaml.Automation;

namespace LabSpace.Controls;

/// <summary>Draft-only editor; all connector and shift-register changes commit as one undoable transaction.</summary>
public sealed class StructureEditor : StackPanel
{
    private sealed record TunnelRow(bool Input, LabTextBox Name, LabTextBox Type, ComboBox Mode, CheckBox Conditional, CheckBox Default);
    private sealed record RegisterRow(LabTextBox Name, LabTextBox Type, CheckBox Initialized, LabTextBox Depth, LabTextBox Initial);
    private readonly List<TunnelRow> _tunnels = [];
    private readonly List<RegisterRow> _registers = [];
    private readonly StackPanel _inputRows = new() { Spacing = 5 }, _outputRows = new() { Spacing = 5 }, _registerRows = new() { Spacing = 5 };
    private readonly CheckBox _stop = new() { Content = "Stop if True (unchecked: continue if True)", FontSize = 12 };
    public StructureEditor(Node node)
    {
        Spacing = 8; MinWidth = 650;
        var description = LabTheme.Text("Tunnel types describe the value INSIDE the structure. Indexing adds an array dimension outside. Names identify terminals across edits.", 12); description.TextWrapping = TextWrapping.Wrap; Children.Add(description);
        var contract = node.Contract ?? throw new ArgumentException("Structure contract is missing.");
        Section("Input tunnels", _inputRows, () => AddTunnel(true, new() { Name = Unique("input") }));
        Section("Output tunnels", _outputRows, () => AddTunnel(false, new() { Name = Unique("output") }));
        Section("Shift registers", _registerRows, () => AddRegister(new() { Name = "state" + (_registers.Count + 1) }));
        foreach (var t in contract.Inputs) AddTunnel(true, t);
        foreach (var t in contract.Outputs) AddTunnel(false, t);
        foreach (var r in contract.Registers) AddRegister(r);
        _stop.IsChecked = contract.StopWhenTrue; _stop.Visibility = node.Kind == "while-loop" ? Visibility.Visible : Visibility.Collapsed; Children.Add(_stop);
        var note = LabTheme.Text("Uninitialized registers retain values between successful calls in this session. Depth selects stacked previous iterations. New terminals are created automatically; wire them in the body before running.", 11, "#666666"); note.TextWrapping = TextWrapping.Wrap; Children.Add(note);
    }
    private string Unique(string prefix) { var i = 1; while (_tunnels.Any(t => t.Name.Text == prefix + i)) i++; return prefix + i; }
    private void Section(string title, StackPanel rows, Action add)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        header.Children.Add(LabTheme.Text(title, 13)); var button = new LabButton("+ Add " + title.ToLowerInvariant(), add, flat: false); AutomationProperties.SetAutomationId(button, "add-" + title.Replace(' ', '-').ToLowerInvariant()); header.Children.Add(button); Children.Add(header); Children.Add(rows);
    }
    private static LabTextBox Box(string value, string label, double width) => new(value, label) { Width = width };
    private static StackPanel Labeled(string label, FrameworkElement control)
    {
        var stack = new StackPanel { Spacing = 2 }; stack.Children.Add(LabTheme.Text(label, 10, "#666666")); stack.Children.Add(control); return stack;
    }
    private void AddTunnel(bool input, StructureTunnel t)
    {
        var name = Box(t.Name, "Tunnel name", 100); var type = Box(TypeSyntax.Format(t.Type), "Tunnel type", 190);
        var mode = new ComboBox { Width = 125, FontSize = 12, MinHeight = 28, ItemsSource = input ? new[] { "LastValue", "Indexing" } : Enum.GetNames<TunnelMode>(), SelectedItem = t.Mode.ToString() };
        var conditional = new CheckBox { Content = "Conditional", IsChecked = t.Conditional, FontSize = 11, Visibility = input ? Visibility.Collapsed : Visibility.Visible };
        var defaultValue = new CheckBox { Content = "Default unwired", IsChecked = t.UseDefaultIfUnwired, FontSize = 11, Visibility = input ? Visibility.Collapsed : Visibility.Visible };
        var row = new TunnelRow(input, name, type, mode, conditional, defaultValue); _tunnels.Add(row);
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        line.Children.Add(Labeled("Name", name)); line.Children.Add(Labeled("Inside type", type)); line.Children.Add(Labeled("Tunnel mode", mode));
        var checks = new StackPanel(); checks.Children.Add(conditional); checks.Children.Add(defaultValue); line.Children.Add(checks);
        var host = input ? _inputRows : _outputRows;
        line.Children.Add(new LabButton("×", () => { _tunnels.Remove(row); host.Children.Remove(line); }, flat: false) { VerticalAlignment = VerticalAlignment.Bottom }); host.Children.Add(line);
    }
    private void AddRegister(ShiftRegister r)
    {
        var name = Box(r.Name, "Register name", 100); var type = Box(TypeSyntax.Format(r.Type), "Register type", 155);
        var depth = Box(r.Depth.ToString(), "Register depth", 45); var initial = Box(r.InitialValue, "Initial value", 160);
        var initialized = new CheckBox { Content = "Initialize", IsChecked = r.Initialized, FontSize = 11 };
        var row = new RegisterRow(name, type, initialized, depth, initial); _registers.Add(row);
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        line.Children.Add(Labeled("Name", name)); line.Children.Add(Labeled("Type", type)); line.Children.Add(Labeled("Depth", depth)); line.Children.Add(Labeled("Initial literal (JSON)", initial)); line.Children.Add(initialized);
        line.Children.Add(new LabButton("×", () => { _registers.Remove(row); _registerRows.Children.Remove(line); }, flat: false)); _registerRows.Children.Add(line);
    }
    public StructureContract ReadContract()
    {
        var contract = new StructureContract { StopWhenTrue = _stop.IsChecked == true };
        foreach (var row in _tunnels)
        {
            var tunnel = new StructureTunnel { Name = row.Name.Text.Trim(), Type = TypeSyntax.Parse(row.Type.Text), Mode = Enum.Parse<TunnelMode>((string)row.Mode.SelectedItem), Conditional = row.Conditional.IsChecked == true, UseDefaultIfUnwired = row.Default.IsChecked == true };
            (row.Input ? contract.Inputs : contract.Outputs).Add(tunnel);
        }
        foreach (var row in _registers)
        {
            if (!int.TryParse(row.Depth.Text, out var depth)) throw new ArgumentException("Register depth must be an integer from 1 to 16.");
            contract.Registers.Add(new() { Name = row.Name.Text.Trim(), Type = TypeSyntax.Parse(row.Type.Text), Depth = depth, Initialized = row.Initialized.IsChecked == true, InitialValue = row.Initial.Text });
        }
        contract.Validate(); return contract;
    }
}
