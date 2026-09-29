using LabSpace.Core;

namespace LabSpace.Dataflow;

public sealed record Diagnostic(string Code, string Message, string? NodeId = null, string? WireId = null);
public sealed class GraphValidationException(IReadOnlyList<Diagnostic> diagnostics) : Exception(string.Join("\n", diagnostics.Select(x => x.Message)))
{
    public IReadOnlyList<Diagnostic> Diagnostics { get; } = diagnostics;
}
public readonly record struct SourceTerminal(string NodeId, string Output);
public sealed record CompiledNode(Node Model, NodeDefinition Definition, IReadOnlyDictionary<string, SourceTerminal> Sources, CompiledGraph? Body, CompiledGraph? Alternative)
{
    public FrameProgram? Frames { get; init; }
    public FormulaProgram? Formula { get; init; }
}
public sealed record CompiledGraph(IReadOnlyList<CompiledNode> Order)
{
    public IReadOnlyDictionary<string, string> ConnectorOutputs { get; init; } = new Dictionary<string, string>();
    public string? ConditionNode { get; init; }
}

public static partial class GraphCompiler
{
    public static IReadOnlyList<Diagnostic> Validate(Diagram graph)
    {
        try { Compile(graph); return []; } catch (GraphValidationException e) { return e.Diagnostics; }
    }
    public static CompiledGraph Compile(Diagram graph) => Compile(graph, 0, new(ReferenceEqualityComparer.Instance), null);
    private static CompiledGraph Compile(Diagram graph, int depth, HashSet<Diagram> ancestors, HashSet<string>? defaultOutputs)
    {
        if (graph is null || depth > 12 || !ancestors.Add(graph)) throw new GraphValidationException([new("DEPTH", "Nested diagrams exceed the depth limit or contain recursive references.")]);
        var errors = new List<Diagnostic>();
        try
        {
            if (graph.Nodes.Count > 4096 || graph.Wires.Count > 16384) throw new GraphValidationException([new("LIMIT", "Diagram exceeds the node or wire limit.")]);
            var nodes = new Dictionary<string, Node>(StringComparer.Ordinal);
            var definitions = new Dictionary<string, NodeDefinition>(StringComparer.Ordinal);
            foreach (var node in graph.Nodes)
            {
                if (string.IsNullOrWhiteSpace(node.Id) || !nodes.TryAdd(node.Id, node)) { errors.Add(new("ID", "Every node must have a unique nonempty ID.", node.Id)); continue; }
                if (!NodeCatalog.TryGet(node.Kind, out var basis)) errors.Add(new("UNKNOWN", $"Unknown function '{node.Kind}'.", node.Id));
                else
                {
                    var before = errors.Count;
                    if (node.Contract is not null) ValidateContract(node, basis, errors);
                    if ((node.Kind is "sequence" or "formula" || node.Frames.Count > 0) && node.Contract is null) errors.Add(new("CONTRACT", "A frame structure or formula requires a connector contract.", node.Id));
                    if (errors.Count == before) definitions[node.Id] = NodeCatalog.Resolve(node);
                }
                if (!Enum.IsDefined(node.DataType)) errors.Add(new("TYPE", "Unknown connector data type.", node.Id));
                if (!double.IsFinite(node.Value) || node.Parameters.Values.Any(v => !double.IsFinite(v))) errors.Add(new("VALUE", "Node parameters must be finite numbers.", node.Id));
            }
            var inputs = nodes.Keys.ToDictionary(x => x, _ => new Dictionary<string, SourceTerminal>(StringComparer.Ordinal));
            var edges = nodes.Keys.ToDictionary(x => x, _ => new List<string>());
            var indegree = nodes.Keys.ToDictionary(x => x, _ => 0); var wireIds = new HashSet<string>();
            foreach (var wire in graph.Wires)
            {
                if (string.IsNullOrWhiteSpace(wire.Id) || !wireIds.Add(wire.Id)) errors.Add(new("ID", "Wire IDs must be nonempty and unique.", WireId: wire.Id));
                if (!definitions.TryGetValue(wire.From, out var from) || !definitions.TryGetValue(wire.To, out var to)) { errors.Add(new("DANGLING", "Wire references a missing node.", WireId: wire.Id)); continue; }
                var port = to.Inputs.FirstOrDefault(p => p.Name == wire.Input); var source = from.FindOutput(wire.Output);
                if (port is null || source is null) { errors.Add(new("PORT", "Wire references a missing input or output terminal.", wire.To, wire.Id)); continue; }
                if (source.Kind != port.Kind) errors.Add(new("TYPE", $"Cannot wire {source.Kind} to {port.Kind} input '{port.Name}'.", wire.To, wire.Id));
                if (!inputs[wire.To].TryAdd(wire.Input, new(wire.From, source.Name))) errors.Add(new("DRIVER", $"Input '{port.Name}' has more than one source.", wire.To, wire.Id));
                if (to.Kind != "feedback") { edges[wire.From].Add(wire.To); indegree[wire.To]++; }
            }
            foreach (var node in nodes.Values)
                if (definitions.TryGetValue(node.Id, out var def))
                    foreach (var port in def.Inputs.Where(p => p.Required))
                        if (!inputs[node.Id].ContainsKey(port.Name) && !(node.Kind == "output" && defaultOutputs?.Contains(ConnectorName(node)) == true))
                            errors.Add(new("UNWIRED", $"{node.Label}: required input '{port.Name}' is unwired.", node.Id));
            if (errors.Count != 0) throw new GraphValidationException(errors);
            var ready = new Queue<string>(graph.Nodes.Where(x => indegree[x.Id] == 0).Select(x => x.Id)); var ordered = new List<CompiledNode>(nodes.Count);
            while (ready.TryDequeue(out var id))
            {
                var model = nodes[id]; var definition = definitions[id]; CompiledGraph? body = null, alternative = null;
                FrameProgram? frames = null; FormulaProgram? formula = null;
                if (model.Kind == "formula")
                {
                    try { formula = FormulaProgram.Compile(model.Text, model.Contract!); }
                    catch (ArgumentException ex) { errors.Add(new("FORMULA", ex.Message, id)); }
                }
                else if (model.Frames.Count > 0 || model.Kind == "sequence") frames = CompileFrames(model, depth, ancestors, errors);
                else if (definition.IsStructure)
                {
                    var defaults = model.Contract?.Outputs.Where(t => t.UseDefaultIfUnwired).Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
                    if (model.Body is null) errors.Add(new("BODY", $"{model.Label}: structure body is missing.", id));
                    else { body = Compile(model.Body, depth + 1, ancestors, defaults); ValidateBody(model, model.Body, errors); }
                    if (model.Kind == "case")
                    {
                        if (model.Alternative is null) errors.Add(new("BODY", "Case structure requires both branches.", id));
                        else { alternative = Compile(model.Alternative, depth + 1, ancestors, defaults); ValidateBody(model, model.Alternative, errors); }
                    }
                }
                ordered.Add(new(model, definition, inputs[id], body, alternative) { Frames = frames, Formula = formula });
                foreach (var target in edges[id]) if (--indegree[target] == 0) ready.Enqueue(target);
            }
            if (ordered.Count != nodes.Count) errors.Add(new("CYCLE", "Combinational cycle detected. Insert an explicit Feedback node to carry state between frames."));
            var connectors = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var node in graph.Nodes.Where(n => n.Kind == "output"))
                if (!connectors.TryAdd(ConnectorName(node), node.Id)) errors.Add(new("CONNECTOR", $"Duplicate output connector '{ConnectorName(node)}'.", node.Id));
            if (errors.Count != 0) throw new GraphValidationException(errors);
            return new(ordered) { ConnectorOutputs = connectors, ConditionNode = graph.Nodes.FirstOrDefault(n => n.Kind == "stop")?.Id };
        }
        finally { ancestors.Remove(graph); }
    }
    public static string ConnectorName(Node node) => string.IsNullOrWhiteSpace(node.Text) ? "result" : node.Text;
    private static void ValidateContract(Node node, NodeDefinition basis, List<Diagnostic> errors)
    {
        var c = node.Contract!;
        void Error(string message) => errors.Add(new("CONTRACT", message, node.Id));
        if (!basis.IsStructure && node.Kind != "formula") { Error("Only structures and formulas can declare a connector contract."); return; }
        if (c.Inputs.IsDefault || c.Outputs.IsDefault || c.Registers.IsDefault || c.Inputs.Length > 32 || c.Outputs.Length > 32 || c.Registers.Length > 16) { Error("Invalid or excessive structure terminals."); return; }
        if (c.Inputs.Any(t => t is null) || c.Outputs.Any(t => t is null) || c.Registers.Any(t => t is null)) { Error("Null structure terminal."); return; }
        ValidateProgrammingContract(node, errors);
        var namesIn = new HashSet<string>(["i", "N", "count", "selector"], StringComparer.Ordinal);
        var namesOut = new HashSet<string>(StringComparer.Ordinal);
        static bool Name(string name) => !string.IsNullOrWhiteSpace(name) && name.Length <= 64 && !name.Contains(':') && name.All(ch => char.IsLetterOrDigit(ch) || ch == '_');
        foreach (var t in c.Inputs)
        {
            if (t is null || !Name(t.Name) || !namesIn.Add(t.Name) || !Enum.IsDefined(t.Type)) { Error("Input tunnel names must be unique identifiers, with a supported type."); continue; }
            if (t.Indexing && (node.Kind is not ("for" or "while") || t.Type != ValueKind.Number)) Error("Input indexing requires a loop and a numeric element type.");
        }
        foreach (var t in c.Outputs)
        {
            if (t is null || !Name(t.Name) || !namesOut.Add(t.Name) || !Enum.IsDefined(t.Type) || !Enum.IsDefined(t.Mode)) { Error("Output tunnel names and modes must be valid and unique."); continue; }
            if (t.Mode != TunnelMode.LastValue && node.Kind is not ("for" or "while")) Error("Only loops support output collection modes.");
            if (t.Mode is TunnelMode.Indexing or TunnelMode.ConditionalIndexing && t.Type != ValueKind.Number) Error("Indexed output tunnels currently collect numeric scalar elements.");
            if (t.Mode == TunnelMode.Concatenating && t.Type != ValueKind.Array) Error("Concatenating outputs require numeric arrays.");
            if (t.Mode == TunnelMode.ConditionalIndexing && !Name(t.Condition)) Error("Conditional indexing requires a named Boolean output connector.");
        }
        foreach (var r in c.Registers)
        {
            if (r is null || !Name(r.Name) || !namesIn.Add(r.Name) || !namesOut.Add(r.Name) || !Enum.IsDefined(r.Type) || r.HistoryDepth is < 1 or > 16) { Error("Invalid, duplicate or excessive shift register."); continue; }
            if (node.Kind is not ("for" or "while")) Error("Shift registers belong to For or While loops.");
        }
        if (namesOut.Count > 0 && !namesOut.Contains(c.PrimaryOutput)) Error("Primary output must name an output tunnel or shift register.");
        if (c.ConditionalFor && node.Kind != "for") Error("A conditional For terminal can only be used on a For loop.");
    }
    private static void ValidateBody(Node owner, Diagram body, List<Diagnostic> errors)
    {
        void Error(string message, string? nodeId = null) => errors.Add(new("CONNECTOR", message, nodeId ?? owner.Id));
        var outputs = body.Nodes.Where(n => n.Kind == "output").ToArray();
        var needsStop = owner.Kind == "while" || owner.Contract?.ConditionalFor == true;
        if (needsStop && body.Nodes.Count(n => n.Kind == "stop") != 1) errors.Add(new("CONDITION", "A conditional loop requires exactly one Loop condition terminal.", owner.Id));
        if (owner.Contract is not { } c)
        {
            if (outputs.Length != 1 || outputs[0].DataType != ValueKind.Number) Error("A legacy nested diagram must have exactly one numeric Connector output.");
            return;
        }
        var inputs = c.Inputs.ToDictionary(t => t.Name, t => t.Type, StringComparer.Ordinal);
        if (owner.Kind is "for" or "while") { inputs["i"] = ValueKind.Number; if (owner.Kind == "for") inputs["N"] = ValueKind.Number; }
        var expected = c.Outputs.ToDictionary(t => t.Name, t => t.Type, StringComparer.Ordinal);
        foreach (var r in c.Registers)
        {
            expected[r.Name] = r.Type;
            for (var i = 0; i < r.HistoryDepth; i++) inputs[i == 0 ? r.Name : r.Name + ":" + i] = r.Type;
        }
        foreach (var t in c.Outputs.Where(t => t.Mode == TunnelMode.ConditionalIndexing))
        {
            if (expected.TryGetValue(t.Condition, out var kind) && kind != ValueKind.Boolean) Error("An indexing condition must be Boolean.");
            expected[t.Condition] = ValueKind.Boolean;
        }
        foreach (var node in body.Nodes.Where(n => n.Kind == "input"))
            if (!inputs.TryGetValue(node.Text, out var kind) || kind != node.DataType) Error($"Input connector '{node.Text}' does not match the structure contract.", node.Id);
        foreach (var node in outputs)
            if (!expected.TryGetValue(ConnectorName(node), out var kind) || kind != node.DataType) Error($"Output connector '{ConnectorName(node)}' does not match the structure contract.", node.Id);
        foreach (var (name, _) in expected)
            if (!outputs.Any(n => ConnectorName(n) == name) && !c.Outputs.Any(t => t.Name == name && t.UseDefaultIfUnwired)) Error($"Missing output connector '{name}'.");
    }
}
