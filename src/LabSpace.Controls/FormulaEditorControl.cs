using System.Collections.Immutable;
using LabSpace.Core;
using Microsoft.UI.Xaml.Media;

namespace LabSpace.Controls;

/// <summary>Text input uses native text services while compilation and commit remain host-independent.</summary>
public sealed class FormulaEditorControl : StackPanel
{
    public LabTextBox Inputs { get; }
    public LabTextBox Outputs { get; }
    public LabTextBox Source { get; }
    public Dictionary<string, FrameworkElement> Fields { get; } = [];
    public FormulaEditorControl(Node node)
    {
        Padding = new(12); Spacing = 7;
        var signature = node.Formula ?? new();
        Inputs = new(string.Join(", ", signature.Inputs), "Input names");
        Outputs = new(string.Join(", ", signature.Outputs), "Output names");
        Source = new(node.Text, "Formula source") { AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, Height = 275, FontSize = 14, FontFamily = new FontFamily("monospace") };
        Children.Add(LabTheme.Text("Inputs (comma-separated, read-only variables)", 12)); Children.Add(Inputs);
        Children.Add(LabTheme.Text("Outputs (each output must be assigned)", 12)); Children.Add(Outputs);
        Children.Add(LabTheme.Text("Formula", 12)); Children.Add(Source);
        var hint = LabTheme.Text("Assignments, arithmetic, comparisons, &&, ||, ?: and math functions. For example: gain = max(x, 0); result = gain * 2;\nFinite scalar numbers only. No loops, native calls or external code execution.", 11, "#555555"); hint.TextWrapping = TextWrapping.Wrap; Children.Add(hint);
        Fields["formula-inputs"] = Inputs; Fields["formula-outputs"] = Outputs; Fields["formula-source"] = Source;
    }
    public FormulaSignature ReadSignature() => new()
    {
        Inputs = Names(Inputs.Text), Outputs = Names(Outputs.Text)
    };
    private static ImmutableArray<string> Names(string text) => text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToImmutableArray();
}
