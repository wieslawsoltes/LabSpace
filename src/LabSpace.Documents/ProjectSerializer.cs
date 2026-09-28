using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LabSpace.Core;

namespace LabSpace.Documents;

public static class ProjectSerializer
{
    public const int MaximumBytes = 8 * 1024 * 1024;
    public static string Save(LabProject project)
    {
        Validate(project);
        var text = JsonSerializer.Serialize(project, ProjectJsonContext.Default.LabProject);
        if (Encoding.UTF8.GetByteCount(text) > MaximumBytes) throw new InvalidDataException("Project exceeds the 8 MiB limit.");
        return text;
    }
    public static LabProject Load(string json)
    {
        if (Encoding.UTF8.GetByteCount(json) > MaximumBytes) throw new InvalidDataException("Project exceeds the 8 MiB limit.");
        var project = JsonSerializer.Deserialize(json, ProjectJsonContext.Default.LabProject) ?? throw new InvalidDataException("Project is empty.");
        Validate(project); return project;
    }
    public static LabProject Clone(LabProject project) => Load(Save(project));
    public static void Validate(LabProject project)
    {
        if (project.FormatVersion != 1) throw new InvalidDataException("Unsupported LabSpace format version. NI .vi binaries are not supported.");
        if (project.Instruments is null || project.Instruments.Count is < 1 or > 64 || string.IsNullOrWhiteSpace(project.Name)) throw new InvalidDataException("A project must have a name and 1–64 VIs.");
        var ids = new HashSet<string>(); var total = 0;
        foreach (var vi in project.Instruments)
        {
            if (vi is null || !ids.Add(vi.Id) || string.IsNullOrWhiteSpace(vi.Name) || vi.Panel is null || vi.Panel.Count > 4096) throw new InvalidDataException("Invalid or duplicate VI.");
            Check(vi.Diagram, 0, ref total);
            var nodes = vi.Diagram.Nodes.Select(x => x.Id).ToHashSet(); var panels = new HashSet<string>();
            foreach (var item in vi.Panel)
            {
                if (item is null || !panels.Add(item.Id) || !nodes.Contains(item.NodeId) || !double.IsFinite(item.Minimum) || !double.IsFinite(item.Maximum) || item.Maximum <= item.Minimum) throw new InvalidDataException("Invalid panel item or range.");
                var b = item.Bounds;
                if (!Finite(b.X, b.Y, b.Width, b.Height) || b.Width is < 20 or > 10000 || b.Height is < 20 or > 10000) throw new InvalidDataException("Invalid panel bounds.");
            }
        }
    }
    private static void Check(Diagram graph, int depth, ref int total)
    {
        if (depth > 12 || graph is null || graph.Nodes is null || graph.Wires is null || graph.Wires.Count > 16384 || graph.Nodes.Count > 4096) throw new InvalidDataException("Invalid or excessively nested diagram.");
        total += graph.Nodes.Count; if (total > 16384) throw new InvalidDataException("Project node budget exceeded.");
        var ids = new HashSet<string>();
        foreach (var node in graph.Nodes)
        {
            if (node is null || !ids.Add(node.Id) || string.IsNullOrEmpty(node.Id) || node.Kind is null || node.Label is null || node.Text is null || node.Parameters is null || !Finite(node.X, node.Y, node.Value) || node.Parameters.Count > 64 || node.Parameters.Values.Any(v => !double.IsFinite(v))) throw new InvalidDataException("Invalid node.");
            if (node.Text.Length > 1000000 || node.Label.Length > 4096 || Math.Abs(node.X) > 1000000 || Math.Abs(node.Y) > 1000000) throw new InvalidDataException("Node payload exceeds the limit.");
            if (node.Body is not null) Check(node.Body, depth + 1, ref total);
            if (node.Alternative is not null) Check(node.Alternative, depth + 1, ref total);
        }
        if (graph.Wires.Any(w => w is null || string.IsNullOrEmpty(w.Id) || w.From is null || w.To is null || w.Input is null)) throw new InvalidDataException("Invalid wire.");
    }
    private static bool Finite(params double[] values) => values.All(double.IsFinite);
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, MaxDepth = 64)]
[JsonSerializable(typeof(LabProject))]
internal partial class ProjectJsonContext : JsonSerializerContext;
