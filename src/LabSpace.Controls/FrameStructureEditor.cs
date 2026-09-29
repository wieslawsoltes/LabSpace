using System.Collections.Immutable;
using LabSpace.Core;
using LabSpace.Editing;

namespace LabSpace.Controls;

/// <summary>
/// Edits a detached structure draft. No document objects are mutated until the host applies ReadDraft().
/// Frame identities survive reordering; sequence-local bindings use identities, not row indices.
/// </summary>
public sealed class FrameStructureEditor : Grid
{
    private sealed record FrameRow(StructureFrame Frame, LabTextBox Label, CheckBox Default);
    private sealed record LocalRow(LabTextBox Name, ComboBox Type, ComboBox Source, string[] FrameIds);

    private readonly Node _draft;
    private readonly StackPanel _frameRows = new() { Spacing = 5, Padding = new Thickness(10) };
    private readonly StackPanel _localRows = new() { Spacing = 5, Padding = new Thickness(10) };
    private readonly Grid _pages = new();
    private readonly List<FrameRow> _rows = [];
    private readonly List<LocalRow> _locals = [];
    private readonly ComboBox _selector;
    private readonly CheckBox _caseInsensitive = new() { Content = "Case-insensitive string matching", FontSize = 12 };
    private readonly TextBlock _error = LabTheme.Text("", 12, "#A32820");
    private readonly Dictionary<string, FrameworkElement> _fields = [];
    private readonly StructureContractEditor _terminals;
    private readonly List<FrameworkElement> _pageElements = [];
    private readonly List<LabButton> _tabs = [];
    public IEnumerable<KeyValuePair<string, FrameworkElement>> Fields => _fields.Concat(_terminals.Fields);

    public FrameStructureEditor(Node detachedDraft)
    {
        _draft = detachedDraft;
        var contract = detachedDraft.Contract ?? throw new ArgumentException("A frame structure needs a contract.", nameof(detachedDraft));
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, Padding = new Thickness(8) };
        Children.Add(tabs); Children.Add(_pages); SetRow(_pages, 1);
        _error.TextWrapping = TextWrapping.Wrap; _error.Margin = new(10, 3, 10, 3); Children.Add(_error); SetRow(_error, 2);
        _selector = new() { ItemsSource = new[] { "Boolean", "Number", "String", "Error" }, SelectedItem = contract.SelectorType.ToString(), MinWidth = 130, MinHeight = 28, FontSize = 12 };
        _caseInsensitive.IsChecked = contract.CaseInsensitive;
        _fields["case-selector-type"] = _selector; _fields["case-ignore-case"] = _caseInsensitive;

        var framePage = new StackPanel { Spacing = 6 };
        var note = LabTheme.Text(detachedDraft.Kind == "case"
            ? "Each selector belongs to one case. Use comma-separated values, inclusive numeric ranges, or quoted strings. Numeric and string selectors require one default case."
            : "Frames execute from left to right. Sequence locals are available only after their source frame completes. Reordering cannot introduce reads before writes.", 12, "#555555");
        note.TextWrapping = TextWrapping.Wrap; note.Margin = new(10, 3, 10, 3); framePage.Children.Add(note);
        if (detachedDraft.Kind == "case") framePage.Children.Add(Row(LabTheme.Text("Selector type"), _selector, _caseInsensitive));
        framePage.Children.Add(_frameRows);
        var add = new LabButton("Add frame", () => Mutate(() =>
        {
            if (_draft.Frames.Count >= 64) throw new ArgumentException("A structure is limited to 64 frames.");
            _draft.Frames.Add(new() { Selector = _draft.Frames.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        }), flat: false);
        _fields["frame-add"] = add; framePage.Children.Add(add);
        AddPage("Frames", new ScrollViewer { Content = framePage, MaxHeight = 425 }, "frames-tab");
        _terminals = new(detachedDraft.Kind, contract);
        AddPage("Terminals", _terminals, "terminals-tab");
        if (detachedDraft.Kind == "sequence")
        {
            var localPage = new StackPanel { Spacing = 6 };
            var heading = LabTheme.Text("Name / value type / source frame", 13); heading.Margin = new(10, 5, 10, 0); localPage.Children.Add(heading); localPage.Children.Add(_localRows);
            var addLocal = new LabButton("Add sequence local", () => Mutate(() =>
            {
                if (_draft.Contract!.Locals.Length >= 32) throw new ArgumentException("A sequence is limited to 32 locals.");
                _draft.Contract = _draft.Contract with { Locals = _draft.Contract.Locals.Add(new() { Name = "local" + (_draft.Contract.Locals.Length + 1), SourceFrameId = _draft.Frames[0].Id }) };
            }), flat: false);
            _fields["local-add"] = addLocal; localPage.Children.Add(addLocal);
            AddPage("Sequence locals", new ScrollViewer { Content = localPage, MaxHeight = 425 }, "locals-tab");
        }
        BuildRows(); ShowPage(0);

        void AddPage(string title, FrameworkElement page, string key)
        {
            var index = _pageElements.Count;
            var button = new LabButton(title, () => ShowPage(index), flat: false);
            tabs.Children.Add(button); _tabs.Add(button); _fields[key] = button;
            _pageElements.Add(page); _pages.Children.Add(page);
        }
    }

    private static StackPanel Row(params UIElement[] children)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(10, 0, 10, 0) };
        foreach (var child in children) panel.Children.Add(child);
        return panel;
    }

    private void ShowPage(int index)
    {
        for (var i = 0; i < _pageElements.Count; i++) { _pageElements[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed; _tabs[i].SetActive(i == index); }
    }

    private void CaptureRows()
    {
        foreach (var row in _rows) { row.Frame.Selector = row.Label.Text; row.Frame.IsDefault = row.Default.IsChecked == true; }
        if (_draft.Kind == "sequence")
            _draft.Contract = _draft.Contract! with { Locals = _locals.Select(row => new SequenceLocal
            {
                Name = row.Name.Text.Trim(), Type = Enum.Parse<ValueKind>((string)row.Type.SelectedItem),
                SourceFrameId = row.Source.SelectedIndex >= 0 ? row.FrameIds[row.Source.SelectedIndex] : ""
            }).ToImmutableArray() };
    }

    private void Mutate(Action action)
    {
        try { CaptureRows(); action(); _error.Text = ""; BuildRows(); }
        catch (Exception exception) { _error.Text = exception.Message; }
    }

    private void BuildRows()
    {
        foreach (var key in _fields.Keys.Where(k => k.StartsWith("frame-row-", StringComparison.Ordinal) || k.StartsWith("local-row-", StringComparison.Ordinal)).ToArray()) _fields.Remove(key);
        _rows.Clear(); _frameRows.Children.Clear();
        for (var i = 0; i < _draft.Frames.Count; i++)
        {
            var index = i; var frame = _draft.Frames[i];
            var label = new LabTextBox(frame.Selector, "Frame selector or label") { Width = 245 };
            var fallback = new CheckBox { Content = "Default", IsChecked = frame.IsDefault, FontSize = 12, Visibility = _draft.Kind == "case" ? Visibility.Visible : Visibility.Collapsed };
            var up = new LabButton("↑", () => Mutate(() => { if (index > 0) (_draft.Frames[index - 1], _draft.Frames[index]) = (_draft.Frames[index], _draft.Frames[index - 1]); }));
            var down = new LabButton("↓", () => Mutate(() => { if (index + 1 < _draft.Frames.Count) (_draft.Frames[index + 1], _draft.Frames[index]) = (_draft.Frames[index], _draft.Frames[index + 1]); }));
            var duplicate = new LabButton("Duplicate", () => Mutate(() =>
            {
                if (_draft.Frames.Count >= 64) throw new ArgumentException("A structure is limited to 64 frames.");
                InstrumentSession.DuplicateFrame(_draft, index);
            }));
            var remove = new LabButton("×", () => Mutate(() =>
            {
                if (_draft.Frames.Count == 1) throw new ArgumentException("A structure needs at least one frame.");
                if (_draft.Contract!.Locals.Any(local => local.SourceFrameId == frame.Id)) throw new ArgumentException("Move or remove this frame's sequence locals before deleting their source frame.");
                _draft.Frames.RemoveAt(index);
            }));
            up.IsEnabled = index > 0; down.IsEnabled = index < _draft.Frames.Count - 1;
            _frameRows.Children.Add(Row(LabTheme.Text(index.ToString(), 12), label, fallback, up, down, duplicate, remove));
            _rows.Add(new(frame, label, fallback));
            _fields[$"frame-row-{index}-label"] = label; _fields[$"frame-row-{index}-default"] = fallback;
            _fields[$"frame-row-{index}-up"] = up; _fields[$"frame-row-{index}-down"] = down; _fields[$"frame-row-{index}-duplicate"] = duplicate; _fields[$"frame-row-{index}-remove"] = remove;
        }
        _locals.Clear(); _localRows.Children.Clear();
        foreach (var local in _draft.Contract!.Locals)
        {
            var index = _locals.Count;
            var name = new LabTextBox(local.Name, "Local name") { Width = 150 };
            var type = new ComboBox { ItemsSource = Enum.GetNames<ValueKind>(), SelectedItem = local.Type.ToString(), MinWidth = 110, MinHeight = 28, FontSize = 12 };
            var ids = _draft.Frames.Select(f => f.Id).ToArray();
            var source = new ComboBox { ItemsSource = _draft.Frames.Select((f, n) => n + ": " + f.Selector).ToArray(), SelectedIndex = Array.IndexOf(ids, local.SourceFrameId), MinWidth = 220, MinHeight = 28, FontSize = 12 };
            var remove = new LabButton("×", () => Mutate(() => _draft.Contract = _draft.Contract! with { Locals = _draft.Contract.Locals.RemoveAt(index) }));
            _localRows.Children.Add(Row(name, type, source, remove)); _locals.Add(new(name, type, source, ids));
            _fields[$"local-row-{index}-name"] = name; _fields[$"local-row-{index}-type"] = type; _fields[$"local-row-{index}-source"] = source;
        }
    }

    public Node ReadDraft()
    {
        CaptureRows();
        _draft.Contract = _terminals.ReadContract() with
        {
            SelectorType = Enum.Parse<ValueKind>((string)_selector.SelectedItem),
            CaseInsensitive = _caseInsensitive.IsChecked == true,
            Locals = _draft.Contract!.Locals
        };
        return _draft;
    }
}
