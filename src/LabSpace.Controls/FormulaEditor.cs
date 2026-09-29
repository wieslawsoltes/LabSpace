using LabSpace.Core;
using LabSpace.Dataflow;

namespace LabSpace.Controls;

/// <summary>Reusable staged scalar formula editor with the same compiler used by execution.</summary>
public sealed class FormulaEditor : Grid
{
    private readonly StructureContractEditor _terminals;
    private readonly TextBlock _validation = LabTheme.Text("", 12, "#555555");
    private readonly Dictionary<string, FrameworkElement> _fields = [];
    public LabTextBox Source { get; }
    public IEnumerable<KeyValuePair<string, FrameworkElement>> Fields => _fields.Concat(_terminals.Fields);

    public FormulaEditor(Node node)
    {
        RowDefinitions.Add(new() { Height = GridLength.Auto }); RowDefinitions.Add(new() { Height = GridLength.Auto }); RowDefinitions.Add(new() { Height = GridLength.Auto });
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, Padding = new Thickness(8) };
        var pages = new Grid(); Children.Add(tabs); Children.Add(pages); SetRow(pages, 1);
        Source = new(node.Text, "Formula source") { AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, FontFamily = new FontFamily("Courier New"), FontSize = 14, Height = 300, Margin = new Thickness(10), HorizontalAlignment = HorizontalAlignment.Stretch };
        ScrollViewer.SetHorizontalScrollBarVisibility(Source, ScrollBarVisibility.Auto); ScrollViewer.SetVerticalScrollBarVisibility(Source, ScrollBarVisibility.Auto);
        _terminals = new("formula", node.Contract ?? new() { Outputs = [new()] }) { MaxHeight = 370, Visibility = Visibility.Collapsed };
        pages.Children.Add(Source); pages.Children.Add(_terminals);
        var code = new LabButton("Formula", () => { Source.Visibility = Visibility.Visible; _terminals.Visibility = Visibility.Collapsed; }, flat: false);
        var terminals = new LabButton("Terminals", () => { Source.Visibility = Visibility.Collapsed; _terminals.Visibility = Visibility.Visible; }, flat: false);
        var validate = new LabButton("Check syntax", Validate, flat: false);
        tabs.Children.Add(code); tabs.Children.Add(terminals); tabs.Children.Add(validate);
        _fields["formula-source"] = Source; _fields["formula-code-tab"] = code; _fields["formula-terminals-tab"] = terminals; _fields["formula-check"] = validate;
        _validation.TextWrapping = TextWrapping.Wrap; _validation.Margin = new(10, 3, 10, 3); Children.Add(_validation); SetRow(_validation, 2);
        _validation.Text = "Scalar expressions, assignments, if/else, for, while, do/while, break and continue. Execution is bounded; no file, network or native-code access.";
    }

    public StructureContract ReadContract() => _terminals.ReadContract();
    public void Validate()
    {
        try { var program = FormulaProgram.Compile(Source.Text, ReadContract()); _validation.Text = "Compiled successfully · " + program.InstructionCount + " bytecode instructions. Apply to update the diagram."; }
        catch (Exception exception) { _validation.Text = exception.Message; }
    }
}
