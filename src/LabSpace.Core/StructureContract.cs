using System.Collections.Immutable;

namespace LabSpace.Core;

/// <summary>How a loop output is collected. Indexed outputs currently collect numeric scalars.</summary>
public enum TunnelMode { LastValue, Indexing, Concatenating, ConditionalIndexing }

public sealed record InputTunnel
{
    public string Name { get; init; } = "x";
    public ValueKind Type { get; init; }
    public bool Indexing { get; init; }
    public bool Required { get; init; } = true;
}

public sealed record OutputTunnel
{
    public string Name { get; init; } = "result";
    public ValueKind Type { get; init; }
    public TunnelMode Mode { get; init; }
    public bool UseDefaultIfUnwired { get; init; }
    public string Condition { get; init; } = "include";
    [System.Text.Json.Serialization.JsonIgnore]
    public ValueKind ExternalType => Mode is TunnelMode.Indexing or TunnelMode.ConditionalIndexing or TunnelMode.Concatenating ? ValueKind.Array : Type;
}

/// <summary>Paired terminals: initial:name outside, name inside and at the output. HistoryDepth adds name:1, name:2, ... inside.</summary>
public sealed record ShiftRegister
{
    public string Name { get; init; } = "state";
    public ValueKind Type { get; init; }
    public bool Initialized { get; init; } = true;
    public int HistoryDepth { get; init; } = 1;
}

/// <summary>Immutable connector contract. Replacing it invalidates the resolver cache without polling or mutable collection hashes.</summary>
public sealed record StructureContract
{
    public ImmutableArray<InputTunnel> Inputs { get; init; } = [];
    public ImmutableArray<OutputTunnel> Outputs { get; init; } = [];
    public ImmutableArray<ShiftRegister> Registers { get; init; } = [];
    public string PrimaryOutput { get; init; } = "result";
    public bool ConditionalFor { get; init; }
    public bool ContinueWhenTrue { get; init; }
}

public static class ValueDefaults
{
    public static Value Create(ValueKind kind, double number = 0, string text = "") => kind switch
    {
        ValueKind.Number => Value.Numeric(number),
        ValueKind.Boolean => Value.Bool(number != 0),
        ValueKind.String => Value.String(text),
        ValueKind.Array => Value.Vector([]),
        ValueKind.Waveform => Value.Series([], 1),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
