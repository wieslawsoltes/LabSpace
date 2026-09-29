using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LabSpace.Core;

namespace LabSpace.Documents;

public static class ProjectSerializer
{
    public const int MaximumBytes = 8 * 1024 * 1024;
    private static readonly HashSet<string> Widgets = ["Numeric", "Knob", "Slider", "Gauge", "LED", "Switch", "Graph", "Chart", "String", "Array", "Error", "Complex"];
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
        Validate(project); project.FormatVersion = 3; return project;
    }
    public static LabProject Clone(LabProject project) => Load(Save(project));
    public static void Validate(LabProject project)
    {
        if (project.FormatVersion is not (1 or 2 or 3)) throw new InvalidDataException("Unsupported LabSpace format version. NI .vi binaries are not supported.");
        if (project.Instruments is null || project.Instruments.Count is < 1 or > 64 || string.IsNullOrWhiteSpace(project.Name)) throw new InvalidDataException("A project must have a name and 1–64 VIs.");
        var ids = new HashSet<string>(); var total = 0;
        foreach (var vi in project.Instruments)
        {
            if (vi is null || string.IsNullOrWhiteSpace(vi.Id) || !ids.Add(vi.Id) || string.IsNullOrWhiteSpace(vi.Name) || vi.Panel is null || vi.Panel.Count > 4096) throw new InvalidDataException("Invalid or duplicate VI.");
            Check(vi.Diagram, 0, ref total);
            var nodes = vi.Diagram.Nodes.Select(x => x.Id).ToHashSet(); var panels = new HashSet<string>();
            foreach (var item in vi.Panel)
            {
                if (item is null || string.IsNullOrWhiteSpace(item.Id) || !panels.Add(item.Id) || !nodes.Contains(item.NodeId) || !Widgets.Contains(item.Widget) || !double.IsFinite(item.Minimum) || !double.IsFinite(item.Maximum) || item.Maximum <= item.Minimum) throw new InvalidDataException("Invalid panel item, widget or range.");
                var b = item.Bounds;
                if (!Finite(b.X, b.Y, b.Width, b.Height) || b.Width is < 20 or > 10000 || b.Height is < 20 or > 10000 || Math.Abs(b.X) > 1000000 || Math.Abs(b.Y) > 1000000) throw new InvalidDataException("Invalid panel bounds.");
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
            if (!NodeCatalog.TryGet(node.Kind, out _)) throw new InvalidDataException($"Unsupported function '{node.Kind}'. This project requires an unavailable function; no changes were imported.");
            if (!Enum.IsDefined(node.DataType)) throw new InvalidDataException("Unsupported connector data type.");
            if (node.Contract is { } contract) ValidateContract(contract);
            if (!Finite(node.Width, node.Height) || node.Width is < 0 or > 10000 || node.Height is < 0 or > 10000)
                throw new InvalidDataException("Invalid diagram node size.");
            if (node.Formula is { } formula && (node.Kind != "formula" || formula.Inputs.IsDefault || formula.Outputs.IsDefault
                || formula.Inputs.Length > 32 || formula.Outputs.Length is < 1 or > 32
                || formula.Inputs.Concat(formula.Outputs).Any(n => !StructureFrames.Identifier(n))))
                throw new InvalidDataException("Invalid formula signature.");
            if (node.Frames is null || node.Frames.Count > 64 || (node.Frames.Count > 0 && !StructureFrames.HasFrames(node))
                || node.VisibleFrame < 0 || (node.Frames.Count > 0 && node.VisibleFrame >= node.Frames.Count))
                throw new InvalidDataException("Invalid structure frame collection.");
            var frames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var frame in node.Frames)
            {
                if (frame is null || string.IsNullOrWhiteSpace(frame.Id) || !frames.Add(frame.Id) || frame.Label is null || frame.Label.Length > 4096)
                    throw new InvalidDataException("Invalid or duplicate structure frame.");
                Check(frame.Diagram, depth + 1, ref total);
            }
            if (node.Text.Length > 1000000 || node.Label.Length > 4096 || Math.Abs(node.X) > 1000000 || Math.Abs(node.Y) > 1000000) throw new InvalidDataException("Node payload exceeds the limit.");
            if (node.Body is not null) Check(node.Body, depth + 1, ref total);
            if (node.Alternative is not null) Check(node.Alternative, depth + 1, ref total);
        }
        if (graph.Wires.Any(w => w is null || string.IsNullOrEmpty(w.Id) || w.From is null || w.To is null || w.Input is null || string.IsNullOrWhiteSpace(w.Output) || w.Output.Length > 128)) throw new InvalidDataException("Invalid wire.");
    }
    private static void ValidateContract(StructureContract contract)
    {
        if (contract.Inputs.IsDefault || contract.Outputs.IsDefault || contract.Registers.IsDefault || contract.Inputs.Length > 32 || contract.Outputs.Length > 32 || contract.Registers.Length > 16 || string.IsNullOrWhiteSpace(contract.PrimaryOutput) || contract.PrimaryOutput.Length > 64)
            throw new InvalidDataException("Invalid structure contract.");
        static bool Name(string? name) => !string.IsNullOrWhiteSpace(name) && name.Length <= 64 && name.All(ch => char.IsLetterOrDigit(ch) || ch == '_');
        if (contract.Inputs.Any(t => t is null || !Name(t.Name) || !Enum.IsDefined(t.Type)) || contract.Outputs.Any(t => t is null || !Name(t.Name) || !Enum.IsDefined(t.Type) || !Enum.IsDefined(t.Mode) || !Name(t.Condition)) || contract.Registers.Any(r => r is null || !Name(r.Name) || !Enum.IsDefined(r.Type) || r.HistoryDepth is < 1 or > 16))
            throw new InvalidDataException("Invalid tunnel or shift register metadata.");
    }
    private static bool Finite(params double[] values) => values.All(double.IsFinite);
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, MaxDepth = 64)]
[JsonSerializable(typeof(LabProject))]
internal partial class ProjectJsonContext : JsonSerializerContext;
