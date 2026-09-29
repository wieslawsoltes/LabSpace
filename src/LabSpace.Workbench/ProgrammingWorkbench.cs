using LabSpace.Controls;
using LabSpace.Core;
using LabSpace.Editing;

namespace LabSpace.Workbench;

public sealed partial class InstrumentWorkbench
{
    private void OpenFrames(Node node)
    {
        if (_dialogOpen) return;
        var draft = InstrumentSession.FramesDraft(node);
        _frameEditor = new(draft); _dialogOpen = true; OverlayMode = "frames";
        ShowProgrammingEditor(node.Label + " — Case / Sequence frames", _frameEditor,
            () => Session.ConfigureFrames(node.Id, _frameEditor.ReadDraft()), "frames");
    }

    private void OpenFormula(Node node)
    {
        if (_dialogOpen) return;
        _formulaEditor = new(node); _dialogOpen = true; OverlayMode = "formula";
        ShowProgrammingEditor(node.Label + " — Formula Node", _formulaEditor,
            () => Session.ConfigureFormula(node.Id, _formulaEditor.Source.Text, _formulaEditor.ReadContract()), "formula");
    }

    private void ShowProgrammingEditor(string title, UIElement editor, Action applyDraft, string prefix)
    {
        var content = new StackPanel { Spacing = 5 }; content.Children.Add(editor);
        var error = LabTheme.Text("", 12, "#A32820"); error.TextWrapping = TextWrapping.Wrap; error.Margin = new(10, 0, 10, 0); content.Children.Add(error);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(10) };
        var apply = new LabButton("Apply", () =>
        {
            try { applyDraft(); CloseOverlay(); BlockDiagram.Fit(); }
            catch (Exception exception) { error.Text = exception.Message; }
        }, flat: false);
        var cancel = new LabButton("Cancel", CloseOverlay, flat: false);
        actions.Children.Add(apply); actions.Children.Add(cancel); content.Children.Add(actions);
        _overlayFields.Clear(); _overlayFields[prefix + "-apply"] = apply; _overlayFields[prefix + "-cancel"] = cancel; _overlayFields[prefix + "-error"] = error;
        var pane = new LabPane(title)
        {
            Width = Math.Min(890, Math.Max(300, ActualWidth - 40)), MaxHeight = Math.Max(200, ActualHeight - 40),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            BorderBrush = LabTheme.Brush("#777777"), BorderThickness = new Thickness(1), PaneContent = content
        };
        pane.Commands.Children.Add(new LabButton("×", CloseOverlay));
        _overlay.Children.Clear(); _overlay.Children.Add(pane); _overlay.Visibility = Visibility.Visible;
    }

    private void OpenDiagramContext(DiagramContextRequest request)
    {
        if (_dialogOpen) return;
        _dialogOpen = true; OverlayMode = "context"; _overlayFields.Clear();
        var items = new StackPanel { Spacing = 1, Padding = new Thickness(3) };
        void Item(string key, string label, Action action)
        {
            var button = new LabButton(label, () => { CloseOverlay(); Safe(action); }) { HorizontalContentAlignment = HorizontalAlignment.Left, HorizontalAlignment = HorizontalAlignment.Stretch };
            items.Children.Add(button); _overlayFields[key] = button;
        }
        var caption = request.Node?.Label ?? "Wire";
        if (request.Node is { } node)
        {
            if (request.Terminal is { } terminal)
            {
                caption = terminal + " — " + caption;
                if (request.Output)
                {
                    Item("context-indicator", "Create indicator", () => { Session.CreateTerminal(node.Id, terminal, true); Fit(); });
                    Item("context-wire", "Wire / branch from terminal", () => BlockDiagram.StartWire(node.Id, terminal));
                }
                else
                {
                    Item("context-constant", "Create constant", () => { Session.CreateTerminal(node.Id, terminal, false); Fit(); });
                    if (NodeCatalog.Resolve(node).Inputs.First(p => p.Name == terminal).Kind is ValueKind.Number or ValueKind.Boolean or ValueKind.String)
                        Item("context-control", "Create control", () => { Session.CreateTerminal(node.Id, terminal, false, true); Fit(); });
                }
            }
            if (node.Kind is "case" or "sequence") Item("context-frames", "Add / delete / rearrange cases…", () => OpenFrames(node));
            else if (NodeCatalog.Resolve(node).IsStructure) Item("context-contract", "Tunnels and shift registers…", () => OpenStructure(node));
            if (NodeCatalog.Resolve(node).IsStructure) Item("context-open", "Open selected subdiagram", () => { Session.Enter(node.Id); BlockDiagram.Fit(); });
            else Item("context-edit", node.Kind == "formula" ? "Edit formula…" : "Edit value / properties…", () => EditNode(node));
            Item("context-breakpoint", node.Breakpoint ? "Remove breakpoint" : "Set breakpoint", Session.ToggleBreakpoint);
            Item("context-duplicate", "Duplicate", () => { Session.Copy(); Session.Paste(); });
        }
        else if (request.WireId is { } wireId && Session.Diagram.Wires.FirstOrDefault(w => w.Id == wireId) is { } wire)
        {
            Item("context-probe", wire.Probe ? "Remove probe" : "Probe", Session.ToggleProbe);
            Item("context-wire", "Create wire branch", () => BlockDiagram.StartWire(wire.From, wire.Output));
        }
        Item("context-delete", "Delete", Session.Delete);
        Item("context-cancel", "Cancel", () => { });
        var origin = ElementBounds(BlockDiagram); const double width = 310;
        var pane = new LabPane(caption)
        {
            Width = Math.Min(width, ActualWidth - 20), MaxHeight = Math.Max(150, ActualHeight - 40),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Margin = new(Math.Clamp(origin.X + request.Position.X, 10, Math.Max(10, ActualWidth - width - 10)), Math.Clamp(origin.Y + request.Position.Y, 10, Math.Max(10, ActualHeight - 400)), 0, 0),
            BorderBrush = LabTheme.Brush("#777777"), BorderThickness = new Thickness(1),
            PaneContent = new ScrollViewer { Content = items, MaxHeight = 350 }
        };
        _overlay.Children.Clear(); _overlay.Children.Add(pane); _overlay.Visibility = Visibility.Visible;
    }
}
