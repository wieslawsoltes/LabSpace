using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace LabSpace.Core;

public sealed record TypeField(string Name, LabType Type);

/// <summary>Structural, immutable wire type. Equality includes rank, ordered cluster fields and enum labels.</summary>
public sealed class LabType : IEquatable<LabType>
{
    public ValueKind Kind { get; init; }
    public LabType? Element { get; init; }
    public int Rank { get; init; }
    public ImmutableArray<TypeField> Fields { get; init; } = [];
    public ImmutableArray<string> Labels { get; init; } = [];
    public static LabType Number { get; } = new() { Kind = ValueKind.Number };
    public static LabType Boolean { get; } = new() { Kind = ValueKind.Boolean };
    public static LabType String { get; } = new() { Kind = ValueKind.String };
    public static LabType Int32 { get; } = new() { Kind = ValueKind.Int32 };
    public static LabType Error { get; } = new() { Kind = ValueKind.Error };
    public static LabType Scalar(ValueKind kind) => kind == ValueKind.Array ? Array(Number) : new() { Kind = kind };
    public static LabType Array(LabType element, int rank = 1) => new() { Kind = ValueKind.Array, Element = element, Rank = rank };
    public static LabType Cluster(params TypeField[] fields) => new() { Kind = ValueKind.Cluster, Fields = [.. fields] };
    public static LabType Enumeration(params string[] labels) => new() { Kind = ValueKind.Enum, Labels = [.. labels] };
    [JsonIgnore]
    public bool IsInteger => Kind is ValueKind.Int8 or ValueKind.UInt8 or ValueKind.Int16 or ValueKind.UInt16 or ValueKind.Int32 or ValueKind.UInt32 or ValueKind.Int64 or ValueKind.UInt64;
    [JsonIgnore]
    public bool IsUnsigned => Kind is ValueKind.UInt8 or ValueKind.UInt16 or ValueKind.UInt32 or ValueKind.UInt64;
    [JsonIgnore]
    public bool IsNumeric => IsInteger || Kind is ValueKind.Number or ValueKind.Single;
    [JsonIgnore]
    public int BitWidth => Kind switch { ValueKind.Int8 or ValueKind.UInt8 => 8, ValueKind.Int16 or ValueKind.UInt16 => 16, ValueKind.Int32 or ValueKind.UInt32 => 32, _ => 64 };
    [JsonIgnore]
    public LabType Indexed => Kind != ValueKind.Array ? throw new InvalidOperationException("Only arrays can be indexed.") : Rank == 1 ? Element! : Array(Element!, Rank - 1);
    [JsonIgnore]
    public LabType Collected => Kind == ValueKind.Array ? Array(Element!, Rank + 1) : Array(this);
    public void Validate(int depth = 0)
    {
        if (depth > 8 || !Enum.IsDefined(Kind) || Fields.IsDefault || Labels.IsDefault || Fields.Length > 64 || Labels.Length > 1024) throw new ArgumentException("Invalid or excessively nested wire type.");
        if (Kind == ValueKind.Array)
        {
            if (Rank is < 1 or > 8 || Element is null || Element.Kind == ValueKind.Array) throw new ArgumentException("An array needs a non-array element type and rank 1–8.");
            Element.Validate(depth + 1);
        }
        else if (Element is not null || Rank != 0) throw new ArgumentException("Only arrays may declare an element type and rank.");
        if (Kind == ValueKind.Cluster)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var f in Fields) { if (f is null || !ValidName(f.Name) || !names.Add(f.Name) || f.Type is null) throw new ArgumentException("Cluster fields require unique names and types."); f.Type.Validate(depth + 1); }
        }
        else if (Fields.Length != 0) throw new ArgumentException("Only clusters may declare fields.");
        if (Kind == ValueKind.Enum)
        {
            if (Labels.Length == 0 || Labels.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 256) || Labels.Distinct(StringComparer.Ordinal).Count() != Labels.Length) throw new ArgumentException("An enum needs unique, nonempty labels.");
        }
        else if (Labels.Length != 0) throw new ArgumentException("Only enums may declare labels.");
    }
    public static bool ValidName(string? name) => !string.IsNullOrWhiteSpace(name) && name.Length <= 128 && !name.StartsWith('$') && !name.Contains(':');
    public bool Equals(LabType? other)
    {
        if (ReferenceEquals(this, other)) return true;
        return other is not null && Kind == other.Kind && Rank == other.Rank && Equals(Element, other.Element)
            && Fields.Length == other.Fields.Length && Fields.Zip(other.Fields).All(p => p.First.Name == p.Second.Name && p.First.Type.Equals(p.Second.Type))
            && Labels.SequenceEqual(other.Labels, StringComparer.Ordinal);
    }
    public override bool Equals(object? obj) => obj is LabType type && Equals(type);
    public override int GetHashCode() { var hash = new HashCode(); hash.Add(Kind); hash.Add(Rank); hash.Add(Element); foreach (var f in Fields) { hash.Add(f.Name); hash.Add(f.Type); } foreach (var l in Labels) hash.Add(l); return hash.ToHashCode(); }
    public override string ToString() => Kind switch
    {
        ValueKind.Array => $"{Element}[{new string(',', Rank - 1)}]",
        ValueKind.Cluster => "{" + string.Join(", ", Fields.Select(f => f.Name + ": " + f.Type)) + "}",
        ValueKind.Enum => "Enum(" + string.Join("|", Labels) + ")",
        ValueKind.Number => "DBL", ValueKind.Single => "SGL", ValueKind.Complex => "CDB", ValueKind.Boolean => "Boolean", _ => Kind.ToString()
    };
}
