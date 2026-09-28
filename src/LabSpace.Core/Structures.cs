namespace LabSpace.Core;

public enum TunnelMode { LastValue, Indexing, Concatenating }

/// <summary>Type is the type on the inside of the structure; indexing adds a dimension outside.</summary>
public sealed class StructureTunnel
{
    public string Name { get; set; } = "value";
    public LabType Type { get; set; } = LabType.Number;
    public TunnelMode Mode { get; set; }
    public bool Conditional { get; set; }
    public bool UseDefaultIfUnwired { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public LabType OutsideType => Mode == TunnelMode.Indexing ? Type.Collected : Type;
}

public sealed class ShiftRegister
{
    public string Name { get; set; } = "state";
    public LabType Type { get; set; } = LabType.Number;
    public bool Initialized { get; set; } = true;
    public int Depth { get; set; } = 1;
    public string InitialValue { get; set; } = "";
}

/// <summary>Explicit structure ABI. Names survive moving, editing, serialization and graph recompilation.</summary>
public sealed class StructureContract
{
    public List<StructureTunnel> Inputs { get; set; } = [];
    public List<StructureTunnel> Outputs { get; set; } = [];
    public List<ShiftRegister> Registers { get; set; } = [];
    public bool StopWhenTrue { get; set; } = true;
    public void Validate()
    {
        if (Inputs is null || Outputs is null || Registers is null || Inputs.Count > 32 || Outputs.Count > 32 || Registers.Count > 16) throw new ArgumentException("Structure connector limit exceeded.");
        foreach (var tunnels in new[] { Inputs, Outputs })
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var tunnel in tunnels)
            {
                if (tunnel is null || !LabType.ValidName(tunnel.Name) || !names.Add(tunnel.Name) || tunnel.Type is null || !Enum.IsDefined(tunnel.Mode)) throw new ArgumentException("Structure tunnels require unique names and valid types/modes.");
                tunnel.Type.Validate(); tunnel.OutsideType.Validate();
                if (tunnel.Mode == TunnelMode.Concatenating && (tunnel.Type.Kind != ValueKind.Array || tunnel.Type.Rank != 1)) throw new ArgumentException("Concatenating outputs require rank-one arrays.");
                if (ReferenceEquals(tunnels, Inputs) && (tunnel.Conditional || tunnel.Mode == TunnelMode.Concatenating)) throw new ArgumentException("Conditional/concatenating modes apply only to outputs.");
            }
        }
        var registerNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var register in Registers)
        {
            if (register is null || !LabType.ValidName(register.Name) || !registerNames.Add(register.Name) || register.Type is null || register.Depth is < 1 or > 16 || register.InitialValue is null) throw new ArgumentException("Invalid shift register.");
            register.Type.Validate(); _ = ValueLiteral.Parse(register.Type, register.InitialValue);
        }
        if (Outputs.Count + Registers.Count == 0) throw new ArgumentException("A typed structure needs an output tunnel or shift register.");
    }
}

public readonly record struct PortAddress(string NodeId, string Port = "value");
