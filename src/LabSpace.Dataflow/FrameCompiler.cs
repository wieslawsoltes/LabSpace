using LabSpace.Core;

namespace LabSpace.Dataflow;

public static partial class GraphCompiler
{
    public static IReadOnlyList<Diagnostic> ValidateInterface(Node node)
    {
        var errors = new List<Diagnostic>();
        if (node.Contract is null) return [new("CONTRACT", "Missing connector contract.", node.Id)];
        ValidateContract(node, NodeCatalog.Get(node.Kind), errors);
        if (errors.Count == 0 && node.Kind is "case" or "sequence")
        {
            if (node.Frames.Count is < 1 or > 64 || node.Frames.Select(f => f.Id).Distinct(StringComparer.Ordinal).Count() != node.Frames.Count) errors.Add(new("FRAMES", "Invalid frame count or identities.", node.Id));
            else try { _ = new FrameProgram(node, []); } catch (Exception e) when (e is ArgumentException or System.Text.Json.JsonException) { errors.Add(new("FRAMES", e.Message, node.Id)); }
        }
        return errors;
    }
    private static void ValidateProgrammingContract(Node node, List<Diagnostic> errors)
    {
        var c = node.Contract!;
        void Error(string message) => errors.Add(new("CONTRACT", message, node.Id));
        if (c.Locals.IsDefault || c.Locals.Length > 32) { Error("Invalid sequence locals."); return; }
        if (node.Kind == "case" && c.SelectorType is not (ValueKind.Boolean or ValueKind.Number or ValueKind.String or ValueKind.Error)) Error("Unsupported Case selector type.");
        if (node.Kind == "case" && node.Frames.Count == 0 && c.SelectorType != ValueKind.Boolean) Error("A non-Boolean Case requires explicit frames.");
        if (node.Kind != "sequence" && c.Locals.Length > 0) Error("Sequence locals belong to a Sequence structure.");
        if (node.Kind == "formula" && (c.Inputs.Any(t => t.Type != ValueKind.Number || t.Indexing) || c.Outputs.Any(t => t.Type != ValueKind.Number || t.Mode != TunnelMode.LastValue) || c.Registers.Length > 0 || c.Locals.Length > 0)) Error("Formula terminals must be numeric scalars with no collection or register semantics.");
        var names = new HashSet<string>(c.Inputs.Select(t => t.Name).Concat(c.Outputs.Select(t => t.Name)), StringComparer.Ordinal);
        foreach (var local in c.Locals)
            if (local is null || string.IsNullOrWhiteSpace(local.Name) || local.Name.Length > 64 || !local.Name.All(ch => char.IsLetterOrDigit(ch) || ch == '_') || !names.Add(local.Name) || !Enum.IsDefined(local.Type) || !node.Frames.Any(f => f.Id == local.SourceFrameId)) Error("Sequence locals require unique names, supported types and an existing source frame.");
    }
    private static FrameProgram? CompileFrames(Node node, int depth, HashSet<Diagram> ancestors, List<Diagnostic> errors)
    {
        void Error(string message) => errors.Add(new("FRAMES", message, node.Id));
        if (node.Kind is not ("case" or "sequence") || node.Contract is null || node.Frames.Count is < 1 or > 64 || node.Frames.Select(f => f.Id).Distinct(StringComparer.Ordinal).Count() != node.Frames.Count || node.Frames.Any(f => string.IsNullOrWhiteSpace(f.Id) || f.Selector is null))
        { Error("Case/Sequence requires 1–64 frames with unique nonempty identities."); return null; }
        if (node.Body is not null || node.Alternative is not null) { Error("Explicit frames cannot be combined with legacy Body/Alternative diagrams."); return null; }
        var compiled = new List<CompiledFrame>();
        var c = node.Contract;
        var defaults = c.Outputs.Where(t => t.UseDefaultIfUnwired).Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var frame in node.Frames)
        {
            var graph = Compile(frame.Diagram, depth + 1, ancestors, defaults);
            compiled.Add(new(frame.Id, frame.Selector, graph));
            if (node.Kind == "case") ValidateBody(node, frame.Diagram, errors);
        }
        if (node.Kind == "sequence")
        {
            foreach (var output in c.Outputs)
            {
                var count = node.Frames.Sum(f => f.Diagram.Nodes.Count(n => n.Kind == "output" && ConnectorName(n) == output.Name));
                if (count != 1 && !(count == 0 && output.UseDefaultIfUnwired)) Error("Sequence output '" + output.Name + "' must have exactly one source frame.");
            }
            var indices = node.Frames.Select((f, i) => (f.Id, Index: i)).ToDictionary(p => p.Id, p => p.Index, StringComparer.Ordinal);
            for (var i = 0; i < node.Frames.Count; i++)
            {
                var frame = node.Frames[i];
                foreach (var terminal in frame.Diagram.Nodes.Where(n => n.Kind is "input" or "output"))
                {
                    var name = terminal.Kind == "input" ? terminal.Text : ConnectorName(terminal);
                    if (terminal.Kind == "input")
                    {
                        var tunnel = c.Inputs.FirstOrDefault(t => t.Name == name);
                        var local = c.Locals.FirstOrDefault(l => l.Name == name);
                        if (tunnel is not null ? tunnel.Type != terminal.DataType : local is null || local.Type != terminal.DataType || indices[local.SourceFrameId] >= i) Error("Sequence input '" + name + "' is undeclared, mistyped or reads a local before its source frame completes.");
                    }
                    else
                    {
                        var tunnel = c.Outputs.FirstOrDefault(t => t.Name == name); var local = c.Locals.FirstOrDefault(l => l.Name == name);
                        if (tunnel is not null ? tunnel.Type != terminal.DataType : local is null || local.Type != terminal.DataType || local.SourceFrameId != frame.Id) Error("Sequence output '" + name + "' is undeclared, mistyped or in the wrong source frame.");
                    }
                }
            }
            foreach (var local in c.Locals)
                if (node.Frames.First(f => f.Id == local.SourceFrameId).Diagram.Nodes.Count(n => n.Kind == "output" && ConnectorName(n) == local.Name && n.DataType == local.Type) != 1) Error("Missing source terminal for sequence local '" + local.Name + "'.");
        }
        try { return new(node, compiled); }
        catch (Exception e) when (e is ArgumentException or System.Text.Json.JsonException) { Error(e.Message); return null; }
    }
}
