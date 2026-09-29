namespace LabSpace.Core;

/// <summary>Independently implemented structures and typed primitives.</summary>
public static class AdvancedNodeCatalog
{
    public static IReadOnlyList<NodeDefinition> All { get; } = Create();
    private static NodeDefinition[] Create()
    {
        var nodes = new List<NodeDefinition>();
        void Add(string kind, string title, string category, string glyph, string help, ValueKind output, bool hasOutput, params PortDefinition[] inputs)
            => nodes.Add(new(kind, title, category, glyph, help, inputs, output, hasOutput));
        var number = ValueKind.Number; var error = ValueKind.Error; var complex = ValueKind.Complex;
        Add("case-multi", "Case Structure", "Structures", "?", "Typed Boolean, numeric, string or error selector. Only the matching frame executes. Numeric labels support inclusive ranges; string labels support quoted lists and half-open ordinal ranges.", number, true);
        Add("sequence", "Stacked Sequence", "Structures", "0..", "Ordered frames. Typed sequence locals become available only in later frames. External outputs publish after the last frame.", number, true);
        Add("sequence-read", "Sequence local read", "Structures", "L→", "Reads a typed value written by an earlier frame. Text names the sequence local.", number, true);
        Add("sequence-write", "Sequence local write", "Structures", "→L", "Writes a named typed sequence local for later frames. Exactly one frame may define each local.", number, false, new PortDefinition("x", number));
        Add("formula", "Formula Node", "Structures", "f(x)", "Compiled scalar formulas with named inputs/outputs, assignments, arithmetic, comparisons, short-circuit logical expressions, ternary selection and math functions. No external code or I/O.", number, true, new PortDefinition("x", number));
        Add("error-constant", "Error cluster constant", "Error Handling", "err", "Status, signed 32-bit code and source. A nonzero code with FALSE status is a warning.", error, true);
        Add("error-control", "Error cluster control", "Controls", "err", "Editable error cluster input.", error, true);
        Add("error-indicator", "Error cluster indicator", "Indicators", "err", "Displays status, code and source from an actual error wire.", error, false, new PortDefinition("x", error));
        Add("error-bundle", "Bundle error", "Error Handling", "err+", "Builds an immutable standard error cluster.", error, true, new PortDefinition("status", ValueKind.Boolean, false), new PortDefinition("code", number, false), new PortDefinition("source", ValueKind.String, false));
        Add("error-unbundle", "Unbundle error", "Error Handling", "err−", "Reads independent status, code and source outputs.", ValueKind.Boolean, true, new PortDefinition("x", error));
        nodes[^1] = nodes[^1] with { Outputs = [new("status", ValueKind.Boolean), new("code", number), new("source", ValueKind.String)] };
        Add("error-merge", "Merge errors", "Error Handling", "err∨", "First error wins; otherwise first warning; otherwise success. Input order is x then y.", error, true, new PortDefinition("x", error, false), new PortDefinition("y", error, false));
        Add("error-clear", "Clear errors", "Error Handling", "err0", "Consumes the incoming error dependency and returns no error.", error, true, new PortDefinition("x", error, false));
        Add("complex", "Complex constant", "Numeric", "CDB", "Finite double-precision real and imaginary components.", complex, true);
        Add("complex-control", "Complex control", "Controls", "CDB", "Editable complex front-panel input.", complex, true);
        Add("complex-indicator", "Complex indicator", "Indicators", "CDB", "Displays the real and imaginary parts of a complex result.", complex, false, new PortDefinition("x", complex));
        Add("complex-build", "Re/Im to complex", "Numeric", "a+bi", "Constructs a complex number from real and imaginary parts.", complex, true, new PortDefinition("real", number), new PortDefinition("imaginary", number, false));
        Add("complex-parts", "Complex to Re/Im", "Numeric", "ReIm", "Independent real and imaginary outputs.", number, true, new PortDefinition("x", complex));
        nodes[^1] = nodes[^1] with { Outputs = [new("real", number), new("imaginary", number)] };
        foreach (var (kind, title, glyph) in new[] { ("add", "Add", "+"), ("subtract", "Subtract", "−"), ("multiply", "Multiply", "×"), ("divide", "Divide", "÷") })
            Add("complex-" + kind, title + " complex", "Numeric", glyph + "i", title + " finite complex operands.", complex, true, new PortDefinition("x", complex), new PortDefinition("y", complex));
        Add("complex-conjugate", "Complex conjugate", "Numeric", "z*", "Conjugates the imaginary part.", complex, true, new PortDefinition("x", complex));
        Add("complex-magnitude", "Complex magnitude", "Numeric", "|z|", "Stable complex magnitude.", number, true, new PortDefinition("x", complex));
        Add("complex-phase", "Complex phase", "Numeric", "arg", "Phase angle in radians.", number, true, new PortDefinition("x", complex));
        return nodes.ToArray();
    }
}
