using LabSpace.Core;

namespace LabSpace.Controls;

/// <summary>A hit-tested diagram context. Position is relative to the surface in device-independent pixels.</summary>
public sealed record DiagramContextRequest(Node? Node, string? Terminal, bool Output, string? WireId, Point Position);
