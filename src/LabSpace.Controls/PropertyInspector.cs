using System.Globalization;
using LabSpace.Core;
using LabSpace.Editing;

namespace LabSpace.Controls;

public sealed class PropertyInspector : ScrollViewer, IDisposable
{
    private readonly InstrumentSession _session;
    public event Action<Node>? StructureRequested;
    public event Action<Node>? EditorRequested;
    public event Action<Node>? FramesRequested;
    private readonly StackPanel _fields = new() { Padding = new Thickness(10), Spacing = 7 };
    public PropertyInspector(InstrumentSession session)
    {
        _session = session; Content = _fields; HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled; session.Changed += Changed; Rebuild();
    }
    private void Changed(SessionChange change) { if ((change & (SessionChange.Document | SessionChange.Selection | SessionChange.Navigation)) != 0) Rebuild(); }
    private void Apply(Action action) { try { action(); } catch (Exception e) { _session.Message(e.Message); } }
    private void Field(string label, string value, Action<string> commit)
    {
        _fields.Children.Add(LabTheme.Text(label, 11, "#575757")); var box = new LabTextBox(value, label); var original = value;
        box.KeyDown += (_, e) => { if (e.Key == VirtualKey.Enter) { if (box.Text != original) { var text = box.Text; original = text; Apply(() => commit(text)); } e.Handled = true; } };
        box.LostFocus += (_, _) => { if (box.Text != original) { var text = box.Text; original = text; Apply(() => commit(text)); } }; _fields.Children.Add(box);
    }
    private static double Number(string text) { if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value)) throw new ArgumentException("Enter a finite number using a decimal point."); return value; }
    public void Rebuild()
    {
        _fields.Children.Clear(); var node = _session.SelectedNode;
        if (node is null)
        {
            if (_session.SelectedWire is not null)
            {
                var wire = _session.Diagram.Wires.First(w => w.Id == _session.SelectedWire); _fields.Children.Add(LabTheme.Text("Wire properties", 14)); _fields.Children.Add(LabTheme.Text("Input: " + wire.Input));
                _fields.Children.Add(new LabButton(wire.Probe ? "Remove probe" : "Attach probe", _session.ToggleProbe, flat: false)); _fields.Children.Add(new LabButton("Delete wire", () => Apply(_session.Delete), flat: false));
            }
            else
            {
                var text = LabTheme.Text("Select a node, wire or front-panel control to inspect its properties. Double-click numeric controls to edit values.", 12, "#6A6A6A"); text.TextWrapping = TextWrapping.Wrap; _fields.Children.Add(text);
                _fields.Children.Add(LabTheme.Text($"{_session.Diagram.Nodes.Count} nodes · {_session.Diagram.Wires.Count} wires", 12));
            }
            return;
        }
        var id = node.Id; var definition = NodeCatalog.Resolve(node); _fields.Children.Add(LabTheme.Text(definition.Title, 16));
        Field("Label", node.Label, text => _session.Edit(() => _session.Find(id)!.Label = text, false));
        if (node.Kind is "constant" or "control" or "bool" or "bool-control" or "feedback") Field("Value", node.Value.ToString("G17", CultureInfo.InvariantCulture), text => _session.SetValue(id, Number(text)));
        if (node.Kind is "string" or "string-control" or "array" or "input" or "output" or "sequence-read" or "sequence-write" or "simulate") Field(node.Kind == "simulate" ? "Waveform (Sine / Square / Triangle)" : "Text", node.Text, text => _session.SetText(id, text));
        if (node.Kind is "input" or "output" or "sequence-read" or "sequence-write")
        {
            var type = new ComboBox { ItemsSource = Enum.GetNames<ValueKind>(), SelectedItem = node.DataType.ToString(), FontSize = 12, MinHeight = 28 };
            type.SelectionChanged += (_, _) => { if (type.SelectedItem is string value && value != node.DataType.ToString()) Apply(() => _session.Edit(() => _session.Find(id)!.DataType = Enum.Parse<ValueKind>(value))); };
            _fields.Children.Add(LabTheme.Text("Connector type", 11)); _fields.Children.Add(type);
        }
        if (node.Kind is "formula" or "error-control" or "error-constant" or "complex" or "complex-control")
            _fields.Children.Add(new LabButton("Edit value or formula…", () => EditorRequested?.Invoke(node), flat: false));
        if (StructureFrames.HasFrames(node))
            _fields.Children.Add(new LabButton("Cases and sequence frames…", () => FramesRequested?.Invoke(node), flat: false));
        foreach (var parameter in node.Parameters.Where(p => p.Key != "caseInsensitive").ToArray())
        {
            var key = parameter.Key; Field(key, parameter.Value.ToString("G17", CultureInfo.InvariantCulture), text => _session.Edit(() => _session.Find(id)!.Parameters[key] = Number(text), false));
        }
        foreach (var port in definition.Inputs.Where(p => !p.Required && (p.Kind is ValueKind.Number or ValueKind.Boolean) && !node.Parameters.ContainsKey(p.Name)))
        {
            if (_session.Diagram.Wires.Any(w => w.To == id && w.Input == port.Name)) continue;
            var key = port.Name; Field("Default " + key, port.Default.ToString(CultureInfo.InvariantCulture), text => _session.Edit(() => _session.Find(id)!.Parameters[key] = Number(text), false));
        }
        if (definition.IsStructure)
        {
            _fields.Children.Add(new LabButton("Tunnels and shift registers…", () => StructureRequested?.Invoke(node), flat: false));
            _fields.Children.Add(new LabButton("Edit " + (node.Kind == "case" ? "TRUE branch" : "body"), () => _session.Enter(id), flat: false));
            if (node.Kind == "case") _fields.Children.Add(new LabButton("Edit FALSE branch", () => _session.Enter(id, true), flat: false));
        }
        _fields.Children.Add(new LabButton(node.Breakpoint ? "Remove breakpoint" : "Set breakpoint", _session.ToggleBreakpoint, flat: false));
        var panel = _session.Path.Count == 0 ? _session.Instrument.Panel.FirstOrDefault(p => p.NodeId == id) : null;
        if (panel is not null)
        {
            _fields.Children.Add(LabTheme.Separator(false)); _fields.Children.Add(LabTheme.Text("Front-panel appearance", 13));
            var choices = definition.Output switch { ValueKind.Number => new[] { "Numeric", "Knob", "Slider", "Gauge" }, ValueKind.Boolean => new[] { "Switch", "LED" }, ValueKind.String => new[] { "String" }, ValueKind.Array => new[] { "Array" }, ValueKind.Error => new[] { "Error" }, ValueKind.Complex => new[] { "Complex" }, _ => new[] { "Graph", "Chart" } };
            var combo = new ComboBox { ItemsSource = choices, SelectedItem = panel.Widget, HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 28, FontSize = 12 };
            combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is string widget && widget != panel.Widget) Apply(() => _session.Edit(() => { panel.Widget = widget; if (widget is "Knob" or "Gauge") panel.Bounds = panel.Bounds with { Height = Math.Max(180, panel.Bounds.Height) }; }, false)); }; _fields.Children.Add(combo);
            Field("Minimum", panel.Minimum.ToString(CultureInfo.InvariantCulture), text => _session.Edit(() => panel.Minimum = Number(text), false));
            Field("Maximum", panel.Maximum.ToString(CultureInfo.InvariantCulture), text => _session.Edit(() => panel.Maximum = Number(text), false));
            Field("Width", panel.Bounds.Width.ToString(CultureInfo.InvariantCulture), text => _session.Edit(() => panel.Bounds = panel.Bounds with { Width = Number(text) }, false));
            Field("Height", panel.Bounds.Height.ToString(CultureInfo.InvariantCulture), text => _session.Edit(() => panel.Bounds = panel.Bounds with { Height = Number(text) }, false));
        }
    }
    public new void Dispose() => _session.Changed -= Changed;
}
