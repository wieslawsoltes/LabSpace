using LabSpace.Core;
using Microsoft.UI.Xaml.Automation;

namespace LabSpace.Controls;

public sealed record QuickDropEntry(string Kind, string Title, string Category, string? Widget = null);

/// <summary>Keyboard-first discovery. Query state is synchronous; visual result rebuilding is coalesced and never determines which command Enter executes.</summary>
public sealed class QuickDropControl : Grid
{
    private readonly StackPanel _items = new() { Spacing = 1 };
    private readonly ScrollViewer _scroll;
    private readonly List<LabButton> _buttons = [];
    private QuickDropEntry[] _entries = [], _matches = [];
    private string? _matchedQuery;
    private int _selected;
    private bool _controls, _renderQueued;
    public LabTextBox Query { get; } = new("", "Quick Drop search");
    public event Action<QuickDropEntry>? Chosen;
    public event Action? Dismissed;
    public IReadOnlyList<QuickDropEntry> Matches => _matches;

    public QuickDropControl()
    {
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        RowDefinitions.Add(new() { Height = new(260) });
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        Query.PlaceholderText = "Type a function or control name…";
        Query.Margin = new(7);
        AutomationProperties.SetAutomationId(Query, "quick-drop-query");
        Children.Add(Query);
        _scroll = new() { Content = _items, Margin = new Thickness(7, 0, 7, 0), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        SetRow(_scroll, 1); Children.Add(_scroll);
        var hint = LabTheme.Text("↑ ↓ choose   ·   Enter places on cursor   ·   Esc cancels", 11, "#606060");
        hint.Margin = new(9); SetRow(hint, 2); Children.Add(hint);

        // TextChanged is asynchronous and may follow Enter during fast native input.
        // Observe the dependency property synchronously, but do not mutate the visual
        // tree from its callback (which may execute during layout).
        Query.RegisterPropertyChangedCallback(TextBox.TextProperty, (_, _) =>
        {
            UpdateMatches();
            QueueRender();
        });
        Query.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Escape) Dismissed?.Invoke();
            else if (e.Key == VirtualKey.Enter)
            {
                UpdateMatches(); // Never execute a stale, previously rendered result.
                if (_matches.Length > 0) Chosen?.Invoke(_matches[_selected]);
            }
            else if (e.Key is VirtualKey.Up or VirtualKey.Down)
            {
                UpdateMatches();
                _selected = Math.Clamp(_selected + (e.Key == VirtualKey.Up ? -1 : 1), 0, Math.Max(0, _matches.Length - 1));
                QueueRender();
                _scroll.ChangeView(null, Math.Max(0, (_selected - 4) * 29), null);
            }
            else return;
            e.Handled = true;
        };
    }

    public void Open(bool controls)
    {
        _controls = controls;
        _entries = Entries().ToArray();
        _matchedQuery = null;
        Query.Text = "";
        UpdateMatches();
        RenderResults();
        Query.Focus(FocusState.Programmatic);
    }

    private IEnumerable<QuickDropEntry> Entries()
    {
        if (!_controls) return NodeCatalog.All.Select(d => new QuickDropEntry(d.Kind, d.Title, d.Category));
        return [new("control", "Numeric control", "Numeric", "Numeric"), new("control", "Knob", "Numeric", "Knob"), new("control", "Slider", "Numeric", "Slider"), new("indicator", "Numeric indicator", "Numeric", "Numeric"), new("indicator", "Gauge", "Numeric", "Gauge"), new("bool-control", "Push button", "Boolean", "Switch"), new("bool-indicator", "Round LED", "Boolean", "LED"), new("graph", "Waveform graph", "Graphs", "Graph"), new("chart", "Waveform chart", "Graphs", "Chart"), new("string-control", "String control", "String", "String"), new("string-indicator", "String indicator", "String", "String"), new("array-indicator", "Array indicator", "Array", "Array"), new("error-control", "Error cluster control", "Clusters", "Error"), new("error-indicator", "Error cluster indicator", "Clusters", "Error"), new("complex-control", "Complex numeric control", "Numeric", "Complex"), new("complex-indicator", "Complex numeric indicator", "Numeric", "Complex")];
    }

    private void UpdateMatches()
    {
        var query = Query.Text.Trim();
        if (string.Equals(_matchedQuery, query, StringComparison.Ordinal)) return;
        _matchedQuery = query;
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        _matches = _entries.Where(d => terms.All(t => (d.Title + " " + d.Kind + " " + d.Category).Contains(t, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(d => d.Title.Equals(query, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(d => d.Title, StringComparer.OrdinalIgnoreCase).Take(50).ToArray();
        _selected = 0;
    }

    private void QueueRender()
    {
        if (_renderQueued) return;
        _renderQueued = true;
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            _renderQueued = false;
            RenderResults();
        })) _renderQueued = false;
    }

    private void RenderResults()
    {
        _items.Children.Clear(); _buttons.Clear();
        foreach (var entry in _matches)
        {
            var button = new LabButton(entry.Title, () =>
            {
                UpdateMatches();
                if (_matches.Contains(entry)) Chosen?.Invoke(entry);
            }) { HorizontalContentAlignment = HorizontalAlignment.Stretch, Height = 29 };
            var row = new Grid();
            var category = LabTheme.Text(entry.Category, 10, "#6C6C6C");
            category.HorizontalAlignment = HorizontalAlignment.Right;
            var name = LabTheme.Text(entry.Title, 12); name.Margin = new(0, 0, 105, 0);
            row.Children.Add(name); row.Children.Add(category); button.Content = row;
            button.SetActive(_buttons.Count == _selected);
            _buttons.Add(button); _items.Children.Add(button);
        }
        if (_matches.Length == 0) _items.Children.Add(LabTheme.Text("No matching functions or controls.", 12, "#777777"));
    }
}
