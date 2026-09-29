using System.Globalization;
using LabSpace.Controls;
using LabSpace.Core;
using LabSpace.Dataflow;
using LabSpace.Editing;
using LabSpace.Skia;
using Microsoft.UI.Xaml.Automation;

namespace LabSpace.Workbench;

public sealed partial class InstrumentWorkbench
{
    private FrameEditorControl? _frameEditor;
    private FormulaEditorControl? _formulaEditor;
    private void InitializeAdvanced(StackPanel tools)
    {
        Inspector.EditorRequested += EditNode; Inspector.FramesRequested += OpenFrames;
        tools.Children.Add(Command("frames-edit", "Cases and sequence frames", () =>
        {
            if (Session.SelectedNode is { } node && StructureFrames.HasFrames(node)) OpenFrames(node);
            else Session.Message("Select a multi-case or sequence structure first.");
        }, "frames"));
        tools.Children.Add(Command("formula-edit", "Edit formula", () =>
        {
            if (Session.SelectedNode is { Kind: "formula" } node) OpenFormula(node);
            else Session.Message("Select a Formula Node first.");
        }, "formula"));
        tools.Children.Add(Command("step-out", "Step out (Ctrl+F11)", Session.StepOut, "step-out"));
        _viewTabs.Children.Add(Command("previous-frame", "◀ Previous frame", () => ChangeFrame(-1), null, true, true));
        _viewTabs.Children.Add(Command("next-frame", "Next frame ▶", () => ChangeFrame(1), null, true, true));
        BlockDiagram.ContextRequested += OpenDiagramContext;
        BlockDiagram.FramesRequested += OpenFrames;
        var stepOut = new KeyboardAccelerator { Key = VirtualKey.F11, Modifiers = VirtualKeyModifiers.Control };
        stepOut.Invoked += (_, e) => { if (_dialogOpen) return; Safe(Session.StepOut); e.Handled = true; }; KeyboardAccelerators.Add(stepOut);
    }
    private void ChangeFrame(int delta)
    {
        var owner = Session.ParentStructure;
        if (owner is null || !StructureFrames.HasFrames(owner)) return;
        Session.NavigateFrame(Math.Clamp(Session.Path[^1].FrameIndex + delta, 0, owner.Frames.Count - 1)); BlockDiagram.Fit();
    }
    private void UpdateAdvanced()
    {
        var owner = Session.ParentStructure;
        var frames = owner is not null && StructureFrames.HasFrames(owner);
        _commands["previous-frame"].Visibility = _commands["next-frame"].Visibility = frames ? Visibility.Visible : Visibility.Collapsed;
        if (frames)
        {
            _commands["previous-frame"].IsEnabled = Session.Path[^1].FrameIndex > 0;
            _commands["next-frame"].IsEnabled = Session.Path[^1].FrameIndex < owner!.Frames.Count - 1;
        }
        _commands["step-out"].IsEnabled = Session.IsPaused;
    }
    private void OpenFrames(Node node)
    {
        if (_dialogOpen) return;
        _frameEditor = new(node);
        ShowEditor(node.Label + (node.Kind == "sequence" ? " — Sequence frames" : " — Case selector labels"), "frames", _frameEditor, () =>
            Session.ConfigureFrames(node.Id, _frameEditor.SelectorType, _frameEditor.Frames, _frameEditor.IgnoreCase), 800);
    }
    private void OpenFormula(Node node)
    {
        if (_dialogOpen) return;
        _formulaEditor = new(node);
        ShowEditor(node.Label + " — Formula Node", "formula", _formulaEditor, () =>
            Session.ConfigureFormula(node.Id, _formulaEditor.ReadSignature(), _formulaEditor.Source.Text), 730);
        DispatcherQueue.TryEnqueue(() => _formulaEditor?.Source.Focus(FocusState.Programmatic));
    }
    private void ShowEditor(string title, string mode, UIElement editor, Action apply, double width)
    {
        _dialogOpen = true; OverlayMode = mode;
        var root = new StackPanel { Spacing = 6 }; root.Children.Add(editor);
        var error = LabTheme.Text("", 12, "#B02F27"); error.TextWrapping = TextWrapping.Wrap; error.Margin = new(12, 0, 12, 0); root.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Padding = new(10), HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new LabButton("Apply", () =>
        {
            try { apply(); CloseOverlay(); }
            catch (Exception e) { error.Text = e.Message; if (e is FormulaException f && _formulaEditor is { } formula) { formula.Source.Focus(FocusState.Programmatic); formula.Source.Select(Math.Min(f.Position, formula.Source.Text.Length), 0); } }
        }, flat: false);
        var cancel = new LabButton("Cancel", CloseOverlay, flat: false); buttons.Children.Add(ok); buttons.Children.Add(cancel); root.Children.Add(buttons);
        _overlayFields[mode + "-apply"] = ok; _overlayFields[mode + "-cancel"] = cancel;
        AutomationProperties.SetAutomationId(ok, mode + "-apply"); AutomationProperties.SetAutomationId(cancel, mode + "-cancel");
        var pane = new LabPane(title) { Width = Math.Min(width, Math.Max(300, ActualWidth - 30)), MaxHeight = Math.Max(200, ActualHeight - 30), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, BorderBrush = LabTheme.Brush("#777777"), BorderThickness = new(1), PaneContent = new ScrollViewer { Content = root } };
        pane.Commands.Children.Add(new LabButton("×", CloseOverlay)); _overlay.Children.Clear(); _overlay.Children.Add(pane); _overlay.Visibility = Visibility.Visible;
    }
    private void OpenError(Node node)
    {
        if (_dialogOpen) return;
        var status = new CheckBox { Content = "Error status", IsChecked = node.Parameter("status", 0) != 0, FontSize = 12 };
        var code = new LabTextBox(node.Value.ToString(CultureInfo.InvariantCulture), "Signed 32-bit code");
        var source = new LabTextBox(node.Text, "Error source") { AcceptsReturn = true, Height = 100 };
        var form = new StackPanel { Padding = new(12), Spacing = 8 };
        form.Children.Add(status); form.Children.Add(LabTheme.Text("Code", 12)); form.Children.Add(code); form.Children.Add(LabTheme.Text("Source", 12)); form.Children.Add(source);
        _overlayFields["error-status"] = status; _overlayFields["error-code"] = code; _overlayFields["error-source"] = source;
        ShowEditor(node.Label + " — Error cluster", "error", form, () =>
        {
            if (!int.TryParse(code.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)) throw new ArgumentException("Code must be a signed 32-bit integer.");
            Session.SetError(node.Id, status.IsChecked == true, number, source.Text);
        }, 520);
    }
    private void OpenComplex(Node node)
    {
        if (_dialogOpen) return;
        var real = new LabTextBox(node.Value.ToString("G17", CultureInfo.InvariantCulture), "Real component");
        var imaginary = new LabTextBox(node.Parameter("imaginary", 0).ToString("G17", CultureInfo.InvariantCulture), "Imaginary component");
        var form = new StackPanel { Padding = new(12), Spacing = 8 };
        form.Children.Add(LabTheme.Text("Real", 12)); form.Children.Add(real); form.Children.Add(LabTheme.Text("Imaginary", 12)); form.Children.Add(imaginary);
        _overlayFields["complex-real"] = real; _overlayFields["complex-imaginary"] = imaginary;
        ShowEditor(node.Label + " — Complex number", "complex", form, () =>
        {
            if (!double.TryParse(real.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var a) || !double.TryParse(imaginary.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var b)) throw new ArgumentException("Enter finite numbers for both components.");
            _ = Value.ComplexValue(a, b); Session.Edit(() => { node.Value = a; node.Parameters["imaginary"] = b; }, false);
        }, 420);
    }
    private void OpenDiagramContext(DiagramContextTarget target)
    {
        if (_dialogOpen) return;
        _dialogOpen = true; OverlayMode = "diagram-context";
        var root = new StackPanel { Padding = new(3), Spacing = 1 };
        void Add(string id, string title, Action action, bool enabled = true)
        {
            var button = new LabButton(title, () => { CloseOverlay(); Safe(action); }) { IsEnabled = enabled, HorizontalContentAlignment = HorizontalAlignment.Left, HorizontalAlignment = HorizontalAlignment.Stretch };
            _overlayFields[id] = button; root.Children.Add(button);
        }
        if (target.Node is { } node && target.Port is { } port)
        {
            var type = target.Output ? NodeCatalog.Resolve(node).FindOutput(port)?.Kind : NodeCatalog.Resolve(node).Inputs.FirstOrDefault(p => p.Name == port)?.Kind;
            if (target.Output)
            {
                Add("terminal-indicator", "Create Indicator", () => Session.CreateTerminal(node.Id, port, true, TerminalCreation.Indicator, new(DiagramGeometry.Bounds(node).Right + 140, node.Y)), type is not null && InstrumentSession.TerminalKind(type.Value, TerminalCreation.Indicator) is not null);
                Add("terminal-branch", "Branch Wire", () => BlockDiagram.StartWire(node.Id, port));
            }
            else
            {
                Add("terminal-constant", "Create Constant", () => Session.CreateTerminal(node.Id, port, false, TerminalCreation.Constant, new(node.X - 170, node.Y)), type is not null && InstrumentSession.TerminalKind(type.Value, TerminalCreation.Constant) is not null);
                Add("terminal-control", "Create Control", () => Session.CreateTerminal(node.Id, port, false, TerminalCreation.Control, new(node.X - 170, node.Y)), type is not null && InstrumentSession.TerminalKind(type.Value, TerminalCreation.Control) is not null);
            }
            Add("terminal-disconnect", "Disconnect Terminal", () => Session.DisconnectTerminal(node.Id, port, target.Output));
        }
        else if (target.Node is { } owner)
        {
            Add("node-open", NodeCatalog.Resolve(owner).IsStructure ? "Open Visible Diagram" : "Edit…", () => { if (NodeCatalog.Resolve(owner).IsStructure) { Session.Enter(owner.Id); BlockDiagram.Fit(); } else EditNode(owner); });
            if (StructureFrames.HasFrames(owner)) Add("node-frames", "Cases and Frames…", () => OpenFrames(owner));
            if (NodeCatalog.Resolve(owner).IsStructure) Add("node-contract", "Tunnels and Shift Registers…", () => OpenStructure(owner));
            Add("node-breakpoint", owner.Breakpoint ? "Clear Breakpoint" : "Set Breakpoint", () => Session.Edit(() => owner.Breakpoint = !owner.Breakpoint, false));
            Add("node-delete", "Delete", Session.Delete);
        }
        else if (target.Wire is { } wire)
        {
            Add("wire-branch", "Branch Wire", () => BlockDiagram.StartWire(wire.From, wire.Output));
            Add("wire-probe", wire.Probe ? "Remove Probe" : "Probe", () => Session.Edit(() => wire.Probe = !wire.Probe, false));
            Add("wire-delete", "Delete Wire", Session.Delete);
        }
        Add("context-functions", "Functions Palette…", () => OpenQuickDrop(false, target.Screen));
        var origin = ElementBounds(BlockDiagram); var width = 240d;
        var pane = new LabPane(target.Port ?? target.Node?.Label ?? "Wire") { Width = width, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, PaneContent = root, BorderBrush = LabTheme.Brush("#777777"), BorderThickness = new(1) };
        pane.Margin = new(Math.Clamp(origin.X + target.Screen.X, 5, Math.Max(5, ActualWidth - width - 5)), Math.Clamp(origin.Y + target.Screen.Y, 5, Math.Max(5, ActualHeight - 280)), 0, 0);
        pane.Commands.Children.Add(new LabButton("×", CloseOverlay)); _overlay.Children.Clear(); _overlay.Children.Add(pane); _overlay.Visibility = Visibility.Visible;
    }
}
