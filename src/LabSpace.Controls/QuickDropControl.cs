using LabSpace.Core;
using Microsoft.UI.Xaml.Automation;

namespace LabSpace.Controls;

public sealed record QuickDropEntry(string Kind, string Title, string Category, string? Widget = null);

/// <summary>Keyboard-first function/control discovery. Selection requests placement; this control never mutates the document.</summary>
public sealed class QuickDropControl : Grid
{
    private readonly StackPanel _items = new() { Spacing = 1 };
    private readonly ScrollViewer _scroll;
    private readonly List<LabButton> _buttons = [];
    private QuickDropEntry[] _matches = [];
    private int _selected;
    private bool _controls;
    public LabTextBox Query { get; } = new("", "Quick Drop search");
    public event Action<QuickDropEntry>? Chosen;
    public event Action? Dismissed;
    public IReadOnlyList<QuickDropEntry> Matches => _matches;
    public QuickDropControl()
    {
        RowDefinitions.Add(new() { Height = GridLength.Auto }); RowDefinitions.Add(new() { Height = new(260) }); RowDefinitions.Add(new() { Height = GridLength.Auto });
        Query.PlaceholderText = "Type a function or control name…"; Query.Margin = new(7); AutomationProperties.SetAutomationId(Query, "quick-drop-query"); Children.Add(Query);
        _scroll = new() { Content = _items, Margin = new Thickness(7, 0, 7, 0), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; SetRow(_scroll, 1); Children.Add(_scroll);
        var hint = LabTheme.Text("↑ ↓ choose   ·   Enter places on cursor   ·   Esc cancels", 11, "#606060"); hint.Margin = new(9); SetRow(hint, 2); Children.Add(hint);
        Query.TextChanged += (_, _) => Refresh();
        Query.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Escape) Dismissed?.Invoke();
            else if (e.Key == VirtualKey.Enter) { if (_matches.Length > 0) Chosen?.Invoke(_matches[_selected]); }
            else if (e.Key is VirtualKey.Up or VirtualKey.Down)
            {
                _selected = Math.Clamp(_selected + (e.Key == VirtualKey.Up ? -1 : 1), 0, Math.Max(0, _matches.Length - 1)); Highlight();
                _scroll.ChangeView(null, Math.Max(0, (_selected - 4) * 29), null);
            }
            else return;
            e.Handled = true;
        };
    }
    public void Open(bool controls) { _controls = controls; Query.Text = ""; Refresh(); Query.Focus(FocusState.Programmatic); }
    private IEnumerable<QuickDropEntry> Entries()
    {
        if (!_controls) return NodeCatalog.All.Select(d => new QuickDropEntry(d.Kind, d.Title, d.Category));
        return [new("control", "Numeric control", "Numeric", "Numeric"), new("control", "Knob", "Numeric", "Knob"), new("control", "Slider", "Numeric", "Slider"), new("indicator", "Numeric indicator", "Numeric", "Numeric"), new("indicator", "Gauge", "Numeric", "Gauge"), new("bool-control", "Push button", "Boolean", "Switch"), new("bool-indicator", "Round LED", "Boolean", "LED"), new("graph", "Waveform graph", "Graphs", "Graph"), new("chart", "Waveform chart", "Graphs", "Chart"), new("string-control", "String control", "String", "String"), new("string-indicator", "String indicator", "String", "String"), new("array-indicator", "Array indicator", "Array", "Array")];
    }
    private void Refresh()
    {
        var terms = Query.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        _matches = Entries().Where(d => terms.All(t => (d.Title + " " + d.Kind + " " + d.Category).Contains(t, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(d => d.Title.Equals(Query.Text.Trim(), StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(d => d.Title).Take(50).ToArray();
        _items.Children.Clear(); _buttons.Clear(); _selected = 0;
        foreach (var entry in _matches)
        {
            var button = new LabButton(entry.Title, () => Chosen?.Invoke(entry)) { HorizontalContentAlignment = HorizontalAlignment.Stretch, Height = 29 };
            var row = new Grid(); var category = LabTheme.Text(entry.Category, 10, "#6C6C6C"); category.HorizontalAlignment = HorizontalAlignment.Right;
            var name = LabTheme.Text(entry.Title, 12); name.Margin = new(0, 0, 105, 0); row.Children.Add(name); row.Children.Add(category); button.Content = row;
            _buttons.Add(button); _items.Children.Add(button);
        }
        if (_matches.Length == 0) _items.Children.Add(LabTheme.Text("No matching functions or controls.", 12, "#777777"));
        Highlight();
    }
    private void Highlight() { for (var i = 0; i < _buttons.Count; i++) _buttons[i].SetActive(i == _selected); }
}
