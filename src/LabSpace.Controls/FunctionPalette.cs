using LabSpace.Core;
using LabSpace.Skia;
using Microsoft.UI.Xaml.Automation;

namespace LabSpace.Controls;

public sealed class FunctionPalette : Grid
{
    private readonly StackPanel _items = new() { Spacing = 5, Padding = new Thickness(7) };
    private readonly LabTextBox _search = new("", "Search functions");
    private bool _controls;
    public Dictionary<string, FrameworkElement> Entries { get; } = [];
    public event Action<string, string?>? AddRequested;
    public FunctionPalette()
    {
        RowDefinitions.Add(new() { Height = GridLength.Auto }); RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        _search.PlaceholderText = "Search palette…"; _search.Margin = new(6); _search.TextChanged += (_, _) => Rebuild(); Children.Add(_search);
        var scroll = new ScrollViewer { Content = _items, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetRow(scroll, 1); Children.Add(scroll); Rebuild();
    }
    public void SetControls(bool controls) { if (_controls == controls) return; _controls = controls; _search.PlaceholderText = controls ? "Search controls…" : "Search functions…"; Rebuild(); }
    public void FocusSearch() => _search.Focus(FocusState.Programmatic);
    private void Rebuild()
    {
        Entries.Clear(); _items.Children.Clear(); var query = _search.Text.Trim();
        var entries = new List<(string Category, string Kind, string Widget, string Title, string Glyph)>();
        if (_controls)
        {
            entries.AddRange([
                ("Numeric", "control", "Numeric", "Numeric", "DBL"), ("Numeric", "control", "Knob", "Knob", "◉"), ("Numeric", "control", "Slider", "Slider", "↔"),
                ("Numeric", "indicator", "Numeric", "Indicator", "123"), ("Numeric", "indicator", "Gauge", "Gauge", "∩"),
                ("Boolean", "bool-control", "Switch", "Push button", "T/F"), ("Boolean", "bool-indicator", "LED", "Round LED", "●"),
                ("Graphs", "graph", "Graph", "Waveform graph", "~"), ("Graphs", "chart", "Chart", "Waveform chart", "~+"),
                ("String & Array", "string-control", "String", "String control", "abc"), ("String & Array", "string-indicator", "String", "String indicator", "abc"), ("String & Array", "array-indicator", "Array", "Array", "[ ]")
            ]);
        }
        else entries.AddRange(NodeCatalog.All.Select(d => (d.Category, d.Kind, "", d.Title, d.Glyph)));
        foreach (var group in entries.Where(x => query.Length == 0 || x.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || x.Category.Contains(query, StringComparison.OrdinalIgnoreCase)).GroupBy(x => x.Category))
        {
            var caption = LabTheme.Text(group.Key, 12, "#4E4E4E"); caption.Margin = new(1, 5, 0, 1); _items.Children.Add(caption);
            var grid = new Grid { ColumnSpacing = 3, RowSpacing = 3 }; for (var col = 0; col < 3; col++) grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); var index = 0;
            foreach (var item in group)
            {
                if (index % 3 == 0) grid.RowDefinitions.Add(new() { Height = new(70) });
                var local = item; var button = new LabButton(local.Title, () => AddRequested?.Invoke(local.Kind, string.IsNullOrEmpty(local.Widget) ? null : local.Widget)); button.Padding = new(2); button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                var content = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var glyph = LabTheme.Text(local.Glyph, local.Glyph.Length > 3 ? 12 : 19, "#424242"); glyph.HorizontalAlignment = HorizontalAlignment.Center;
                content.Children.Add(new Border { Child = glyph, Width = 34, Height = 28, Background = LabTheme.Brush(_controls ? "#DDDDDD" : "#FFF5CA"), BorderBrush = LabTheme.Brush("#A5A28A"), BorderThickness = new(1), Padding = new(2) });
                var label = LabTheme.Text(local.Title, 10); label.TextWrapping = TextWrapping.Wrap; label.TextAlignment = TextAlignment.Center; label.MaxLines = 2; content.Children.Add(label); button.Content = content;
                AutomationProperties.SetAutomationId(button, "palette-" + local.Kind + "-" + local.Widget); Entries[local.Kind + (local.Widget.Length == 0 ? "" : ":" + local.Widget)] = button;
                Grid.SetRow(button, index / 3); Grid.SetColumn(button, index % 3); grid.Children.Add(button); index++;
            }
            _items.Children.Add(grid);
        }
        if (_items.Children.Count == 0) _items.Children.Add(LabTheme.Text("No matching functions.", 12, "#777777"));
    }
}
