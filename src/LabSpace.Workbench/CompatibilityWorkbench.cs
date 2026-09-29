using LabSpace.Controls;
using LabSpace.Core;
using LabSpace.Editing;
using Microsoft.UI.Xaml.Automation;

namespace LabSpace.Workbench;

public sealed partial class InstrumentWorkbench
{
    private readonly Grid _overlay = new() { Visibility = Visibility.Collapsed, Background = LabTheme.Brush("#20000000") };
    private readonly Dictionary<string, FrameworkElement> _overlayFields = [];
    private StructureContractEditor? _contractEditor;
    private LabPane? _quickPane;
    public QuickDropControl QuickDrop { get; } = new();
    public string OverlayMode { get; private set; } = "";
    public IEnumerable<KeyValuePair<string, FrameworkElement>> OverlayFields => _overlayFields.Concat(_contractEditor?.Fields ?? new Dictionary<string, FrameworkElement>()).Concat(_frameEditor?.Fields ?? new Dictionary<string, FrameworkElement>()).Concat(_formulaEditor?.Fields ?? new Dictionary<string, FrameworkElement>());

    private void InitializeCompatibility(Grid root, StackPanel tools)
    {
        tools.Children.Add(LabTheme.Separator());
        tools.Children.Add(Command("quick-drop", "Quick Drop (Ctrl+Space)", () => OpenQuickDrop(View == StudioView.FrontPanel), "quick-drop"));
        tools.Children.Add(Command("structure", "Tunnels and shift registers", () => { if (Session.SelectedNode is { } node && NodeCatalog.Resolve(node).IsStructure) OpenStructure(node); else Session.Message("Select a For, While, Case or SubVI structure first."); }, "VI"));
        tools.Children.Add(Command("step-into", "Step into (F11)", Session.StepInto, "↓"));
        _viewTabs.Children.Add(Command("true-branch", "TRUE case", () => SwitchCase(false), null, true, true));
        _viewTabs.Children.Add(Command("false-branch", "FALSE case", () => SwitchCase(true), null, true, true));
        Grid.SetRow(_overlay, 0); Grid.SetRowSpan(_overlay, 6); root.Children.Add(_overlay);
        _overlay.KeyDown += (_, e) => { if (e.Key == VirtualKey.Escape) { CloseOverlay(); e.Handled = true; } };
        Inspector.StructureRequested += OpenStructure;
        FrontPanel.PaletteRequested += p => OpenQuickDrop(true, p);
        BlockDiagram.PaletteRequested += p => OpenQuickDrop(false, p);
        QuickDrop.Chosen += entry =>
        {
            var panel = OverlayMode == "controls"; CloseOverlay();
            if (panel) FrontPanel.ArmPlacement(entry.Kind, entry.Widget); else BlockDiagram.ArmPlacement(entry.Kind);
            Session.Message("Place " + entry.Title + ": click the canvas. Escape cancels.");
        };
        QuickDrop.Dismissed += CloseOverlay;
        // Buttons consume Space for their own activation. Observe the routed key even
        // when handled so Ctrl+Space remains a studio command after toolbar/tab clicks.
        AddHandler(KeyDownEvent, new KeyEventHandler((_, e) =>
        {
            if (e.Key != VirtualKey.Space || _dialogOpen) return;
            var control = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
            if ((control & Windows.UI.Core.CoreVirtualKeyStates.Down) == 0) return;
            OpenQuickDrop(View == StudioView.FrontPanel);
            e.Handled = true;
        }), true);
        var quick = new KeyboardAccelerator { Key = VirtualKey.Space, Modifiers = VirtualKeyModifiers.Control };
        quick.Invoked += (_, e) => { if (_dialogOpen) return; OpenQuickDrop(View == StudioView.FrontPanel); e.Handled = true; }; KeyboardAccelerators.Add(quick);
        var step = new KeyboardAccelerator { Key = VirtualKey.F11 };
        step.Invoked += (_, e) => { if (_dialogOpen) return; Safe(Session.StepInto); e.Handled = true; }; KeyboardAccelerators.Add(step);
        InitializeAdvanced(tools);
    }
    private void OpenQuickDrop(bool controls, Point? position = null)
    {
        if (_dialogOpen) return;
        BlockDiagram.Cancel(); FrontPanel.Cancel(); _dialogOpen = true; OverlayMode = controls ? "controls" : "functions";
        _overlayFields.Clear(); _overlayFields["quick-drop-query"] = QuickDrop.Query;
        var pane = new LabPane(controls ? "Controls — Quick Drop" : "Functions — Quick Drop") { Width = Math.Min(440, Math.Max(270, ActualWidth - 30)), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left, BorderBrush = LabTheme.Brush("#777777"), BorderThickness = new(1) };
        var origin = ElementBounds(controls ? (FrameworkElement)FrontPanel : BlockDiagram);
        var x = position is { } p ? origin.X + p.X : (ActualWidth - pane.Width) / 2;
        var y = position is { } q ? origin.Y + q.Y : 130;
        pane.Margin = new(Math.Clamp(x, 10, Math.Max(10, ActualWidth - pane.Width - 10)), Math.Clamp(y, 10, Math.Max(10, ActualHeight - 370)), 0, 0);
        _quickPane = pane; pane.Commands.Children.Add(new LabButton("×", CloseOverlay)); pane.PaneContent = QuickDrop;
        _overlay.Children.Clear(); _overlay.Children.Add(pane); _overlay.Visibility = Visibility.Visible;
        DispatcherQueue.TryEnqueue(() => QuickDrop.Open(controls));
    }
    private void OpenStructure(Node node)
    {
        if (_dialogOpen) return;
        _dialogOpen = true; OverlayMode = "structure";
        _contractEditor = new(node.Kind, InstrumentSession.ContractDraft(node));
        var content = new StackPanel { Spacing = 5 }; content.Children.Add(_contractEditor);
        var error = LabTheme.Text("", 12, "#AC3026"); error.TextWrapping = TextWrapping.Wrap; error.Margin = new(10, 0, 10, 0); content.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(10) };
        var apply = new LabButton("Apply", () =>
        {
            try { Session.ConfigureStructure(node.Id, _contractEditor!.ReadContract()); CloseOverlay(); BlockDiagram.Fit(); }
            catch (Exception e) { error.Text = e.Message; }
        }, flat: false);
        var cancel = new LabButton("Cancel", CloseOverlay, flat: false); buttons.Children.Add(apply); buttons.Children.Add(cancel); content.Children.Add(buttons);
        _overlayFields.Clear(); _overlayFields["contract-apply"] = apply; _overlayFields["contract-cancel"] = cancel;
        AutomationProperties.SetAutomationId(apply, "contract-apply"); AutomationProperties.SetAutomationId(cancel, "contract-cancel");
        var pane = new LabPane(node.Label + " — Tunnels and shift registers") { Width = Math.Min(860, Math.Max(300, ActualWidth - 40)), MaxHeight = Math.Max(200, ActualHeight - 40), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, BorderBrush = LabTheme.Brush("#777777"), BorderThickness = new(1) };
        pane.PaneContent = content; pane.Commands.Children.Add(new LabButton("×", CloseOverlay));
        _overlay.Children.Clear(); _overlay.Children.Add(pane); _overlay.Visibility = Visibility.Visible;
    }
    private void CloseOverlay()
    {
        if (_quickPane is not null) { _quickPane.PaneContent = null; _quickPane = null; }
        _overlay.Visibility = Visibility.Collapsed; _overlay.Children.Clear(); _overlayFields.Clear(); _contractEditor = null; _frameEditor = null; _formulaEditor = null; OverlayMode = ""; _dialogOpen = false;
        if (View == StudioView.FrontPanel) FrontPanel.Focus(FocusState.Programmatic); else BlockDiagram.Focus(FocusState.Programmatic);
    }
    private void RunOrShowErrors()
    {
        if (Session.Diagnostics.Count > 0) { _errorsHost.Visibility = Visibility.Visible; BuildErrors(); Session.Message("The VI is broken. Resolve the listed errors before running."); }
        else Session.Run();
    }
    private void UpdateCompatibility()
    {
        UpdateAdvanced();
        if (_commands["run"].Content is VectorIcon icon) { var name = Session.Diagnostics.Count > 0 ? "run-broken" : "run"; if (icon.Icon != name) { icon.Icon = name; icon.Invalidate(); } }
        var inCase = false;
        if (Session.Path.Count > 0)
        {
            var graph = Session.Instrument.Diagram;
            for (var i = 0; i < Session.Path.Count; i++)
            {
                var level = Session.Path[i]; var node = graph.Nodes.FirstOrDefault(n => n.Id == level.NodeId); if (node is null) break;
                if (i == Session.Path.Count - 1) inCase = node.Kind == "case";
                graph = InstrumentSession.ResolveChild(node, level) ?? graph;
            }
        }
        _commands["true-branch"].Visibility = _commands["false-branch"].Visibility = inCase ? Visibility.Visible : Visibility.Collapsed;
        if (inCase) { _commands["true-branch"].SetActive(!Session.Path[^1].Alternative); _commands["false-branch"].SetActive(Session.Path[^1].Alternative); }
        if (Session.IsPaused && Session.DebugFrame is { } frame)
        {
            _helpPane.Caption.Text = "Execution context";
            _help.Text = "Next: " + (frame.NextNode?.Label ?? "return") + "\nDepth: " + (frame.Path.Count(c => c == '/') + 1) + "\n\n" + string.Join("\n", frame.Graph.Order.Where(n => frame.Values.ContainsKey(n.Model.Id)).TakeLast(8).Select(n => n.Model.Label + " = " + frame.Values[n.Model.Id]));
        }
        else _helpPane.Caption.Text = "Context Help";
    }
    private void SwitchCase(bool alternative)
    {
        if (Session.Path.Count == 0) return; var id = Session.Path[^1].NodeId; Session.Leave(); Session.Enter(id, alternative); BlockDiagram.Fit();
    }
}
