namespace LabSpace.Core;

internal static class ProgrammingNodeCatalog
{
    public static void Append(List<NodeDefinition> nodes)
    {
        static PortDefinition P(string name, ValueKind kind, bool required = true) => new(name, kind, required);
        void Add(string kind, string title, string glyph, ValueKind output, params PortDefinition[] inputs) => nodes.Add(new(kind, title, "Programming", glyph, title + ". Independently implemented typed function.", inputs, output, !kind.EndsWith("-indicator", StringComparison.Ordinal)));
        Add("sequence", "Stacked Sequence", "SEQ", ValueKind.Number);
        Add("waveform-constant", "Waveform constant", "Y(t)", ValueKind.Waveform);
        Add("formula", "Formula Node", "f(x)", ValueKind.Number);
        Add("error-constant", "Error cluster constant", "error", ValueKind.Error);
        Add("error-indicator", "Error cluster indicator", "error", ValueKind.Error, P("x", ValueKind.Error));
        Add("error-bundle", "Bundle error", "error", ValueKind.Error, P("status", ValueKind.Boolean), P("code", ValueKind.Number), P("source", ValueKind.String));
        Add("error-clear", "Clear error", "clear", ValueKind.Error, P("x", ValueKind.Error));
        Add("error-merge", "Merge errors", "merge", ValueKind.Error, P("x", ValueKind.Error), P("y", ValueKind.Error));
        nodes.Add(new("error-unbundle", "Unbundle error", "Programming", "error", "Extracts status, signed 32-bit code and source as distinct outputs.", [P("x", ValueKind.Error)], ValueKind.Boolean)
        { Outputs = [new("status", ValueKind.Boolean), new("code", ValueKind.Number), new("source", ValueKind.String)] });
        Add("complex-constant", "Complex constant", "CDB", ValueKind.Complex);
        Add("complex-indicator", "Complex indicator", "CDB", ValueKind.Complex, P("x", ValueKind.Complex));
        Add("complex-build", "Re/Im to Complex", "a+bi", ValueKind.Complex, P("real", ValueKind.Number), P("imaginary", ValueKind.Number));
        nodes.Add(new("complex-parts", "Complex to Re/Im", "Programming", "a,b", "Extracts real and imaginary components.", [P("x", ValueKind.Complex)], ValueKind.Number)
        { Outputs = [new("real", ValueKind.Number), new("imaginary", ValueKind.Number)] });
        foreach (var (kind, title, glyph) in new[] { ("complex-add", "Add complex", "+"), ("complex-subtract", "Subtract complex", "−"), ("complex-multiply", "Multiply complex", "×"), ("complex-divide", "Divide complex", "÷") })
            Add(kind, title, glyph, ValueKind.Complex, P("x", ValueKind.Complex), P("y", ValueKind.Complex));
        Add("complex-conjugate", "Complex conjugate", "z*", ValueKind.Complex, P("x", ValueKind.Complex));
        Add("complex-magnitude", "Complex magnitude", "|z|", ValueKind.Number, P("x", ValueKind.Complex));
        Add("complex-phase", "Complex phase", "arg", ValueKind.Number, P("x", ValueKind.Complex));
        Add("select-error", "Select error cluster", "?", ValueKind.Error, P("selector", ValueKind.Boolean), P("x", ValueKind.Error), P("y", ValueKind.Error));
        Add("select-complex", "Select complex", "?", ValueKind.Complex, P("selector", ValueKind.Boolean), P("x", ValueKind.Complex), P("y", ValueKind.Complex));
    }
}
