namespace LabSpace.Core;

internal static class ExtendedNodeCatalog
{
    public static void Append(List<NodeDefinition> nodes)
    {
        static PortDefinition P(string name, ValueKind type = ValueKind.Number, bool required = true, double value = 0) => new(name, type, required, value);
        void Add(string kind, string title, string group, string glyph, ValueKind output, params PortDefinition[] inputs) => nodes.Add(new(kind, title, group, glyph, title + ". Runs locally in the managed dataflow engine; finite numbers and bounded arrays/strings are required.", inputs, output));
        foreach (var (k, t, g) in new[] { ("negate", "Negate", "−x"), ("increment", "Increment", "+1"), ("decrement", "Decrement", "−1"), ("floor", "Round toward minus infinity", "⌊x⌋"), ("ceiling", "Round toward plus infinity", "⌈x⌉"), ("truncate", "Truncate", "int"), ("exp", "Exponential", "eˣ"), ("log", "Natural logarithm", "ln"), ("log10", "Base 10 logarithm", "log"), ("tan", "Tangent", "tan"), ("sign", "Sign", "sgn"), ("reciprocal", "Reciprocal", "1/x") })
            Add(k, t, "Numeric", g, ValueKind.Number, P("x"));
        Add("pi", "Pi constant", "Numeric", "π", ValueKind.Number);
        Add("atan2", "Inverse tangent (two inputs)", "Numeric", "atan2", ValueKind.Number, P("y"), P("x"));
        Add("remainder", "Remainder", "Numeric", "rem", ValueKind.Number, P("x"), P("y"));
        Add("in-range", "In range", "Comparison", "[x]", ValueKind.Boolean, P("x"), P("minimum"), P("maximum"));
        foreach (var (k, t) in new[] { ("xor", "Exclusive OR"), ("nand", "NAND"), ("nor", "NOR") }) Add(k, t, "Boolean", k, ValueKind.Boolean, P("x", ValueKind.Boolean), P("y", ValueKind.Boolean));
        foreach (var kind in new[] { ValueKind.Boolean, ValueKind.String, ValueKind.Array, ValueKind.Waveform }) Add("select-" + kind.ToString().ToLowerInvariant(), "Select " + kind, "Comparison", "?", kind, P("selector", ValueKind.Boolean), P("x", kind), P("y", kind));
        foreach (var (k, t) in new[] { ("upper", "To upper case"), ("lower", "To lower case"), ("trim", "Trim whitespace"), ("reverse-string", "Reverse Unicode string") }) Add(k, t, "String", "abc", ValueKind.String, P("x", ValueKind.String));
        Add("substring", "String subset", "String", "a[i]", ValueKind.String, P("x", ValueKind.String), P("offset", required: false), P("length", required: false, value: 1000000));
        Add("replace-string", "Search and replace string", "String", "a→b", ValueKind.String, P("x", ValueKind.String), P("search", ValueKind.String), P("replacement", ValueKind.String));
        Add("parse-number", "Decimal string to number", "String", "a→1", ValueKind.Number, P("x", ValueKind.String));
        Add("utf8-length", "UTF-8 byte length", "String", "UTF8", ValueKind.Number, P("x", ValueKind.String));
        Add("initialize-array", "Initialize array", "Array", "[n]", ValueKind.Array, P("value", required: false), P("count", required: false, value: 10));
        Add("concat-array", "Concatenate arrays", "Array", "[+ ]", ValueKind.Array, P("x", ValueKind.Array), P("y", ValueKind.Array));
        foreach (var (k, t) in new[] { ("reverse-array", "Reverse 1D array"), ("sort-array", "Sort 1D array") }) Add(k, t, "Array", "[↔]", ValueKind.Array, P("x", ValueKind.Array));
        Add("subset-array", "Array subset", "Array", "[i:n]", ValueKind.Array, P("x", ValueKind.Array), P("offset", required: false), P("length", required: false, value: 65536));
        Add("replace-array", "Replace array element", "Array", "[i]=", ValueKind.Array, P("x", ValueKind.Array), P("index"), P("value"));
        Add("search-array", "Search 1D array", "Array", "[?]", ValueKind.Number, P("x", ValueKind.Array), P("value"));
        Add("build-waveform", "Build waveform", "Waveform", "Y→~", ValueKind.Waveform, P("x", ValueKind.Array), P("rate", required: false, value: 1000), P("start", required: false));
        Add("waveform-rate", "Waveform sample rate", "Waveform", "Hz", ValueKind.Number, P("x", ValueKind.Waveform));
        Add("waveform-dt", "Waveform sample interval", "Waveform", "dt", ValueKind.Number, P("x", ValueKind.Waveform));
    }
}
