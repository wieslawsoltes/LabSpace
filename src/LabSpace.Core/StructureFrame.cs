namespace LabSpace.Core;

/// <summary>A stable, editable subdiagram identity, independent of its ordinal position.</summary>
public sealed class StructureFrame
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Selector { get; set; } = "0";
    public bool IsDefault { get; set; }
    public Diagram Diagram { get; set; } = new();
}

/// <summary>A sequence local is written in exactly one frame and may be read only in later frames.</summary>
public sealed record SequenceLocal
{
    public string Name { get; init; } = "local";
    public ValueKind Type { get; init; }
    public string SourceFrameId { get; init; } = "";
}

public sealed record ErrorCluster(bool Status, int Code, string Source);

public static class NodeDiagrams
{
    public static IEnumerable<Diagram> Children(Node node)
    {
        if (node.Body is not null) yield return node.Body;
        if (node.Alternative is not null) yield return node.Alternative;
        foreach (var frame in node.Frames) yield return frame.Diagram;
    }
}
