using LabSpace.Core;

namespace LabSpace.Dataflow;

public sealed record Diagnostic(string Code, string Message, string? NodeId = null, string? WireId = null, string? StructureId = null);
public sealed class GraphValidationException(IReadOnlyList<Diagnostic> diagnostics) : Exception(string.Join("\n", diagnostics.Select(x => x.Message)))
{
    public IReadOnlyList<Diagnostic> Diagnostics { get; } = diagnostics;
}
public sealed record CompiledNode(Node Model, NodeDefinition Definition, IReadOnlyDictionary<string, PortAddress> Sources, CompiledGraph? Body, CompiledGraph? Alternative)
{
    private string? _literalText;
    private LabType? _literalType;
    private Value? _literal;
    public Value Literal(LabType type)
    {
        if (_literal is not null && _literalText == Model.Text && Equals(_literalType, type)) return _literal;
        var value = Model.Kind == "array" ? Value.Vector(Model.Text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(x => double.Parse(x, System.Globalization.CultureInfo.InvariantCulture))) : ValueLiteral.Parse(type, Model.Text);
        _literalText = Model.Text; _literalType = type; return _literal = value;
    }
}
public sealed record CompiledGraph(IReadOnlyList<CompiledNode> Order)
{
    public CompiledNode[] FeedbackNodes { get; } = Order.Where(n => n.Model.Kind == "feedback").ToArray();
    public string? LegacyOutput { get; } = Order.FirstOrDefault(n => n.Model.Kind == "output")?.Model.Id;
    public string? Stop { get; } = Order.FirstOrDefault(n => n.Model.Kind == "stop")?.Model.Id;
    public IReadOnlyDictionary<string, CompiledNode> TunnelOutputs { get; } = Order.Where(n => n.Model.Kind == "tunnel-out").GroupBy(n => n.Model.Text).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
    public IReadOnlyDictionary<string, CompiledNode> RegisterOutputs { get; } = Order.Where(n => n.Model.Kind == "shift-write").GroupBy(n => n.Model.Text).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
}

public static class GraphCompiler
{
    public static IReadOnlyList<Diagnostic> Validate(Diagram graph)
    {
        try { Compile(graph); return []; } catch (GraphValidationException e) { return e.Diagnostics; }
    }
    public static CompiledGraph Compile(Diagram graph) => Compile(graph, 0, new HashSet<Diagram>(ReferenceEqualityComparer.Instance));
    private static CompiledGraph Compile(Diagram graph, int depth, HashSet<Diagram> ancestors, StructureContract? ownerContract = null)
    {
        var errors = new List<Diagnostic>();
        if (depth > 12 || !ancestors.Add(graph)) throw new GraphValidationException([new("DEPTH", "Nested diagrams exceed the depth limit or contain recursive references.")]);
        try
        {
            if (graph.Nodes.Count > 4096 || graph.Wires.Count > 16384) throw new GraphValidationException([new("LIMIT", "Diagram exceeds the node or wire limit.")]);
            var nodes = new Dictionary<string, Node>(); var definitions = new Dictionary<string, NodeDefinition>();
            foreach (var node in graph.Nodes)
            {
                if (string.IsNullOrWhiteSpace(node.Id) || !nodes.TryAdd(node.Id, node)) { errors.Add(new("ID", "Every node must have a unique nonempty ID.", node.Id)); continue; }
                if (!NodeCatalog.TryGet(node.Kind, out _)) errors.Add(new("UNKNOWN", $"Unknown function '{node.Kind}'.", node.Id));
                else try { node.Type?.Validate(); definitions[node.Id] = NodeCatalog.Describe(node); }
                    catch (ArgumentException e) { errors.Add(new("SCHEMA", e.Message, node.Id)); }
                if (!double.IsFinite(node.Value) || node.Parameters.Values.Any(v => !double.IsFinite(v))) errors.Add(new("VALUE", "Node parameters must be finite numbers.", node.Id));
            }
            var inputs = nodes.Keys.ToDictionary(x => x, _ => new Dictionary<string, PortAddress>(StringComparer.Ordinal));
            var edges = nodes.Keys.ToDictionary(x => x, _ => new List<string>());
            var indegree = nodes.Keys.ToDictionary(x => x, _ => 0); var wireIds = new HashSet<string>();
            foreach (var wire in graph.Wires)
            {
                if (string.IsNullOrEmpty(wire.Id) || !wireIds.Add(wire.Id)) errors.Add(new("ID", "Wire IDs must be nonempty and unique.", WireId: wire.Id));
                if (!definitions.TryGetValue(wire.From, out var from) || !definitions.TryGetValue(wire.To, out var to)) { errors.Add(new("DANGLING", "Wire references a missing or invalid node.", WireId: wire.Id)); continue; }
                var port = to.Inputs.FirstOrDefault(p => p.Name == wire.Input); var output = from.FindOutput(wire.Output);
                if (port is null || output is null) { errors.Add(new("PORT", "Wire references a missing terminal.", wire.To, wire.Id)); continue; }
                if (!ValueConversion.CanConvert(output.DataType, port.DataType)) errors.Add(new("TYPE", $"Cannot wire {output.DataType} to {port.DataType} input '{port.Name}'.", wire.To, wire.Id));
                if (!inputs[wire.To].TryAdd(wire.Input, new(wire.From, output.Name))) errors.Add(new("DRIVER", $"Input '{port.Name}' has more than one source.", wire.To, wire.Id));
                if (to.Kind != "feedback") { edges[wire.From].Add(wire.To); indegree[wire.To]++; }
            }
            foreach (var node in nodes.Values)
                if (definitions.TryGetValue(node.Id, out var def))
                {
                    foreach (var port in def.Inputs.Where(p => p.Required)) if (!inputs[node.Id].ContainsKey(port.Name) && !(node.Kind == "tunnel-out" && port.Name == "x" && ownerContract?.Outputs.FirstOrDefault(t => t.Name == node.Text)?.UseDefaultIfUnwired == true)) errors.Add(new("UNWIRED", $"{node.Label}: required input '{port.Name}' is unwired.", node.Id));
                    if (node.Kind == "for-loop" && !inputs[node.Id].ContainsKey("count") && !node.Parameters.ContainsKey("count") && !node.Contract!.Inputs.Any(t => t.Mode == TunnelMode.Indexing)) errors.Add(new("COUNT", "A For Loop needs a count or an auto-indexed input array.", node.Id));
                }
            if (errors.Count != 0) throw new GraphValidationException(errors);
            var ready = new Queue<string>(graph.Nodes.Where(x => indegree[x.Id] == 0).Select(x => x.Id)); var ordered = new List<CompiledNode>();
            while (ready.TryDequeue(out var id))
            {
                var model = nodes[id]; var definition = definitions[id]; CompiledGraph? body = null, alternative = null;
                if (definition.IsStructure)
                {
                    if (model.Body is null) errors.Add(new("BODY", $"{model.Label}: structure body is missing.", id));
                    else
                    {
                        try { body = Compile(model.Body, depth + 1, ancestors, model.Contract); }
                        catch (GraphValidationException e) { errors.AddRange(e.Diagnostics.Select(d => d with { StructureId = id })); }
                        ValidateBody(model, model.Body, errors);
                    }
                    if (model.Kind is "case" or "case-typed")
                    {
                        if (model.Alternative is null) errors.Add(new("BODY", "Case structure requires both branches.", id));
                        else
                        {
                            try { alternative = Compile(model.Alternative, depth + 1, ancestors, model.Contract); }
                            catch (GraphValidationException e) { errors.AddRange(e.Diagnostics.Select(d => d with { StructureId = id })); }
                            ValidateBody(model, model.Alternative, errors);
                        }
                    }
                }
                ordered.Add(new(model, definition, inputs[id], body, alternative));
                foreach (var target in edges[id]) if (--indegree[target] == 0) ready.Enqueue(target);
            }
            if (ordered.Count != nodes.Count) errors.Add(new("CYCLE", "Combinational cycle detected. Insert an explicit Feedback node to carry state between frames."));
            if (errors.Count != 0) throw new GraphValidationException(errors);
            return new(ordered);
        }
        finally { ancestors.Remove(graph); }
    }
    private static void ValidateBody(Node owner, Diagram body, List<Diagnostic> errors)
    {
        var typed = AdvancedNodes.IsTypedStructure(owner.Kind);
        if (!typed && body.Nodes.Count(n => n.Kind == "output") != 1) errors.Add(new("CONNECTOR", "A legacy nested diagram must have exactly one Connector output.", owner.Id));
        if (owner.Kind is "while" or "while-loop" && body.Nodes.Count(n => n.Kind == "stop") != 1) errors.Add(new("CONDITION", "A while loop requires exactly one Loop condition terminal.", owner.Id));
        if (!typed) return;
        var contract = owner.Contract!;
        if (owner.Kind is "case-typed" or "subvi-typed" && (contract.Registers.Count > 0 || contract.Inputs.Concat(contract.Outputs).Any(t => t.Mode != TunnelMode.LastValue || t.Conditional))) errors.Add(new("CONTRACT", "Case/SubVI connectors cannot use loop indexing or shift registers.", owner.Id));
        foreach (var tunnel in contract.Outputs)
        {
            var count = body.Nodes.Count(n => n.Kind == "tunnel-out" && n.Text == tunnel.Name);
            if (tunnel.Conditional && body.Nodes.FirstOrDefault(n => n.Kind == "tunnel-out" && n.Text == tunnel.Name) is { } conditional && !body.Wires.Any(w => w.To == conditional.Id && w.Input == "include")) errors.Add(new("CONDITION", $"Conditional tunnel '{tunnel.Name}' needs a Boolean include wire.", conditional.Id, StructureId: owner.Id));
            if (count > 1 || (count == 0 && !tunnel.UseDefaultIfUnwired)) errors.Add(new("TUNNEL", $"Output '{tunnel.Name}' needs exactly one wired terminal in every body, or Use Default if Unwired.", owner.Id));
        }
        foreach (var register in contract.Registers) if (body.Nodes.Count(n => n.Kind == "shift-write" && n.Text == register.Name) != 1) errors.Add(new("REGISTER", $"Shift register '{register.Name}' needs one right terminal.", owner.Id));
        foreach (var n in body.Nodes)
        {
            LabType? expected = n.Kind switch
            {
                "tunnel-in" => contract.Inputs.FirstOrDefault(t => t.Name == n.Text)?.Type,
                "tunnel-out" => contract.Outputs.FirstOrDefault(t => t.Name == n.Text)?.Type,
                "shift-read" or "shift-write" => contract.Registers.FirstOrDefault(r => r.Name == n.Text)?.Type,
                _ => null
            };
            if (n.Kind is "tunnel-in" or "tunnel-out" or "shift-read" or "shift-write")
            {
                if (expected is null || !expected.Equals(n.Type ?? LabType.Number)) errors.Add(new("CONTRACT", $"Terminal '{n.Text}' does not match the owning structure's type declaration.", n.Id, StructureId: owner.Id));
                if (n.Kind == "shift-read" && contract.Registers.FirstOrDefault(r => r.Name == n.Text) is { } r)
                {
                    var slot = n.Parameter("element", 0); if (slot < 0 || slot >= r.Depth || slot != Math.Truncate(slot)) errors.Add(new("REGISTER", "Stacked register element is out of range.", n.Id, StructureId: owner.Id));
                }
            }
            if (n.Kind is "iteration" or "loop-count" && owner.Kind is "case-typed" or "subvi-typed") errors.Add(new("CONTRACT", "Loop terminals must be inside a loop.", n.Id, StructureId: owner.Id));
        }
    }
}
