using System.Collections.Immutable;

namespace LabSpace.Core;

/// <summary>Immutable standard error payload; status distinguishes errors from warnings.</summary>
public sealed record ErrorCluster(bool Status, int Code, string Source)
{
    public static ErrorCluster None { get; } = new(false, 0, "");
}

/// <summary>A stable identity keeps uninitialized state attached to a case when cases are reordered.</summary>
public sealed class StructureFrame
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Label { get; set; } = "0";
    public bool IsDefault { get; set; }
    public Diagram Diagram { get; set; } = new();
}

public sealed record FormulaSignature
{
    public ImmutableArray<string> Inputs { get; init; } = ["x"];
    public ImmutableArray<string> Outputs { get; init; } = ["result"];
}

public static class StructureFrames
{
    public static bool HasFrames(Node node) => node.Kind is "case-multi" or "sequence";
    public static Diagram? VisibleBody(Node node) => HasFrames(node)
        ? node.Frames.ElementAtOrDefault(Math.Clamp(node.VisibleFrame, 0, Math.Max(0, node.Frames.Count - 1)))?.Diagram
        : node.Body;
    public static string Caption(Node node)
    {
        if (!HasFrames(node)) return node.Kind == "case" ? "True" : NodeCatalog.Get(node.Kind).Glyph;
        if (node.Frames.Count == 0) return "No frames";
        var index = Math.Clamp(node.VisibleFrame, 0, node.Frames.Count - 1);
        var frame = node.Frames[index];
        return node.Kind == "sequence" ? $"{index} [0..{node.Frames.Count - 1}]" : frame.Label + (frame.IsDefault ? ", Default" : "");
    }
    public static bool Identifier(string? text) => !string.IsNullOrWhiteSpace(text) && text.Length <= 64
        && (char.IsLetter(text[0]) || text[0] == '_') && text.All(c => char.IsLetterOrDigit(c) || c == '_');
}
