using System.Globalization;
using LabSpace.Core;
using LabSpace.Editing;

namespace LabSpace.Controls;

public sealed class PropertyInspector : ScrollViewer, IDisposable
{
    private readonly InstrumentSession _session;
    private readonly StackPanel _fields = new() { Padding = new Thickness(10), Spacing = 6 };
    private bool _rebuilding;
    public event Action<Node>? TypeRequested;
    public event Action<Node>? StructureRequested;
    public PropertyInspector(InstrumentSession session)
    {
        _session = session; Content = _fields; HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        session.Changed += Changed; Rebuild();
    }
    private void Changed(SessionChange change) { if ((change & (SessionChange.Document | SessionChange.Selection | SessionChange.Navigation)) != 0) Rebuild(); }
    private void Apply(Action action) { try { action(); } catch (Exception e) { _session.Message(e.Message); } }
    private void Field(string label, string value, Action<string> commit)
    {
        _fields.Children.Add(LabTheme.Text(label, 11, "#575757")); var box = new LabTextBox(value, label); var original = value;
        void Commit() { if (_rebuilding || box.Text == original) return; var text = box.Text; original = text; Apply(() => commit(text)); }
        box.KeyDown += (_, e) => { if (e.Key == VirtualKey.Enter) { Commit(); e.Handled = true; } };
        box.LostFocus += (_, _) => Commit(); _fields.Children.Add(box);
    }
    private static double Number(string text)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value)) throw new ArgumentException("Enter a finite number using a decimal point.");
        return value;
    }
    public void Rebuild()
    {
        if (_rebuilding) return; _rebuilding = true;
        try { Build(); } finally { _rebuilding = false; }
    }
    private void Build()
    {
        _fields.Children.Clear(); var node = _session.SelectedNode;
        if (node is null)
        {
            if (_session.SelectedWire is { } wireId && _session.Diagram.Wires.FirstOrDefault(w => w.Id == wireId) is { } wire)
            {
                _fields.Children.Add(LabTheme.Text("Wire properties", 14));
                _fields.Children.Add(LabTheme.Text("Output: " + wire.Output)); _fields.Children.Add(LabTheme.Text("Input: " + wire.Input));
                _fields.Children.Add(LabTheme.Text(_session.WireValue(wire)?.ToString() ?? "Not executed", 12));
                _fields.Children.Add(new LabButton(wire.Probe ? "Remove probe" : "Attach probe", _session.ToggleProbe, flat: false));
                _fields.Children.Add(new LabButton("Restore automatic routing", () => Apply(_session.ResetWireRouting), flat: false));
                _fields.Children.Add(new LabButton("Delete wire", () => Apply(_session.Delete), flat: false));
                var hint = LabTheme.Text("Ctrl-click to branch a wire. Select a wire, then drag an interior segment to position it.", 11, "#666666"); hint.TextWrapping = TextWrapping.Wrap; _fields.Children.Add(hint);
            }
            else
            {
                var text = LabTheme.Text("Select a node, wire or front-panel control to inspect its properties. Double-click controls to edit their typed values.", 12, "#6A6A6A"); text.TextWrapping = TextWrapping.Wrap; _fields.Children.Add(text);
                _fields.Children.Add(LabTheme.Text($"{_session.Diagram.Nodes.Count} nodes · {_session.Diagram.Wires.Count} wires", 12));
            }
            return;
        }
        var id = node.Id; var def = NodeCatalog.Describe(node); _fields.Children.Add(LabTheme.Text(def.Title, 15));
        Field("Label", node.Label, text => _session.Edit(() => _session.Find(id)!.Label = text, false));
        if (node.Kind is "constant" or "control" or "bool" or "bool-control" or "feedback") Field("Value", node.Value.ToString("G17", CultureInfo.InvariantCulture), text => _session.SetValue(id, Number(text)));
        if (node.Kind is "string" or "string-control" or "array" or "input" or "simulate" or "array-reshape" or "tunnel-in" or "tunnel-out" or "shift-read" or "shift-write") Field(node.Kind == "simulate" ? "Waveform (Sine / Square / Triangle)" : "Text / terminal name", node.Text, text => _session.SetText(id, text));
        if (node.Type is not null || node.Kind is "bundle" or "unbundle" or "convert" or "to-variant" or "from-variant")
        {
            var typeLabel = LabTheme.Text(TypeSyntax.Format(node.Type ?? def.DataType), 11, "#765F32"); typeLabel.TextWrapping = TextWrapping.Wrap; _fields.Children.Add(typeLabel);
            _fields.Children.Add(new LabButton(node.Kind is "typed-control" or "typed-constant" ? "Edit typed value…" : "Representation…", () => TypeRequested?.Invoke(node), flat: false));
        }
        foreach (var parameter in node.Parameters.ToArray())
        {
            var key = parameter.Key; Field(key, parameter.Value.ToString("G17", CultureInfo.InvariantCulture), text => _session.Edit(() => _session.Find(id)!.Parameters[key] = Number(text), false));
        }
        foreach (var port in def.Inputs.Where(p => !p.Required && !node.Parameters.ContainsKey(p.Name) && (p.DataType.IsNumeric || p.Kind == ValueKind.Boolean) && !p.Name.StartsWith("init:", StringComparison.Ordinal)))
        {
            if (_session.Diagram.Wires.Any(w => w.To == id && w.Input == port.Name)) continue;
            var key = port.Name; Field("Default " + key, port.Default.ToString(CultureInfo.InvariantCulture), text => _session.Edit(() => _session.Find(id)!.Parameters[key] = Number(text), false));
        }
        if (def.IsStructure)
        {
            if (node.Contract is not null) _fields.Children.Add(new LabButton("Configure tunnels / registers…", () => StructureRequested?.Invoke(node), flat: false));
            var isCase = node.Kind is "case" or "case-typed";
            _fields.Children.Add(new LabButton("Edit " + (isCase ? "TRUE branch" : "body"), () => _session.Enter(id), flat: false));
            if (isCase) _fields.Children.Add(new LabButton("Edit FALSE branch", () => _session.Enter(id, true), flat: false));
        }
        _fields.Children.Add(new LabButton(node.Breakpoint ? "Remove breakpoint" : "Set breakpoint", _session.ToggleBreakpoint, flat: false));
        foreach (var port in def.OutputPorts) _fields.Children.Add(LabTheme.Text("→ " + port.Name + ": " + TypeSyntax.Format(port.DataType), 10, "#666666"));
        var panel = _session.Path.Count == 0 ? _session.Instrument.Panel.FirstOrDefault(p => p.NodeId == id) : null;
        if (panel is null) return;
        _fields.Children.Add(LabTheme.Separator(false)); _fields.Children.Add(LabTheme.Text("Front-panel appearance", 13));
        var choices = def.DataType.IsNumeric ? new[] { "Numeric", "Knob", "Slider", "Gauge", "Meter", "Thermometer", "Tank" } : def.Output switch
        {
            ValueKind.Boolean => ["Switch", "LED"], ValueKind.String => ["String"], ValueKind.Array => ["Array"],
            ValueKind.Cluster => ["Cluster"], ValueKind.Error => ["Error"], ValueKind.Enum => ["Enum"], ValueKind.Complex => ["Numeric"], _ => new[] { "Graph", "Chart" }
        };
        var combo = new ComboBox { ItemsSource = choices, SelectedItem = panel.Widget, HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 28, FontSize = 12 };
        combo.SelectionChanged += (_, _) =>
        {
            if (!_rebuilding && combo.SelectedItem is string widget && widget != panel.Widget) Apply(() => _session.Edit(() =>
            { panel.Widget = widget; if (widget is "Knob" or "Gauge" or "Meter" or "Thermometer" or "Tank") panel.Bounds = panel.Bounds with { Height = Math.Max(180, panel.Bounds.Height) }; }, false));
        };
        _fields.Children.Add(combo);
        Field("Minimum", panel.Minimum.ToString(CultureInfo.InvariantCulture), text => _session.Edit(() => panel.Minimum = Number(text), false));
        Field("Maximum", panel.Maximum.ToString(CultureInfo.InvariantCulture), text => _session.Edit(() => panel.Maximum = Number(text), false));
        Field("Width", panel.Bounds.Width.ToString(CultureInfo.InvariantCulture), text => _session.Edit(() => panel.Bounds = panel.Bounds with { Width = Number(text) }, false));
        Field("Height", panel.Bounds.Height.ToString(CultureInfo.InvariantCulture), text => _session.Edit(() => panel.Bounds = panel.Bounds with { Height = Number(text) }, false));
    }
    public new void Dispose() => _session.Changed -= Changed;
}
