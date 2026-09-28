using LabSpace.Core;
using Microsoft.UI.Xaml.Automation;

namespace LabSpace.Controls;

/// <summary>A reusable structural type editor. The compact syntax supports nested clusters, ranks and enum labels.</summary>
public sealed class TypeDescriptorEditor : StackPanel
{
    public LabTextBox Expression { get; }
    private readonly TextBlock _validation = LabTheme.Text("", 11, "#7B3024");
    public TypeDescriptorEditor(LabType type)
    {
        Spacing = 5;
        Children.Add(LabTheme.Text("Representation / wire type", 12));
        Expression = new(TypeSyntax.Format(type), "Wire type");
        AutomationProperties.SetAutomationId(Expression, "type-expression");
        Children.Add(Expression);
        var presets = new ComboBox { ItemsSource = new[] { "DBL", "SGL", "I8", "U8", "I16", "U16", "I32", "U32", "I64", "U64", "Boolean", "String", "CDB", "Error", "DBL[]", "DBL[,]", "{value: DBL, enabled: Boolean}", "enum(Off, On, Auto)" }, PlaceholderText = "Choose a representation…", FontSize = 12, MinHeight = 28, HorizontalAlignment = HorizontalAlignment.Stretch };
        presets.SelectionChanged += (_, _) => { if (presets.SelectedItem is string text) Expression.Text = text; }; Children.Add(presets);
        var guide = LabTheme.Text("Arrays: I32[] or DBL[,]    Cluster: {temperature: DBL, valid: Boolean}\nEnum: enum(Off, On, Auto). Field names may be quoted.", 11, "#666666"); guide.TextWrapping = TextWrapping.Wrap; Children.Add(guide);
        _validation.TextWrapping = TextWrapping.Wrap; Children.Add(_validation);
        Expression.TextChanged += (_, _) => { try { _ = ReadType(); _validation.Text = ""; } catch (Exception e) { _validation.Text = e.Message; } };
    }
    public LabType ReadType() => TypeSyntax.Parse(Expression.Text);
}
