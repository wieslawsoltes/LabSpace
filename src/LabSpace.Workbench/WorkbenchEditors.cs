using LabSpace.Controls;
using LabSpace.Core;
using Microsoft.UI.Xaml.Automation;

namespace LabSpace.Workbench;

public sealed partial class InstrumentWorkbench
{
    public FrameworkElement? ActiveDialog { get; private set; }
    private LabDialog? _activeLabDialog;
    private async Task<bool> ShowDialogAsync(LabDialog dialog)
    {
        _activeLabDialog = dialog; ActiveDialog = dialog.Root;
        try { return await dialog.ShowAsync(XamlRoot); }
        finally { ActiveDialog = null; _activeLabDialog = null; }
    }
    private void OpenDiagramContext(Windows.Foundation.Point position)
    {
        var node = Session.SelectedNode;
        if (node is null && Session.SelectedWire is null) { ShowProperties(false); Palette.FocusSearch(); return; }
        var menu = new MenuFlyout();
        void Item(string label, Action action)
        {
            var item = new MenuFlyoutItem { Text = label, FontSize = 12, FontFamily = LabTheme.Font };
            item.Click += (_, _) => Safe(action); menu.Items.Add(item);
        }
        if (node is not null)
        {
            Item("Properties / representation…", () => Forget(EditRepresentationAsync(node)));
            if (node.Contract is not null) Item("Configure tunnels and shift registers…", () => Forget(EditStructureAsync(node)));
            if (NodeCatalog.Describe(node).IsStructure)
            {
                Item("Open body", () => { Session.Enter(node.Id, node.PreviewAlternative); BlockDiagram.Fit(); });
                if (node.Kind is "case" or "case-typed") Item("Show other case", () => Session.Edit(() => node.PreviewAlternative = !node.PreviewAlternative, false));
            }
            Item(node.Breakpoint ? "Clear breakpoint" : "Set breakpoint", Session.ToggleBreakpoint);
            Item("Duplicate", () => { Session.Copy(); Session.Paste(); });
        }
        else
        {
            Item("Attach / remove probe", Session.ToggleProbe);
            Item("Restore automatic wire routing", Session.ResetWireRouting);
        }
        Item("Delete", Session.Delete);
        menu.ShowAt(BlockDiagram, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions { Position = position });
    }
    private async Task EditRepresentationAsync(Node node)
    {
        if (_dialogOpen) return; _dialogOpen = true;
        try
        {
            var editable = node.Kind is "typed-control" or "typed-constant";
            var supported = editable || node.Type is not null || node.Kind is "typed-indicator" or "bundle" or "unbundle" or "convert" or "to-variant" or "from-variant" or "array-build" or "array-index" or "array-replace" or "array-concat" or "array-reverse" or "array-sort" or "array-transpose" or "array-reshape" or "add" or "subtract" or "multiply" or "divide" or "min" or "max" or "power";
            if (!supported) { ShowProperties(true); return; }
            var schema = NodeCatalog.Describe(node);
            var type = node.Type ?? (node.Kind == "unbundle" ? schema.Inputs[0].DataType : node.Kind.StartsWith("array-", StringComparison.Ordinal) ? schema.Inputs[0].DataType : schema.DataType);
            var body = new StackPanel { Spacing = 10 }; var editor = new TypeDescriptorEditor(type); body.Children.Add(editor);
            var value = new LabTextBox(node.Text, "Typed value") { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 64, MaxHeight = 200 };
            AutomationProperties.SetAutomationId(value, "typed-value");
            if (editable)
            {
                body.Children.Add(LabTheme.Text("Value (JSON for arrays, clusters, booleans and complex data)", 12)); body.Children.Add(value);
                body.Children.Add(new LabButton("Use the type's default value", () => value.Text = "", flat: false));
                var hint = LabTheme.Text("I64 and U64 literals are parsed without conversion through floating point. Empty input selects the type's default. Plain strings do not need quotes.", 11, "#666666"); hint.TextWrapping = TextWrapping.Wrap; body.Children.Add(hint);
            }
            var validation = LabTheme.Text("", 12, "#AC3025"); validation.TextWrapping = TextWrapping.Wrap; body.Children.Add(validation);
            var dialog = new LabDialog(node.Label + " — " + (editable ? "Value and representation" : "Representation"), body);
            dialog.Validate = () =>
            {
                try { var result = editor.ReadType(); if (editable) Session.SetTypedLiteral(node.Id, result, value.Text); else Session.SetType(node.Id, result); return true; }
                catch (Exception e) { validation.Text = e.Message; return false; }
            };
            dialog.Opened = () => { var box = editable ? value : editor.Expression; box.Focus(FocusState.Programmatic); box.SelectAll(); };
            await ShowDialogAsync(dialog);
        }
        finally { _dialogOpen = false; }
    }
    private async Task EditStructureAsync(Node node)
    {
        if (_dialogOpen || node.Contract is null) return; _dialogOpen = true;
        try
        {
            var body = new StackPanel { Spacing = 10 }; var editor = new StructureEditor(node); body.Children.Add(editor);
            var validation = LabTheme.Text("", 12, "#AC3025"); validation.TextWrapping = TextWrapping.Wrap; body.Children.Add(validation);
            var dialog = new LabDialog(node.Label + " — Structure configuration", body, width: 850);
            dialog.Validate = () =>
            {
                try { Session.ConfigureStructure(node.Id, editor.ReadContract()); return true; }
                catch (Exception e) { validation.Text = e.Message; return false; }
            };
            await ShowDialogAsync(dialog); BlockDiagram.Fit();
        }
        finally { _dialogOpen = false; }
    }
    private async Task QuickDropAsync()
    {
        if (_dialogOpen) return; _dialogOpen = true;
        try
        {
            var body = new StackPanel { Spacing = 8 };
            var search = new LabTextBox("", "Quick Drop search") { PlaceholderText = "Search functions or controls…" };
            AutomationProperties.SetAutomationId(search, "quick-drop-search"); body.Children.Add(search);
            var results = new StackPanel { Spacing = 2 }; body.Children.Add(new ScrollViewer { Content = results, Height = 300 });
            var hint = LabTheme.Text("Enter inserts the selected function. Arrow keys select; Escape cancels.", 11, "#666666"); body.Children.Add(hint);
            var matches = new List<NodeDefinition>(); var selected = 0; var buttons = new List<LabButton>();
            var dialog = new LabDialog("Quick Drop", body, "Insert", 460);
            void Select(int index) { selected = Math.Clamp(index, 0, Math.Max(0, matches.Count - 1)); for (var i = 0; i < buttons.Count; i++) buttons[i].SetActive(i == selected); }
            void Rebuild()
            {
                var query = search.Text.Trim();
                matches = NodeCatalog.All.Where(n => (View != StudioView.FrontPanel || n.IsControl || n.IsIndicator) && (query.Length == 0 || n.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || n.Category.Contains(query, StringComparison.OrdinalIgnoreCase) || n.Kind.Contains(query, StringComparison.OrdinalIgnoreCase))).Take(40).ToList();
                results.Children.Clear(); buttons.Clear(); selected = 0;
                for (var i = 0; i < matches.Count; i++)
                {
                    var index = i; var n = matches[i]; var button = new LabButton(n.Title + "   ·   " + n.Category, () => { Select(index); dialog.Accept(); }) { HorizontalContentAlignment = HorizontalAlignment.Left };
                    AutomationProperties.SetAutomationId(button, "quick-drop-" + n.Kind); buttons.Add(button); results.Children.Add(button);
                }
                Select(0);
                if (matches.Count == 0) results.Children.Add(LabTheme.Text("No matching functions.", 12));
            }
            search.TextChanged += (_, _) => Rebuild();
            search.KeyDown += (_, e) =>
            {
                if (e.Key == VirtualKey.Down) { Select(selected + 1); e.Handled = true; }
                if (e.Key == VirtualKey.Up) { Select(selected - 1); e.Handled = true; }
                if (e.Key == VirtualKey.Enter) { dialog.Accept(); e.Handled = true; }
            };
            dialog.Validate = () => matches.Count > 0; dialog.Opened = () => search.Focus(FocusState.Programmatic); Rebuild();
            if (await ShowDialogAsync(dialog) && matches.Count > 0) AddFromPalette(matches[selected].Kind, null);
        }
        finally { _dialogOpen = false; }
    }
}
