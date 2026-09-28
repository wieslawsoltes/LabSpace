namespace LabSpace.Core;

public sealed record OutputDefinition(string Name, ValueKind Kind);
public sealed record PortDefinition(string Name, ValueKind Kind, bool Required = true, double Default = 0);
public sealed record NodeDefinition(string Kind, string Title, string Category, string Glyph, string Description, PortDefinition[] Inputs, ValueKind Output, bool HasOutput = true)
{
    public OutputDefinition[] Outputs { get; init; } = HasOutput ? [new("result", Output)] : [];
    public OutputDefinition? FindOutput(string name) => Outputs.FirstOrDefault(p => p.Name == name) ?? (name == "result" && Outputs.Length > 0 ? Outputs[0] : null);
    public bool IsControl => Kind is "control" or "bool-control" or "string-control";
    public bool IsIndicator => Kind is "indicator" or "bool-indicator" or "string-indicator" or "graph" or "chart" or "array-indicator";
    public bool IsStructure => Kind is "for" or "while" or "case" or "subvi";
}

public static class NodeCatalog
{
    private static PortDefinition N(string name, bool required = true, double value = 0) => new(name, ValueKind.Number, required, value);
    private static PortDefinition B(string name) => new(name, ValueKind.Boolean);
    private static PortDefinition S(string name) => new(name, ValueKind.String);
    private static PortDefinition A(string name) => new(name, ValueKind.Array);
    private static PortDefinition W(string name) => new(name, ValueKind.Waveform);
    public static readonly IReadOnlyList<NodeDefinition> All = Create();
    private static readonly IReadOnlyDictionary<string, NodeDefinition> Map = All.ToDictionary(x => x.Kind);
    public static NodeDefinition Get(string kind) => Map.TryGetValue(kind, out var value) ? value : throw new ArgumentException($"Unknown function '{kind}'.");
    public static NodeDefinition Resolve(Node node) => NodeResolver.Resolve(node);
    public static bool TryGet(string kind, out NodeDefinition definition) => Map.TryGetValue(kind, out definition!);
    private static List<NodeDefinition> Create()
    {
        var result = new List<NodeDefinition>();
        void Add(string k, string title, string category, string glyph, string help, ValueKind output, bool hasOutput, params PortDefinition[] inputs) => result.Add(new(k, title, category, glyph, help, inputs, output, hasOutput));
        Add("control", "Numeric control", "Controls", "DBL", "Editable front-panel numeric input.", ValueKind.Number, true);
        Add("constant", "Numeric constant", "Numeric", "DBL", "A finite double-precision numeric constant.", ValueKind.Number, true);
        Add("bool-control", "Boolean control", "Controls", "T/F", "Front-panel Boolean switch.", ValueKind.Boolean, true);
        Add("bool", "Boolean constant", "Boolean", "T/F", "TRUE when Value is nonzero; otherwise FALSE.", ValueKind.Boolean, true);
        Add("string-control", "String control", "Controls", "abc", "Front-panel string input.", ValueKind.String, true);
        Add("string", "String constant", "String", "abc", "Constant text, configured in the inspector.", ValueKind.String, true);
        Add("indicator", "Numeric indicator", "Indicators", "DBL", "Displays a numeric result on the front panel.", ValueKind.Number, false, N("x"));
        Add("bool-indicator", "Boolean LED", "Indicators", "LED", "Displays a Boolean result on the front panel.", ValueKind.Boolean, false, B("x"));
        Add("string-indicator", "String indicator", "Indicators", "abc", "Displays a string on the front panel.", ValueKind.String, false, S("x"));
        Add("array-indicator", "Array indicator", "Indicators", "[ ]", "Displays up to the first visible rows of an array.", ValueKind.Array, false, A("x"));
        Add("graph", "Waveform graph", "Indicators", "~", "Displays the current waveform block.", ValueKind.Waveform, false, W("x"));
        Add("chart", "Waveform chart", "Indicators", "~+", "Displays bounded recent waveform history.", ValueKind.Waveform, false, W("x"));
        foreach (var (k, t, g) in new[] { ("add", "Add", "+"), ("subtract", "Subtract", "−"), ("multiply", "Multiply", "×"), ("divide", "Divide", "÷"), ("power", "Power", "xⁿ"), ("min", "Minimum", "min"), ("max", "Maximum", "max") })
            Add(k, t, "Numeric", g, t + " two numeric values. Non-finite results are reported as runtime errors.", ValueKind.Number, true, N("x"), N("y"));
        foreach (var (k, t, g) in new[] { ("sin", "Sine", "sin"), ("cos", "Cosine", "cos"), ("sqrt", "Square root", "√"), ("abs", "Absolute value", "|x|"), ("round", "Round", "≈") })
            Add(k, t, "Numeric", g, t + " of x. Trigonometric arguments are in radians.", ValueKind.Number, true, N("x"));
        foreach (var (k, t, g) in new[] { ("greater", "Greater than", ">"), ("less", "Less than", "<"), ("equal", "Equal", "=") })
            Add(k, t, "Comparison", g, "Compares two double-precision values.", ValueKind.Boolean, true, N("x"), N("y"));
        Add("and", "AND", "Boolean", "AND", "Logical conjunction.", ValueKind.Boolean, true, B("x"), B("y"));
        Add("or", "OR", "Boolean", "OR", "Logical disjunction.", ValueKind.Boolean, true, B("x"), B("y"));
        Add("not", "NOT", "Boolean", "NOT", "Logical negation.", ValueKind.Boolean, true, B("x"));
        Add("select", "Select", "Comparison", "?", "Selects x when selector is TRUE, otherwise y. Both dependencies are evaluated.", ValueKind.Number, true, B("selector"), N("x"), N("y"));
        Add("concat", "Concatenate", "String", "a+b", "Concatenates two strings; output is limited to one million characters.", ValueKind.String, true, S("x"), S("y"));
        Add("length", "String length", "String", "len", "Returns the UTF-16 code-unit length.", ValueKind.Number, true, S("x"));
        Add("format", "Number to string", "String", "1→a", "Formats x with invariant-culture G7 formatting.", ValueKind.String, true, N("x"));
        Add("array", "Array constant", "Array", "[ ]", "Comma-separated invariant-culture numeric values.", ValueKind.Array, true);
        Add("build-array", "Build array", "Array", "[xy]", "Creates a two-element numeric array.", ValueKind.Array, true, N("x"), N("y"));
        Add("array-size", "Array size", "Array", "size", "Returns the element count.", ValueKind.Number, true, A("x"));
        Add("index", "Index array", "Array", "[i]", "Returns an element at a zero-based integer index; out-of-range indexes are errors.", ValueKind.Number, true, A("x"), N("index"));
        Add("sum", "Sum array", "Array", "Σ", "Sums numeric elements.", ValueKind.Number, true, A("x"));
        Add("mean", "Mean array", "Array", "μ", "Arithmetic mean; an empty array is an error.", ValueKind.Number, true, A("x"));
        Add("simulate", "Simulate signal", "Signal Processing", "~", "SIMULATED source. Sine, Square or Triangle. Parameters: count, rate, noise. Inputs default to amplitude 1 and frequency 10 Hz.", ValueKind.Waveform, true, N("amplitude", false, 1), N("frequency", false, 10));
        Add("gain", "Waveform gain", "Signal Processing", "×~", "Multiplies each sample by gain.", ValueKind.Waveform, true, W("x"), N("gain", false, 1));
        Add("offset", "Waveform offset", "Signal Processing", "+~", "Adds offset to each sample.", ValueKind.Waveform, true, W("x"), N("offset", false));
        Add("filter", "Moving average", "Signal Processing", "LP", "Causal moving average within each input block. Parameter: window (1–1024). Filter history resets at each block.", ValueKind.Waveform, true, W("x"));
        Add("rms", "RMS", "Signal Processing", "RMS", "Stable root-mean-square amplitude of a waveform.", ValueKind.Number, true, W("x"));
        Add("peak", "Peak to peak", "Signal Processing", "p-p", "Maximum sample minus minimum sample.", ValueKind.Number, true, W("x"));
        Add("fft", "FFT magnitude", "Signal Processing", "FFT", "One-sided radix-2 amplitude spectrum. Input must have power-of-two sample count. Hann window, coherent-gain normalization; x-axis is frequency.", ValueKind.Waveform, true, W("x"));
        Add("to-array", "Waveform to array", "Signal Processing", "Y[]", "Extracts the immutable samples as an array.", ValueKind.Array, true, W("x"));
        Add("time", "Elapsed time", "Timing", "t", "Logical acquisition time in seconds, advanced between completed frames.", ValueKind.Number, true);
        Add("random", "Random number", "Numeric", "?01", "Deterministic pseudo-random value in [0,1), reproducible for a fresh runtime.", ValueKind.Number, true);
        Add("feedback", "Feedback node", "Structures", "z⁻¹", "Returns previous completed frame input. Value is the initial state. Explicitly breaks a dataflow cycle.", ValueKind.Number, true, N("x", false));
        Add("input", "Connector input", "Structures", "in", "Typed argument in a nested diagram. Text names the tunnel, register or iteration terminal.", ValueKind.Number, true);
        Add("output", "Connector output", "Structures", "out", "Typed named result of a nested diagram.", ValueKind.Number, false, N("x"));
        Add("stop", "Loop condition", "Structures", "STOP", "Stops a while loop after the current iteration when TRUE.", ValueKind.Boolean, false, B("x"));
        Add("for", "For Loop", "Structures", "FOR", "For Loop with optional typed tunnels, auto-indexing and stacked shift registers. Double-click to edit its body.", ValueKind.Number, true, N("count", false, 10), N("initial", false));
        Add("while", "While Loop", "Structures", "WHILE", "While Loop with typed tunnels and shift registers. Safety limit: 10,000 iterations and a shared node budget.", ValueKind.Number, true, N("initial", false));
        Add("case", "Case Structure", "Structures", "CASE", "Executes only the selected branch. TRUE uses Body; FALSE uses Alternative. Supports named typed tunnels.", ValueKind.Number, true, B("selector"), N("initial", false));
        Add("subvi", "SubVI", "Structures", "VI", "Executes an embedded reusable diagram with a named typed connector contract. Double-click to edit the body.", ValueKind.Number, true, N("x", false));
        ExtendedNodeCatalog.Append(result);
        return result;
    }
}
