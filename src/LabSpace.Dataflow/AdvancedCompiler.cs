using LabSpace.Core;

namespace LabSpace.Dataflow;

public static partial class GraphCompiler
{
    private static void ValidateAdvancedNode(Node node, IReadOnlyDictionary<string, ValueKind>? locals, List<Diagnostic> errors)
    {
        void Error(string code, string text) => errors.Add(new(code, text, node.Id));
        if (StructureFrames.HasFrames(node))
        {
            if (node.Contract is null) Error("CONTRACT", "Multi-frame structures require a typed connector contract.");
            if (node.Frames is null || node.Frames.Count is < 1 or > 64 || node.Frames.Any(f => f is null || f.Diagram is null || string.IsNullOrWhiteSpace(f.Id) || f.Label is null)
                || node.Frames.Select(f => f.Id).Distinct(StringComparer.Ordinal).Count() != node.Frames.Count)
                Error("FRAME", "Frames require unique stable IDs, labels and diagrams; 1–64 frames are supported.");
            else if (node.Kind == "case-multi")
            {
                try { CaseDispatchTable.Compile(node); }
                catch (Exception ex) when (ex is ArgumentException or System.Text.Json.JsonException) { Error("CASE", ex.Message); }
            }
        }
        if (node.Kind is "sequence-read" or "sequence-write")
        {
            if (locals is null) Error("LOCAL", "Sequence locals must belong directly to a sequence frame.");
            if (!StructureFrames.Identifier(node.Text)) Error("LOCAL", "Sequence local names must be identifiers.");
            if (node.Kind == "sequence-read" && (locals is null || !locals.TryGetValue(node.Text, out var kind) || kind != node.DataType))
                Error("LOCAL", $"Local '{node.Text}' must be written with the same type in an earlier frame.");
        }
        if (node.Kind == "formula")
        {
            try { FormulaProgram.Compile(node.Text, node.Formula ?? new()); }
            catch (FormulaException ex) { Error("FORMULA", ex.Message); }
        }
    }
    private static IReadOnlyList<CompiledGraph> CompileFrames(Node node, int depth, HashSet<Diagram> ancestors, List<Diagnostic> errors)
    {
        var graphs = new List<CompiledGraph>(node.Frames.Count);
        var locals = new Dictionary<string, ValueKind>(StringComparer.Ordinal);
        var outputs = new HashSet<string>(StringComparer.Ordinal);
        var defaults = node.Contract!.Outputs.Where(o => o.UseDefaultIfUnwired).Select(o => o.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var frame in node.Frames)
        {
            var compiled = Compile(frame.Diagram, depth + 1, ancestors, defaults, node.Kind == "sequence" ? locals : null);
            graphs.Add(compiled);
            ValidateBody(node, frame.Diagram, errors, requireAllOutputs: node.Kind != "sequence");
            if (node.Kind != "sequence") continue;
            foreach (var n in frame.Diagram.Nodes.Where(n => n.Kind == "sequence-write"))
                if (!locals.TryAdd(n.Text, n.DataType)) errors.Add(new("LOCAL", $"Sequence local '{n.Text}' has multiple writer frames.", n.Id));
            foreach (var output in compiled.ConnectorOutputs.Keys)
                if (!outputs.Add(output)) errors.Add(new("CONNECTOR", $"Sequence output '{output}' must be assigned in exactly one frame.", node.Id));
        }
        if (node.Kind == "sequence")
            foreach (var output in node.Contract.Outputs)
                if (!output.UseDefaultIfUnwired && !outputs.Contains(output.Name)) errors.Add(new("CONNECTOR", $"Sequence output '{output.Name}' is not assigned by any frame.", node.Id));
        return graphs;
    }
}
