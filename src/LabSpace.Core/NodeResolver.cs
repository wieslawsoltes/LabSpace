using System.Runtime.CompilerServices;

namespace LabSpace.Core;

/// <summary>Resolves model-dependent terminals, with weak caching by immutable contract identity.</summary>
internal static class NodeResolver
{
    private sealed class Entry
    {
        public string Kind = "";
        public ValueKind Type;
        public StructureContract? Contract;
        public NodeDefinition? Definition;
    }
    private static readonly ConditionalWeakTable<Node, Entry> Cache = new();
    public static NodeDefinition Resolve(Node node)
    {
        var basis = NodeCatalog.Get(node.Kind);
        if (node.Contract is null && node.Kind is not ("input" or "output")) return basis;
        var entry = Cache.GetValue(node, static _ => new Entry());
        if (entry.Definition is not null && entry.Kind == node.Kind && entry.Type == node.DataType && ReferenceEquals(entry.Contract, node.Contract)) return entry.Definition;
        var result = ResolveCore(node, basis);
        entry.Kind = node.Kind; entry.Type = node.DataType; entry.Contract = node.Contract; entry.Definition = result;
        return result;
    }
    private static NodeDefinition ResolveCore(Node node, NodeDefinition basis)
    {
        if (node.Kind == "input") return basis with { Output = node.DataType, Outputs = [new("result", node.DataType)] };
        if (node.Kind == "output") return basis with { Output = node.DataType, Inputs = [new("x", node.DataType)], Outputs = [] };
        if (node.Contract is not { } contract) return basis;
        var inputs = new List<PortDefinition>(); var outputs = new List<OutputDefinition>();
        if (node.Kind == "for") inputs.Add(new("count", ValueKind.Number, false, contract.Inputs.Any(t => t.Indexing) ? 10000 : 10));
        if (node.Kind == "case") inputs.Add(new("selector", ValueKind.Boolean));
        inputs.AddRange(contract.Inputs.Select(t => new PortDefinition(t.Name, t.Indexing ? ValueKind.Array : t.Type, t.Required)));
        foreach (var r in contract.Registers.Where(r => r.Initialized))
            for (var i = 0; i < r.HistoryDepth; i++) inputs.Add(new("initial:" + r.Name + (i == 0 ? "" : ":" + i), r.Type, false));
        outputs.AddRange(contract.Outputs.Select(t => new OutputDefinition(t.Name, t.ExternalType)));
        outputs.AddRange(contract.Registers.Select(r => new OutputDefinition(r.Name, r.Type)));
        var primary = outputs.FindIndex(p => p.Name == contract.PrimaryOutput);
        if (primary > 0) { var item = outputs[primary]; outputs.RemoveAt(primary); outputs.Insert(0, item); }
        return basis with { Inputs = inputs.ToArray(), Output = outputs.Count == 0 ? ValueKind.Number : outputs[0].Kind, HasOutput = outputs.Count > 0, Outputs = outputs.ToArray() };
    }
}
