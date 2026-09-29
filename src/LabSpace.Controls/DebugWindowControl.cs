using System.Globalization;
using LabSpace.Core;
using LabSpace.Editing;
using Microsoft.UI.Xaml.Automation;

namespace LabSpace.Controls;

/// <summary>Reusable, modeless, cross-VI probe table. Refresh is explicit so hidden panes perform no layout work.</summary>
public sealed class DebugWindowControl : UserControl
{
    private sealed record Row(Grid Visual, TextBlock Number, TextBlock Location, TextBlock Source,
        TextBlock Type, TextBlock Value, TextBlock State, LabButton Locate, LabButton Remove);
    private readonly InstrumentSession _session;
    private readonly StackPanel _rows = new() { Spacing = 0 };
    private readonly Dictionary<DebugAddress, Row> _presenters = [];
    private readonly Dictionary<string, FrameworkElement> _fields = [];
    private readonly LabTextBox _filter = new("", "Filter probes") { Width = 190, PlaceholderText = "Filter VI, diagram or terminal" };
    private readonly CheckBox _allow = new() { Content = "Allow debugging", MinHeight = 27, FontSize = 12 };
    private readonly CheckBox _retain = new() { Content = "Retain wire values", MinHeight = 27, FontSize = 12 };
    private readonly TextBlock _summary = LabTheme.Text("", 11, "#555555");
    private bool _updating;
    public IReadOnlyDictionary<string, FrameworkElement> Fields => _fields;
    public int RefreshCount { get; private set; }
    public event Action? ProbeLocated;
    public DebugWindowControl(InstrumentSession session)
    {
        _session = session; FontFamily = LabTheme.Font; FontSize = 12;
        var root = new Grid { Background = LabTheme.Brush("#EFEFEF") };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new(25) });
        root.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = new(22) });
        var commands = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new(7, 2, 7, 2) };
        _allow.IsChecked = session.DebuggingEnabled; _retain.IsChecked = session.RetainWireValues;
        _allow.Click += (_, _) => { if (!_updating) session.SetDebuggingEnabled(_allow.IsChecked == true); };
        _retain.Click += (_, _) => { if (!_updating) session.SetRetainWireValues(_retain.IsChecked == true); };
        _filter.TextChanged += (_, _) => Refresh();
        var clear = new LabButton("Clear values", session.ClearDebuggerValues, flat: false);
        commands.Children.Add(_allow); commands.Children.Add(_retain); commands.Children.Add(clear); commands.Children.Add(_filter);
        root.Children.Add(new ScrollViewer { Content = commands, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
        Track("debug-allow", _allow); Track("debug-retain", _retain); Track("debug-clear", clear); Track("debug-filter", _filter);
        var heading = Table(); heading.Background = LabTheme.Brush("#DADADA");
        var headings = new[] { "#", "VI / Diagram", "Source terminal", "Type", "Value", "State", "", "" };
        for (var i = 0; i < headings.Length; i++) Cell(heading, LabTheme.Text(headings[i], 11), i);
        Grid.SetRow(heading, 1); root.Children.Add(heading);
        var scroll = new ScrollViewer { Content = _rows, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 2); root.Children.Add(scroll);
        _summary.Margin = new(7, 0, 7, 0); Grid.SetRow(_summary, 3); root.Children.Add(_summary); Content = root;
    }
    private void Track(string key, FrameworkElement element)
    { _fields[key] = element; AutomationProperties.SetAutomationId(element, key); }
    private static Grid Table()
    {
        var grid = new Grid { MinHeight = 28, MinWidth = 690, BorderThickness = new(0, 0, 0, 1), BorderBrush = LabTheme.Brush("#D6D6D6") };
        foreach (var width in new[] { 28, 170, 160, 65, -1, 65, 58, 32 })
            grid.ColumnDefinitions.Add(new() { Width = width < 0 ? new(1, GridUnitType.Star) : new(width) });
        return grid;
    }
    private static void Cell(Grid row, FrameworkElement element, int column)
    { element.Margin = new(5, 0, 3, 0); Grid.SetColumn(element, column); row.Children.Add(element); }
    private Row Create(ProbeSnapshot probe)
    {
        var grid = Table(); grid.Background = LabTheme.Brush("#FFFFFF");
        TextBlock Text() => new() { FontFamily = LabTheme.Font, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var number = Text(); var location = Text(); var source = Text(); var type = Text(); var value = Text(); var state = Text();
        var address = probe.Address;
        var locate = new LabButton("Locate", () => { _session.LocateProbe(address); ProbeLocated?.Invoke(); });
        var remove = new LabButton("×", () => { _session.RemoveProbe(address); Refresh(); });
        var cells = new FrameworkElement[] { number, location, source, type, value, state, locate, remove };
        for (var i = 0; i < cells.Length; i++) Cell(grid, cells[i], i);
        ToolTipService.SetToolTip(location, probe.Diagram); ToolTipService.SetToolTip(source, probe.Source + " → " + probe.Target);
        return new(grid, number, location, source, type, value, state, locate, remove);
    }
    public void Refresh()
    {
        RefreshCount++; _updating = true;
        try { _allow.IsChecked = _session.DebuggingEnabled; _retain.IsChecked = _session.RetainWireValues; }
        finally { _updating = false; }
        var all = _session.GetProbes(); var query = _filter.Text.Trim();
        var filtered = all.Where(p => query.Length == 0 || (p.Instrument + " " + p.Diagram + " " + p.Source + " " + p.Target)
            .Contains(query, StringComparison.OrdinalIgnoreCase)).Take(128).ToArray();
        var keys = filtered.Select(p => p.Address).ToHashSet();
        foreach (var key in _presenters.Keys.Where(key => !keys.Contains(key)).ToArray())
        { _rows.Children.Remove(_presenters[key].Visual); _presenters.Remove(key); }
        foreach (var key in _fields.Keys.Where(k => k.StartsWith("debug-locate-", StringComparison.Ordinal) || k.StartsWith("debug-remove-", StringComparison.Ordinal)).ToArray()) _fields.Remove(key);
        for (var i = 0; i < filtered.Length; i++)
        {
            var probe = filtered[i];
            if (!_presenters.TryGetValue(probe.Address, out var row))
            { row = Create(probe); _presenters.Add(probe.Address, row); _rows.Children.Insert(i, row.Visual); }
            else if (_rows.Children.IndexOf(row.Visual) != i) { _rows.Children.Remove(row.Visual); _rows.Children.Insert(i, row.Visual); }
            row.Number.Text = (i + 1).ToString(CultureInfo.InvariantCulture); row.Location.Text = probe.Instrument;
            row.Source.Text = probe.Source; row.Type.Text = probe.Type.ToString(); row.Value.Text = Format(probe.Value);
            row.State.Text = probe.Live ? "Live" : probe.Retained ? "Retained" : "No value";
            Track("debug-locate-" + i, row.Locate); Track("debug-remove-" + i, row.Remove);
        }
        _summary.Text = $"{all.Count} probes in loaded VIs · {filtered.Length} shown · {_session.RetainedDebugBytes / 1024:N0} KiB retained / 16 MiB limit"
            + (all.Count == 0 ? " · Select a wire and choose Probe." : "");
    }
    public static string Format(Value? value)
    {
        if (value is null) return "Not executed / value not retained";
        if (value.Kind == ValueKind.String) return Clip(value.Text);
        if (value.Kind == ValueKind.Error) return (value.Error.Status ? "Error " : value.Error.Code == 0 ? "No error " : "Warning ") + value.Error.Code + ": " + Clip(value.Error.Source);
        if (value.Kind is ValueKind.Array or ValueKind.Waveform)
            return "[" + string.Join(", ", value.Samples.Take(8).Select(v => v.ToString("G6", CultureInfo.InvariantCulture))) + (value.Samples.Length > 8 ? ", …" : "") + $"] ({value.Samples.Length})";
        return value.ToString();
    }
    private static string Clip(string value) => value.Length > 160 ? value[..160] + "…" : value;
}
