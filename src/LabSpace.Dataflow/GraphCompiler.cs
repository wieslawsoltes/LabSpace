using LabSpace.Core;

namespace LabSpace.Dataflow;

public sealed record Diagnostic(string Code, string Message, string? NodeId = null, string? WireId = null);
public sealed class GraphValidationException(IReadOnlyList<Diagnostic> diagnostics) : Exception(string.Join("\n", diagnostics.Select(x => x.Message)))
{
    public IReadOnlyList<Diagnostic> Diagnostics { get; } = diagnostics;
}
public sealed record CompiledNode(Node Model, NodeDefinition Definition, IReadOnlyDictionary<string, string> Sources, CompiledGraph? Body, CompiledGraph? Alternative);
public sealed record CompiledGraph(IReadOnlyList<CompiledNode> Order);

public static class GraphCompiler
{
    public static IReadOnlyList<Diagnostic> Validate(Diagram graph)
    {
        try { Compile(graph); return []; } catch (GraphValidationException e) { return e.Diagnostics; }
    }
    public static CompiledGraph Compile(Diagram graph) => Compile(graph, 0, new HashSet<Diagram>(ReferenceEqualityComparer.Instance));
    private static CompiledGraph Compile(Diagram graph, int depth, HashSet<Diagram> ancestors)
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
                if (!NodeCatalog.TryGet(node.Kind, out var definition)) errors.Add(new("UNKNOWN", $"Unknown function '{node.Kind}'.", node.Id));
                else definitions[node.Id] = definition;
                if (!double.IsFinite(node.Value) || node.Parameters.Values.Any(v => !double.IsFinite(v))) errors.Add(new("VALUE", "Node parameters must be finite numbers.", node.Id));
            }
            var inputs = nodes.Keys.ToDictionary(x => x, _ => new Dictionary<string, string>());
            var edges = nodes.Keys.ToDictionary(x => x, _ => new List<string>());
            var indegree = nodes.Keys.ToDictionary(x => x, _ => 0); var wireIds = new HashSet<string>();
            foreach (var wire in graph.Wires)
            {
                if (!wireIds.Add(wire.Id)) errors.Add(new("ID", "Wire IDs must be unique.", WireId: wire.Id));
                if (!definitions.TryGetValue(wire.From, out var from) || !definitions.TryGetValue(wire.To, out var to)) { errors.Add(new("DANGLING", "Wire references a missing node.", WireId: wire.Id)); continue; }
                var port = to.Inputs.FirstOrDefault(p => p.Name == wire.Input);
                if (port is null || !from.HasOutput) { errors.Add(new("PORT", "Wire references a missing or non-output terminal.", wire.To, wire.Id)); continue; }
                if (from.Output != port.Kind) errors.Add(new("TYPE", $"Cannot wire {from.Output} to {port.Kind} input '{port.Name}'.", wire.To, wire.Id));
                if (!inputs[wire.To].TryAdd(wire.Input, wire.From)) errors.Add(new("DRIVER", $"Input '{port.Name}' has more than one source.", wire.To, wire.Id));
                if (to.Kind != "feedback") { edges[wire.From].Add(wire.To); indegree[wire.To]++; }
            }
            foreach (var node in nodes.Values)
                if (definitions.TryGetValue(node.Id, out var def))
                    foreach (var port in def.Inputs.Where(p => p.Required))
                        if (!inputs[node.Id].ContainsKey(port.Name)) errors.Add(new("UNWIRED", $"{node.Label}: required input '{port.Name}' is unwired.", node.Id));
            if (errors.Count != 0) throw new GraphValidationException(errors);
            var ready = new Queue<string>(graph.Nodes.Where(x => indegree[x.Id] == 0).Select(x => x.Id)); var ordered = new List<CompiledNode>();
            while (ready.TryDequeue(out var id))
            {
                var model = nodes[id]; var definition = definitions[id]; CompiledGraph? body = null, alternative = null;
                if (definition.IsStructure)
                {
                    if (model.Body is null) errors.Add(new("BODY", $"{model.Label}: structure body is missing.", id));
                    else { body = Compile(model.Body, depth + 1, ancestors); ValidateBody(model.Body, model.Kind == "while", id, errors); }
                    if (model.Kind == "case")
                    {
                        if (model.Alternative is null) errors.Add(new("BODY", "Case structure requires both branches.", id));
                        else { alternative = Compile(model.Alternative, depth + 1, ancestors); ValidateBody(model.Alternative, false, id, errors); }
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
    private static void ValidateBody(Diagram body, bool needsStop, string id, List<Diagnostic> errors)
    {
        if (body.Nodes.Count(n => n.Kind == "output") != 1) errors.Add(new("CONNECTOR", "A nested diagram must have exactly one Connector output.", id));
        if (needsStop && body.Nodes.Count(n => n.Kind == "stop") != 1) errors.Add(new("CONDITION", "A while loop requires exactly one Loop condition terminal.", id));
    }
}
