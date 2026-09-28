using System.Globalization;
using LabSpace.Controls;
using LabSpace.Core;
using LabSpace.Documents;
using LabSpace.Editing;
using LabSpace.Skia;
using LabSpace.Storage;

namespace LabSpace.Workbench;

public enum StudioView { FrontPanel, BlockDiagram, Split }

/// <summary>The complete studio is a reusable Uno control. Hosts provide only storage and font assets.</summary>
public sealed partial class InstrumentWorkbench : UserControl, IDisposable
{
    private readonly IProjectStorage _storage;
    private readonly LabFonts _fonts;
    private readonly DispatcherTimer _executionTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly DispatcherTimer _recoveryTimer = new() { Interval = TimeSpan.FromMilliseconds(800) };
    private readonly StackPanel _projectItems = new() { Padding = new Thickness(5), Spacing = 2 };
    private readonly StackPanel _documentTabs = new() { Orientation = Orientation.Horizontal, Spacing = 1 };
    private readonly StackPanel _viewTabs = new() { Orientation = Orientation.Horizontal };
    private readonly Grid _editors = new();
    private readonly Grid _middle = new();
    private readonly LabPane _left = new("Project Explorer");
    private readonly LabPane _right = new("Controls");
    private readonly LabPane _front = new("Front Panel");
    private readonly LabPane _diagram = new("Block Diagram");
    private readonly LabPane _helpPane = new("Context Help");
    private readonly TextBlock _help = LabTheme.Text("", 12);
    private readonly TextBlock _status = LabTheme.Text("Ready", 12);
    private readonly TextBlock _metrics = LabTheme.Text("", 11, "#606060");
    private readonly TextBlock _title = LabTheme.Text("LabSpace", 12);
    private readonly StackPanel _errors = new() { Spacing = 3, Padding = new Thickness(8) };
    private readonly Border _errorsHost = new();
    private readonly Dictionary<string, LabButton> _commands = [];
    private readonly Dictionary<string, FrameworkElement> _viButtons = [];
    private readonly StackPanel _rightTabs = new() { Orientation = Orientation.Horizontal };
    private readonly Grid _rightContent = new();
    private bool _savingRecovery, _disposed, _showProperties, _dialogOpen;
    private long _recoveredRevision = -1;
    private string _projectSignature = "";
    private string? _hoverId;
    public InstrumentSession Session { get; }
    public FrontPanelSurface FrontPanel { get; }
    public DiagramSurface BlockDiagram { get; }
    public FunctionPalette Palette { get; } = new();
    public PropertyInspector Inspector { get; }
    public StudioView View { get; private set; } = StudioView.FrontPanel;
    public IReadOnlyDictionary<string, LabButton> Commands => _commands;
    public IReadOnlyDictionary<string, FrameworkElement> InstrumentButtons => _viButtons;
    public InstrumentWorkbench(InstrumentSession session, IProjectStorage storage, LabFonts fonts)
    {
        Session = session; _storage = storage; _fonts = fonts;
        FontFamily = LabTheme.Font; FontSize = 12; Background = LabTheme.Brush("#EFEFEF");
        FrontPanel = new(session, fonts); BlockDiagram = new(session, fonts); Inspector = new(session);
        FrontPanel.EditRequested += EditNode; BlockDiagram.EditRequested += EditNode;
        BlockDiagram.HoverChanged += SetHelp;
        Palette.AddRequested += AddFromPalette;
        _front.PaneContent = FrontPanel; _diagram.PaneContent = BlockDiagram;
        _front.Commands.Children.Add(Command("fit-panel", "Fit panel", FrontPanel.Fit, "fit", false));
        _diagram.Commands.Children.Add(Command("fit-diagram", "Fit diagram", BlockDiagram.Fit, "fit", false));
        var root = new Grid();
        root.RowDefinitions.Add(new() { Height = new(26) }); root.RowDefinitions.Add(new() { Height = new(34) }); root.RowDefinitions.Add(new() { Height = new(27) }); root.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new() { Height = new(25) });
        Content = root;
        var menuRow = new Grid { Background = LabTheme.Brush("#F4F4F4"), BorderBrush = LabTheme.Brush("#BABABA"), BorderThickness = new Thickness(0, 0, 0, 1) };
        menuRow.Children.Add(BuildMenus()); _title.HorizontalAlignment = HorizontalAlignment.Right; _title.Margin = new(0, 0, 12, 0); _title.Foreground = LabTheme.Brush("#626262"); menuRow.Children.Add(_title); root.Children.Add(menuRow);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1, Padding = new Thickness(5, 2, 5, 2), Background = LabTheme.Brush("#EFEFEF") };
        tools.Children.Add(Command("new", "New VI", Session.NewInstrument, "new")); tools.Children.Add(Command("open", "Open project", () => Forget(OpenAsync()), "open")); tools.Children.Add(Command("save", "Save project", () => Forget(SaveAsync()), "save")); tools.Children.Add(LabTheme.Separator());
        tools.Children.Add(Command("run", "Run", RunOrShowErrors, "run")); tools.Children.Add(Command("continuous", "Run continuously", () => Session.Run(true), "continuous")); tools.Children.Add(Command("abort", "Abort execution", Session.Abort, "stop")); tools.Children.Add(Command("pause", "Pause execution", Session.Pause, "pause")); tools.Children.Add(LabTheme.Separator());
        tools.Children.Add(Command("highlight", "Highlight execution", () => { Session.Highlight = !Session.Highlight; Session.Notify(SessionChange.Execution); }, "highlight")); tools.Children.Add(Command("step", "Single step", Session.Step, "step")); tools.Children.Add(Command("probe", "Attach wire probe", () => { if (Session.SelectedWire is null) Session.Message("Select a wire, then attach a probe."); else Session.ToggleProbe(); }, "probe")); tools.Children.Add(LabTheme.Separator());
        tools.Children.Add(Command("undo", "Undo", Session.Undo, "undo")); tools.Children.Add(Command("redo", "Redo", Session.Redo, "redo")); tools.Children.Add(LabTheme.Separator());
        tools.Children.Add(Command("edit-panel", "Edit front panel", () => { Session.PanelEditMode = !Session.PanelEditMode; Session.PanelEditMode.ToString(); Session.Notify(SessionChange.View | SessionChange.Execution); }, "edit"));
        tools.Children.Add(Command("layout", "Clean up diagram", () => { Safe(Session.AutoLayout); SetView(StudioView.BlockDiagram); BlockDiagram.Fit(); }, "layout"));
        tools.Children.Add(Command("fit", "Fit to window", Fit, "fit")); tools.Children.Add(LabTheme.Separator());
        var fontCaption = LabTheme.Text("13 pt Application Font", 12); fontCaption.Margin = new(8, 0, 8, 0); tools.Children.Add(fontCaption);
        tools.Children.Add(Command("errors", "Error list", ToggleErrors, "!")); tools.Children.Add(Command("help", "Help", () => Forget(ShowHelpAsync()), "help"));
        var toolbarScroll = new ScrollViewer { Content = tools, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled }; Grid.SetRow(toolbarScroll, 1); root.Children.Add(toolbarScroll);
        var tabHost = new ScrollViewer { Content = _documentTabs, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Background = LabTheme.Brush("#DADADA") }; Grid.SetRow(tabHost, 2); root.Children.Add(tabHost);
        _middle.ColumnDefinitions.Add(new() { Width = new(185) }); _middle.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); _middle.ColumnDefinitions.Add(new() { Width = new(245) });
        Grid.SetRow(_middle, 3); root.Children.Add(_middle);
        _left.PaneContent = new ScrollViewer { Content = _projectItems, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; _left.BorderBrush = LabTheme.Brush("#9B9B9B"); _left.BorderThickness = new Thickness(0, 0, 1, 0); _middle.Children.Add(_left);
        var center = new Grid(); center.RowDefinitions.Add(new() { Height = new(30) }); center.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        _viewTabs.Background = LabTheme.Brush("#E4E4E4");
        _viewTabs.Children.Add(Command("front-panel", "Front Panel", () => SetView(StudioView.FrontPanel), "panel", true, true));
        _viewTabs.Children.Add(Command("block-diagram", "Block Diagram", () => SetView(StudioView.BlockDiagram), "diagram", true, true));
        _viewTabs.Children.Add(Command("split", "Split", () => SetView(StudioView.Split), null, true, true));
        _viewTabs.Children.Add(Command("up", "Parent diagram", () => { Session.Leave(); BlockDiagram.Fit(); }, "←", true, true));
        center.Children.Add(_viewTabs); _editors.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); _editors.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        _editors.Children.Add(_front); _editors.Children.Add(_diagram); Grid.SetRow(_editors, 1); center.Children.Add(_editors); Grid.SetColumn(center, 1); _middle.Children.Add(center);
        var rightBody = new Grid(); rightBody.RowDefinitions.Add(new() { Height = new(29) }); rightBody.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        _rightTabs.Children.Add(new LabButton("Palette", () => ShowProperties(false))); _rightTabs.Children.Add(new LabButton("Properties", () => ShowProperties(true))); rightBody.Children.Add(_rightTabs);
        _rightContent.Children.Add(Palette); _rightContent.Children.Add(Inspector); Grid.SetRow(_rightContent, 1); rightBody.Children.Add(_rightContent); _right.PaneContent = rightBody;
        var rightColumn = new Grid(); rightColumn.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); rightColumn.RowDefinitions.Add(new() { Height = new(184) }); rightColumn.Children.Add(_right);
        _help.TextWrapping = TextWrapping.Wrap; _help.TextTrimming = TextTrimming.None; _help.Margin = new(10); _help.VerticalAlignment = VerticalAlignment.Top;
        _helpPane.PaneContent = new ScrollViewer { Content = _help }; Grid.SetRow(_helpPane, 1); rightColumn.Children.Add(_helpPane); rightColumn.BorderBrush = LabTheme.Brush("#9B9B9B"); rightColumn.BorderThickness = new Thickness(1, 0, 0, 0); Grid.SetColumn(rightColumn, 2); _middle.Children.Add(rightColumn);
        _errorsHost.Child = new ScrollViewer { Content = _errors, MaxHeight = 155 }; _errorsHost.Background = LabTheme.Brush("#FFF9EF"); _errorsHost.Visibility = Visibility.Collapsed; Grid.SetRow(_errorsHost, 4); root.Children.Add(_errorsHost);
        var statusBar = new Grid { Background = LabTheme.Brush("#E5E5E5"), BorderBrush = LabTheme.Brush("#A5A5A5"), BorderThickness = new Thickness(0, 1, 0, 0) }; statusBar.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); statusBar.ColumnDefinitions.Add(new() { Width = new(315) }); _status.Margin = new(8, 0, 8, 0); _metrics.Margin = new(8, 0, 8, 0); statusBar.Children.Add(_status); Grid.SetColumn(_metrics, 1); statusBar.Children.Add(_metrics); Grid.SetRow(statusBar, 5); root.Children.Add(statusBar);
        SizeChanged += (_, _) => { _middle.ColumnDefinitions[0].Width = ActualWidth < 1100 ? new(0) : new(185); _middle.ColumnDefinitions[2].Width = ActualWidth < 760 ? new(190) : new(245); _title.Visibility = ActualWidth < 900 ? Visibility.Collapsed : Visibility.Visible; };
        InitializeCompatibility(root, tools); AddAccelerators(); Session.Changed += OnChanged;
        _executionTimer.Tick += (_, _) => { if (!_dialogOpen) Session.Tick(); }; _executionTimer.Start();
        _recoveryTimer.Tick += (_, _) => Forget(SaveRecoveryAsync()); _recoveryTimer.Start();
        ShowProperties(false); RebuildProject(); ApplyView(); OnChanged(SessionChange.All); SetHelp(null);
        Loaded += (_, _) => { Fit(); Session.Run(); };
    }
    private LabButton Command(string key, string title, Action action, string? icon = null, bool register = true, bool showText = false)
    {
        var button = new LabButton(title, () => Safe(action));
        if (icon is not null)
        {
            var glyph = new VectorIcon(icon, _fonts);
            if (showText) { var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 }; stack.Children.Add(glyph); stack.Children.Add(LabTheme.Text(title)); button.Content = stack; }
            else { button.Content = glyph; button.Padding = new(4, 2, 4, 2); }
        }
        if (register) _commands[key] = button; return button;
    }
    private UIElement BuildMenus()
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
        void Menu(string title, params (string Label, Action Action)[] entries)
        {
            var button = new LabButton(title, () => { }); button.Padding = new(8, 1, 8, 1); button.MinHeight = 24;
            var flyout = new MenuFlyout();
            foreach (var entry in entries) { var item = new MenuFlyoutItem { Text = entry.Label, FontFamily = LabTheme.Font, FontSize = 13 }; item.Click += (_, _) => Safe(entry.Action); flyout.Items.Add(item); }
            button.Flyout = flyout; bar.Children.Add(button);
        }
        Menu("File", ("New VI\tCtrl+N", Session.NewInstrument), ("Open project…\tCtrl+O", () => Forget(OpenAsync())), ("Save project…\tCtrl+S", () => Forget(SaveAsync())), ("Export waveform CSV…", () => Forget(ExportAsync())), ("Load example project", () => Forget(LoadExamplesAsync())));
        Menu("Edit", ("Undo\tCtrl+Z", Session.Undo), ("Redo\tCtrl+Y", Session.Redo), ("Copy\tCtrl+C", Session.Copy), ("Paste\tCtrl+V", Session.Paste), ("Duplicate\tCtrl+D", () => { Session.Copy(); Session.Paste(); }), ("Delete selection", Session.Delete), ("Select all", Session.SelectAll));
        Menu("View", ("Front Panel", () => SetView(StudioView.FrontPanel)), ("Block Diagram\tCtrl+E", () => SetView(StudioView.BlockDiagram)), ("Split views", () => SetView(StudioView.Split)), ("Fit to window", Fit), ("100% zoom", () => { FrontPanel.SetZoom(1); BlockDiagram.SetZoom(1); }), ("Properties", () => ShowProperties(true)), ("Error list", ToggleErrors));
        Menu("Project", ("New virtual instrument", Session.NewInstrument), ("VI properties…", () => Forget(EditInstrumentAsync())), ("Parent diagram", () => Session.Leave()));
        Menu("Operate", ("Run\tCtrl+R", RunOrShowErrors), ("Run continuously\tF6", () => Session.Run(true)), ("Abort execution", Session.Abort), ("Pause / resume", Session.Pause), ("Single step\tF10", Session.Step), ("Set / remove breakpoint", Session.ToggleBreakpoint));
        Menu("Tools", ("Clean up diagram", () => { Session.AutoLayout(); BlockDiagram.Fit(); }), ("Attach / remove wire probe", Session.ToggleProbe), ("Toggle panel editing", () => { Session.PanelEditMode = !Session.PanelEditMode; Session.Notify(); }), ("Validate diagram", () => { Session.Validate(); ToggleErrors(); }));
        Menu("Window", ("Front Panel / Block Diagram", ToggleView), ("Split horizontally", () => SetView(StudioView.Split)), ("Fit all content", Fit));
        Menu("Help", ("LabSpace user guide", () => Forget(ShowHelpAsync())), ("Compatibility and limits", () => Forget(ShowHelpAsync(true))));
        return bar;
    }
    private void AddAccelerators()
    {
        void Key(VirtualKey key, VirtualKeyModifiers modifiers, Action action)
        {
            var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
            accelerator.Invoked += (_, e) => { if (_dialogOpen) return; Safe(action); e.Handled = true; }; KeyboardAccelerators.Add(accelerator);
        }
        Key(VirtualKey.E, VirtualKeyModifiers.Control, ToggleView); Key(VirtualKey.R, VirtualKeyModifiers.Control, RunOrShowErrors); Key(VirtualKey.S, VirtualKeyModifiers.Control, () => Forget(SaveAsync())); Key(VirtualKey.O, VirtualKeyModifiers.Control, () => Forget(OpenAsync())); Key(VirtualKey.N, VirtualKeyModifiers.Control, Session.NewInstrument);
        Key(VirtualKey.F6, VirtualKeyModifiers.None, () => Session.Run(true)); Key(VirtualKey.F10, VirtualKeyModifiers.None, Session.Step);
        // Editing accelerators stay on canvas surfaces so text fields retain native undo and clipboard behavior.
        foreach (var canvas in new CanvasViewport[] { FrontPanel, BlockDiagram })
        {
            foreach (var (key, action) in new (VirtualKey, Action)[] { (VirtualKey.Z, Session.Undo), (VirtualKey.Y, Session.Redo), (VirtualKey.C, Session.Copy), (VirtualKey.V, Session.Paste), (VirtualKey.A, Session.SelectAll), (VirtualKey.D, () => { Session.Copy(); Session.Paste(); }) })
            {
                var accelerator = new KeyboardAccelerator { Key = key, Modifiers = VirtualKeyModifiers.Control }; accelerator.Invoked += (_, e) => { Safe(action); e.Handled = true; }; canvas.KeyboardAccelerators.Add(accelerator);
            }
        }
    }
    private void RebuildProject()
    {
        var signature = Session.ActiveId + "|" + string.Join("|", Session.Project.Instruments.Select(v => v.Id + v.Name)); if (_projectSignature == signature) return; _projectSignature = signature;
        _projectItems.Children.Clear(); _documentTabs.Children.Clear(); _viButtons.Clear();
        var name = LabTheme.Text(Session.Project.Name, 12); name.Margin = new(4, 6, 3, 7); _projectItems.Children.Add(name);
        _projectItems.Children.Add(LabTheme.Text("▾  My Computer", 12));
        foreach (var vi in Session.Project.Instruments)
        {
            var id = vi.Id; var button = new LabButton(vi.Name, () => { Session.Switch(id); Fit(); }); button.HorizontalContentAlignment = HorizontalAlignment.Left; button.Margin = new(9, 1, 0, 1);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 }; row.Children.Add(new VectorIcon("diagram", _fonts)); row.Children.Add(LabTheme.Text(vi.Name, 12)); button.Content = row; button.SetActive(id == Session.ActiveId); _viButtons[id] = button; _projectItems.Children.Add(button);
            if (id == Session.ActiveId)
            {
                var front = new LabButton("Front Panel", () => SetView(StudioView.FrontPanel)); front.Margin = new(28, 0, 0, 0); front.HorizontalContentAlignment = HorizontalAlignment.Left; _projectItems.Children.Add(front);
                var diagram = new LabButton("Block Diagram", () => SetView(StudioView.BlockDiagram)); diagram.Margin = new(28, 0, 0, 3); diagram.HorizontalContentAlignment = HorizontalAlignment.Left; _projectItems.Children.Add(diagram);
            }
            var tab = new LabButton(vi.Name, () => { Session.Switch(id); Fit(); }); tab.Padding = new(12, 3, 12, 3); tab.SetActive(id == Session.ActiveId); _documentTabs.Children.Add(tab);
        }
        _projectItems.Children.Add(LabTheme.Separator(false)); var note = LabTheme.Text("Dependencies\n  LabSpace built-in functions\n\nTarget\n  Desktop / WebAssembly\n\nAcquisition\n  Simulated signals", 11, "#676767"); note.TextWrapping = TextWrapping.Wrap; note.Margin = new(10, 8, 0, 0); _projectItems.Children.Add(note);
    }
    private void OnChanged(SessionChange change)
    {
        UpdateCompatibility();
        _status.Text = Session.Status.Replace('\n', ' '); _status.Foreground = LabTheme.Brush(Session.Diagnostics.Count > 0 ? "#AC3229" : "#363636");
        _metrics.Text = $"{Session.LastMilliseconds:F2} ms  |  {Session.LastNodeCount} nodes  |  frame {Session.Frames}  |  Skia";
        _title.Text = Session.Instrument.Name + (Session.Dirty ? " *" : "") + " — LabSpace";
        _commands["undo"].IsEnabled = Session.CanUndo; _commands["redo"].IsEnabled = Session.CanRedo; _commands["pause"].SetActive(Session.IsPaused); _commands["continuous"].SetActive(Session.IsRunning); _commands["highlight"].SetActive(Session.Highlight); _commands["edit-panel"].SetActive(Session.PanelEditMode); _commands["up"].Visibility = Session.Path.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        _commands["errors"].SetActive(Session.Diagnostics.Count > 0);
        if ((change & (SessionChange.Document | SessionChange.Navigation)) != 0) { RebuildProject(); if (_errorsHost.Visibility == Visibility.Visible) BuildErrors(); }
        if (Session.Path.Count > 0 && View == StudioView.FrontPanel) SetView(StudioView.BlockDiagram);
        _diagram.Caption.Text = Session.Path.Count == 0 ? Session.Instrument.Name + " — Block Diagram" : "Nested diagram · " + Session.Path.Count + " level(s)";
        _front.Caption.Text = Session.Instrument.Name + " — Front Panel";
        if ((change & SessionChange.Selection) != 0) SetHelp(Session.SelectedNode);
    }
    public void SetView(StudioView view) { View = view; ApplyView(); DispatcherQueue.TryEnqueue(Fit); }
    private void ApplyView()
    {
        _front.Visibility = View == StudioView.BlockDiagram ? Visibility.Collapsed : Visibility.Visible; _diagram.Visibility = View == StudioView.FrontPanel ? Visibility.Collapsed : Visibility.Visible;
        Grid.SetRow(_front, 0); Grid.SetRow(_diagram, View == StudioView.Split ? 1 : 0); Grid.SetRowSpan(_front, View == StudioView.Split ? 1 : 2); Grid.SetRowSpan(_diagram, View == StudioView.Split ? 1 : 2);
        Palette.SetControls(View == StudioView.FrontPanel); _right.Caption.Text = View == StudioView.FrontPanel ? "Controls" : "Functions";
        _commands["front-panel"].SetActive(View == StudioView.FrontPanel); _commands["block-diagram"].SetActive(View == StudioView.BlockDiagram); _commands["split"].SetActive(View == StudioView.Split);
    }
    public void ToggleView() => SetView(View == StudioView.FrontPanel ? StudioView.BlockDiagram : StudioView.FrontPanel);
    public void Fit() { FrontPanel.Fit(); BlockDiagram.Fit(); }
    public void ShowProperties(bool show) { _showProperties = show; Inspector.Visibility = show ? Visibility.Visible : Visibility.Collapsed; Palette.Visibility = show ? Visibility.Collapsed : Visibility.Visible; }
    private void AddFromPalette(string kind, string? widget)
    {
        Safe(() =>
        {
            var viewport = BlockDiagram.WorldViewport; var offset = Session.Diagram.Nodes.Count % 5 * 15;
            Session.Add(kind, Math.Round((viewport.MidX - 40 + offset) / 10) * 10, Math.Round((viewport.MidY - 30 + offset) / 10) * 10, widget);
            if (View == StudioView.FrontPanel) { Session.PanelEditMode = true; FrontPanel.Fit(); }
            Session.Message($"Added {NodeCatalog.Get(kind).Title}. " + (View == StudioView.FrontPanel ? "Drag to position the control." : "Connect its typed terminals."));
        });
    }
    private void SetHelp(Node? node)
    {
        var id = node?.Id; if (_hoverId == id && node is not null) return; _hoverId = id;
        if (node is null) { _help.Text = "LABSPACE\n\nFront Panel: operate or arrange controls.\nBlock Diagram: connect output terminals to matching inputs.\n\nCtrl+E switches views. Hover over a function for help."; return; }
        if (!NodeCatalog.TryGet(node.Kind, out var def)) { _help.Text = "Unknown function: " + node.Kind; return; }
        def = NodeCatalog.Resolve(node);
        _help.Text = def.Title.ToUpperInvariant() + "\n\n" + def.Description + "\n\n" + string.Join("\n", def.Inputs.Select(p => $"{p.Name}: {p.Kind}" + (p.Required ? " (required)" : " (optional)"))) + "\n" + string.Join("\n", def.Outputs.Select(p => $"{p.Name}: {p.Kind} (output)"));
    }
    private void BuildErrors()
    {
        _errors.Children.Clear(); _errors.Children.Add(LabTheme.Text($"Error List — {Session.Diagnostics.Count} problem(s)", 13));
        foreach (var error in Session.Diagnostics.Take(100))
        {
            var local = error; var button = new LabButton(local.Code + ": " + local.Message, () => { if (local.NodeId is not null) Session.Select(local.NodeId); SetView(StudioView.BlockDiagram); ShowProperties(true); }); button.HorizontalContentAlignment = HorizontalAlignment.Left; _errors.Children.Add(button);
        }
        if (Session.Diagnostics.Count == 0) _errors.Children.Add(LabTheme.Text("No compile errors. The diagram is ready to run.", 12, "#276B31"));
    }
    private void ToggleErrors() { BuildErrors(); _errorsHost.Visibility = _errorsHost.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; }
    private async void EditNode(Node node)
    {
        if (_dialogOpen) return; _dialogOpen = true;
        try
        {
            var kind = NodeCatalog.Get(node.Kind); var editable = kind.IsControl || node.Kind is "constant" or "bool" or "string" or "array" or "input" or "output";
            if (!editable) { ShowProperties(true); return; }
            var isText = node.Kind is "string" or "string-control" or "array" or "input" or "output";
            var box = new LabTextBox(isText ? node.Text : node.Value.ToString("G17", CultureInfo.InvariantCulture), node.Label) { MinWidth = 290 };
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = node.Label, Content = box, PrimaryButtonText = "Apply", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
            dialog.Opened += (_, _) => { box.Focus(FocusState.Programmatic); box.SelectAll(); };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                if (isText) Session.SetText(node.Id, box.Text);
                else if (double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)) Session.SetValue(node.Id, value);
                else Session.Message("The value was not changed: enter a finite number using a decimal point.");
            }
        }
        catch (Exception error) { Session.Message(error.Message); }
        finally { _dialogOpen = false; }
    }
    public async Task SaveAsync()
    {
        var json = ProjectSerializer.Save(Session.Project); var revision = Session.Revision;
        await _storage.SaveAsync(SafeName(Session.Project.Name) + ".labspace.json", json); if (Session.Revision == revision) Session.MarkSaved(); Session.Message("Project saved");
    }
    private async Task<bool> ConfirmReplaceAsync()
    {
        if (!Session.Dirty) return true; if (_dialogOpen) return false;
        _dialogOpen = true;
        try { var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Replace the current project?", Content = "The current project has unsaved edits. Save it first to keep a separate file. The recovery copy will be replaced by the new project.", PrimaryButtonText = "Replace", CloseButtonText = "Cancel" }; return await dialog.ShowAsync() == ContentDialogResult.Primary; }
        finally { _dialogOpen = false; }
    }
    public async Task OpenAsync()
    {
        // Open the picker before an awaited confirmation to preserve browser user activation.
        var json = await _storage.OpenAsync(); if (string.IsNullOrEmpty(json)) return; var project = ProjectSerializer.Load(json);
        if (!await ConfirmReplaceAsync()) return; Session.Replace(project); _recoveredRevision = -1; Fit(); Session.Run();
    }
    private async Task LoadExamplesAsync() { if (!await ConfirmReplaceAsync()) return; Session.Replace(Examples.Create()); _recoveredRevision = -1; Fit(); Session.Run(); }
    private async Task EditInstrumentAsync()
    {
        if (_dialogOpen) return; _dialogOpen = true;
        try { var name = new LabTextBox(Session.Instrument.Name, "VI name"); var description = new LabTextBox(Session.Instrument.Description, "VI description") { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 130 }; var stack = new StackPanel { Spacing = 10, MinWidth = 340 }; stack.Children.Add(name); stack.Children.Add(description); var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Virtual instrument properties", Content = stack, PrimaryButtonText = "Apply", CloseButtonText = "Cancel" }; if (await dialog.ShowAsync() == ContentDialogResult.Primary) Session.Edit(() => { Session.Instrument.Name = name.Text; Session.Instrument.Description = description.Text; }, false); }
        finally { _dialogOpen = false; }
    }
    private async Task SaveRecoveryAsync()
    {
        if (_savingRecovery || _recoveredRevision == Session.Revision || _disposed) return;
        _savingRecovery = true;
        try { var revision = Session.Revision; var json = ProjectSerializer.Save(Session.Project); await _storage.WriteRecoveryAsync(json); _recoveredRevision = revision; }
        catch (Exception error) { Session.Message("Recovery save failed: " + error.Message); }
        finally { _savingRecovery = false; }
    }
    private async Task ExportAsync()
    {
        var id = Session.SelectedNode?.Id; var value = id is not null ? Session.Values.GetValueOrDefault(id) : null;
        value ??= Session.Values.Values.FirstOrDefault(v => v.Kind == ValueKind.Waveform);
        if (value is null || value.Kind is not (ValueKind.Waveform or ValueKind.Array)) { Session.Message("Run the VI and select a waveform or array before exporting."); return; }
        var text = new System.Text.StringBuilder("x,value\n"); for (var i = 0; i < value.Samples.Length; i++) text.Append((value.StartTime + i / value.SampleRate).ToString("G17", CultureInfo.InvariantCulture)).Append(',').Append(value.Samples[i].ToString("G17", CultureInfo.InvariantCulture)).Append('\n');
        await _storage.ExportAsync("LabSpace-waveform.csv", text.ToString(), "text/csv"); Session.Message("Waveform exported");
    }
    private static string SafeName(string name) => string.Concat(name.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or ' ')).Trim() is { Length: > 0 } valid ? valid : "LabSpace-project";
    private async Task ShowHelpAsync(bool compatibility = false)
    {
        if (_dialogOpen) return; _dialogOpen = true;
        try
        {
            var text = new TextBlock { Text = compatibility ? HelpContent.Compatibility : HelpContent.UserGuide, TextWrapping = TextWrapping.Wrap, FontSize = 14, FontFamily = LabTheme.Font, MaxWidth = 650 };
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = compatibility ? "Compatibility and limits" : "LabSpace — User guide", Content = new ScrollViewer { Content = text, MaxHeight = 540 }, CloseButtonText = "Close" }; await dialog.ShowAsync();
        }
        finally { _dialogOpen = false; }
    }
    public void Safe(Action action) { try { action(); } catch (Exception error) { Session.Message(error.Message); } }
    private async void Forget(Task task) { try { await task; } catch (Exception error) { Session.Message(error.Message); } }
    public Rect ElementBounds(FrameworkElement element)
    {
        if (element.Visibility != Visibility.Visible || element.ActualWidth <= 0 || element.ActualHeight <= 0) return new();
        try { var origin = element.TransformToVisual(this).TransformPoint(new(0, 0)); return new(origin.X, origin.Y, element.ActualWidth, element.ActualHeight); } catch { return new(); }
    }
    public new void Dispose()
    {
        if (_disposed) return; _disposed = true; _executionTimer.Stop(); _recoveryTimer.Stop(); Session.Abort(); Session.Changed -= OnChanged; FrontPanel.Dispose(); BlockDiagram.Dispose(); Inspector.Dispose();
    }
}
