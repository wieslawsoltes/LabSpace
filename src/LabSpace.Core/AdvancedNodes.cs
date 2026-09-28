namespace LabSpace.Core;

/// <summary>Typed node schemas shared by compiler, editor, palette, hit testing and renderer.</summary>
public static class AdvancedNodes
{
    public static bool IsTypedStructure(string kind) => kind is "for-loop" or "while-loop" or "case-typed" or "subvi-typed";
    public static readonly IReadOnlyList<NodeDefinition> Definitions = Build();
    private static readonly HashSet<string> Kinds = Definitions.Select(d => d.Kind).ToHashSet(StringComparer.Ordinal);
    public static bool Contains(string kind) => Kinds.Contains(kind);
    private static List<NodeDefinition> Build()
    {
        var list = new List<NodeDefinition>();
        void Add(string kind, string title, string category, string glyph, string help, ValueKind type = ValueKind.Number, bool output = true) => list.Add(new(kind, title, category, glyph, help, [], type, output));
        Add("typed-constant", "Typed constant", "Data Types", "I32", "Exact integers, complex numbers, enums, arrays, clusters and error data. Edit the wire type and JSON literal.");
        Add("typed-control", "Typed control", "Controls", "I32", "A front-panel control with an explicit structural wire type.");
        Add("typed-indicator", "Typed indicator", "Indicators", "I32", "Displays any supported structural value.", output: false);
        Add("convert", "Numeric conversion", "Data Types", "→I", "Coerces a DBL into the configured numeric representation. Real-to-integer conversion rounds to even and saturates.");
        Add("bundle", "Bundle by name", "Cluster", "{+}", "Creates a cluster from explicitly ordered, named and typed fields.", ValueKind.Cluster);
        Add("unbundle", "Unbundle by name", "Cluster", "{−}", "Exposes one independently wireable output per cluster field.");
        Add("to-variant", "To Variant", "Cluster", "→V", "Wraps the input without losing its type or value.", ValueKind.Variant);
        Add("from-variant", "Variant to Data", "Cluster", "V→", "Extracts a variant as the configured structural type; incompatible values are errors.");
        Add("error", "Build error cluster", "Error Handling", "!", "Explicit status, signed I32 code and source. No exception is silently converted to a measurement.", ValueKind.Error);
        Add("error-fields", "Unbundle error", "Error Handling", "!→", "Independently wireable status, code and source outputs.");
        Add("clear-error", "Clear errors", "Error Handling", "!✓", "Returns a no-error cluster.", ValueKind.Error);
        Add("merge-errors", "Merge errors", "Error Handling", "!!", "Returns the first error, otherwise the first warning, otherwise no error.", ValueKind.Error);
        foreach (var (k, title, glyph) in new[] { ("array-index", "Index typed array", "[i]"), ("array-replace", "Replace array subset", "[←]"), ("array-concat", "Concatenate arrays", "[++ ]"), ("array-reverse", "Reverse array", "[↔]"), ("array-transpose", "Transpose 2D array", "[T]"), ("array-reshape", "Reshape array", "[n,m]"), ("array-build", "Build typed array", "[+]"), ("array-sort", "Sort numeric array", "[↑]") })
            Add(k, title, "Array", glyph, "Rank-aware immutable array operation. Configure the element/wire type in Properties.");
        Add("tunnel-in", "Input tunnel", "Structure Terminals", "in", "Text identifies a declared typed input tunnel.");
        Add("tunnel-out", "Output tunnel", "Structure Terminals", "out", "Text identifies an output tunnel. Optional include input controls conditional collection.", output: false);
        Add("shift-read", "Left shift register", "Structure Terminals", "↓", "Reads a previous iteration. Text is the register name; element selects a stacked history slot.");
        Add("shift-write", "Right shift register", "Structure Terminals", "↑", "Stages the register value for the next iteration. All registers advance simultaneously.", output: false);
        Add("iteration", "Iteration terminal", "Structure Terminals", "i", "Zero-based loop iteration, I32.", ValueKind.Int32);
        Add("loop-count", "Count terminal", "Structure Terminals", "N", "Effective For Loop iteration count after input-array bounds, I32.", ValueKind.Int32);
        Add("for-loop", "For Loop with tunnels", "Typed Structures", "FOR", "Typed input/output tunnels, shortest-array auto-indexing, conditional/concatenating outputs and stacked shift registers.");
        Add("while-loop", "While Loop with tunnels", "Typed Structures", "WHILE", "Post-test loop with typed tunnels and registers. Out-of-range indexed inputs use defaults. Bounded execution.");
        Add("case-typed", "Typed Case Structure", "Typed Structures", "CASE", "Executes only the selected branch. Declared outputs must be wired in each branch unless Use Default if Unwired is enabled.");
        Add("subvi-typed", "Typed SubVI", "Typed Structures", "VI", "An embedded reusable diagram with multiple named typed inputs and outputs.");
        return list;
    }
    public static NodeDefinition Describe(Node node, NodeDefinition definition)
    {
        var type = node.Type ?? LabType.Number;
        PortDefinition P(string name, LabType t, bool required = true, double value = 0) => new(name, t.Kind, required, value, t);
        NodeDefinition With(LabType output, params PortDefinition[] inputs) => definition with { Output = output.Kind, WireType = output, Inputs = inputs };
        switch (node.Kind)
        {
            case "typed-constant": case "typed-control": case "tunnel-in": case "shift-read": return With(type);
            case "typed-indicator": case "shift-write": return With(type, P("x", type));
            case "tunnel-out": return With(type, P("x", type), P("include", LabType.Boolean, false, 1));
            case "iteration": case "loop-count": return With(LabType.Int32);
            case "convert": return With(type, P("x", LabType.Number));
            case "bundle": case "unbundle":
                type = node.Type ?? LabType.Cluster(new("value", LabType.Number), new("enabled", LabType.Boolean));
                if (type.Kind != ValueKind.Cluster || type.Fields.IsEmpty) throw new ArgumentException("Bundle/unbundle needs a nonempty cluster type.");
                return node.Kind == "bundle" ? With(type, type.Fields.Select(f => P(f.Name, f.Type)).ToArray()) : With(type.Fields[0].Type, P("x", type)) with { NamedOutputs = type.Fields.Select(f => P(f.Name, f.Type)).ToArray() };
            case "to-variant": return With(LabType.Scalar(ValueKind.Variant), P("x", type));
            case "from-variant": return With(type, P("x", LabType.Scalar(ValueKind.Variant)));
            case "error": return With(LabType.Error, P("status", LabType.Boolean, false), P("code", LabType.Int32, false), P("source", LabType.String, false));
            case "clear-error": return With(LabType.Error, P("x", LabType.Error));
            case "merge-errors": return With(LabType.Error, P("x", LabType.Error), P("y", LabType.Error));
            case "error-fields": return With(LabType.Boolean, P("x", LabType.Error)) with { NamedOutputs = [P("status", LabType.Boolean), P("code", LabType.Int32), P("source", LabType.String)] };
            case "array-build": return With(type.Collected, P("x", type), P("y", type));
            case "array-index": case "array-replace": case "array-concat": case "array-reverse": case "array-sort": case "array-transpose": case "array-reshape":
                type = node.Type ?? LabType.Array(LabType.Number, node.Kind is "array-transpose" or "array-reshape" ? 2 : 1);
                if (type.Kind != ValueKind.Array) throw new ArgumentException("This function requires an array type.");
                if (node.Kind == "array-index") return With(type.Indexed, P("x", type), P("index", LabType.Int32));
                if (node.Kind == "array-replace") return With(type, P("x", type), P("index", LabType.Int32), P("value", type.Indexed));
                if (node.Kind == "array-concat") return With(type, P("x", type), P("y", type));
                if (node.Kind == "array-reshape") return With(type, P("x", LabType.Array(type.Element!)));
                return With(type, P("x", type));
            case "for-loop": case "while-loop": case "case-typed": case "subvi-typed":
                var contract = node.Contract ?? throw new ArgumentException("Typed structure contract is missing."); contract.Validate();
                var inputs = new List<PortDefinition>();
                if (node.Kind == "for-loop") inputs.Add(P("count", LabType.Int32, false, -1));
                if (node.Kind == "case-typed") inputs.Add(P("selector", LabType.Boolean));
                inputs.AddRange(contract.Inputs.Select(t => P(t.Name, t.OutsideType)));
                inputs.AddRange(contract.Registers.Where(r => r.Initialized).Select(r => P("init:" + r.Name, r.Type, false)));
                var outputs = contract.Outputs.Select(t => P(t.Name, t.OutsideType)).Concat(contract.Registers.Select(r => P("shift:" + r.Name, r.Type))).ToArray();
                if (inputs.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != inputs.Count) throw new ArgumentException("A tunnel uses a reserved connector name.");
                return With(outputs[0].DataType, inputs.ToArray()) with { NamedOutputs = outputs };
            case "add": case "subtract": case "multiply": case "divide": case "power": case "min": case "max":
                if (node.Type is null) return definition;
                if (!(type.IsNumeric || type.Kind == ValueKind.Complex || (type.Kind == ValueKind.Array && (type.Element!.IsNumeric || type.Element.Kind == ValueKind.Complex)))) throw new ArgumentException("Arithmetic needs numeric or complex data.");
                return With(type, P("x", type), P("y", type));
            default: return definition;
        }
    }
}
