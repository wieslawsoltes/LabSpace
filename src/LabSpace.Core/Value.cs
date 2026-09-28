using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;

namespace LabSpace.Core;

// Preserve the original serialized enum values; new representations are append-only.
public enum ValueKind { Number, Boolean, String, Array, Waveform, Int8, UInt8, Int16, UInt16, Int32, UInt32, Int64, UInt64, Single, Complex, Cluster, Error, Enum, Variant }

/// <summary>Immutable by-value payload, including exact 64-bit integers and rectangular, rank-aware arrays.</summary>
public sealed record Value
{
    public ValueKind Kind => Type.Kind;
    public LabType Type { get; private init; } = LabType.Number;
    public double Number { get; private init; }
    public long Integer { get; private init; }
    public ulong Unsigned { get; private init; }
    public bool Boolean { get; private init; }
    public string Text { get; private init; } = "";
    public ImmutableArray<double> Samples { get; private init; } = [];
    public ImmutableArray<Value> Elements { get; private init; } = [];
    public ImmutableArray<int> Shape { get; private init; } = [];
    public ImmutableDictionary<string, Value> Fields { get; private init; } = ImmutableDictionary<string, Value>.Empty;
    public double SampleRate { get; private init; } = 1;
    public double StartTime { get; private init; }
    public Complex Complex { get; private init; }
    public Value? Contained { get; private init; }
    public int Count => Elements.IsEmpty ? Samples.Length : Elements.Length;
    private Value() { }
    public static Value Numeric(double value) => Real(value, ValueKind.Number);
    public static Value Real(double value, ValueKind kind)
    {
        if (kind is not (ValueKind.Number or ValueKind.Single)) throw new ArgumentException("A real representation must be DBL or SGL.");
        if (kind == ValueKind.Single) value = (float)value;
        if (!double.IsFinite(value)) throw new ArithmeticException("The operation produced a non-finite number.");
        return new() { Type = LabType.Scalar(kind), Number = value };
    }
    public static Value Signed(long value, ValueKind kind = ValueKind.Int64)
    {
        var type = LabType.Scalar(kind);
        if (!type.IsInteger || type.IsUnsigned) throw new ArgumentException("Expected a signed integer representation.");
        var bits = type.BitWidth; if (bits < 64 && (value < -(1L << (bits - 1)) || value >= (1L << (bits - 1)))) throw new OverflowException("Integer literal is outside its representation.");
        return new() { Type = type, Integer = value, Number = value };
    }
    public static Value UnsignedInteger(ulong value, ValueKind kind = ValueKind.UInt64)
    {
        var type = LabType.Scalar(kind); if (!type.IsUnsigned) throw new ArgumentException("Expected an unsigned integer representation.");
        if (type.BitWidth < 64 && value >= (1UL << type.BitWidth)) throw new OverflowException("Integer literal is outside its representation.");
        return new() { Type = type, Unsigned = value, Number = value };
    }
    public static Value Bool(bool value) => new() { Type = LabType.Boolean, Boolean = value };
    public static Value String(string value) { if (value?.Length > 1000000) throw new ArgumentException("String length limit exceeded."); return new() { Type = LabType.String, Text = value ?? "" }; }
    public static Value ComplexNumber(Complex value)
    {
        if (!double.IsFinite(value.Real) || !double.IsFinite(value.Imaginary)) throw new ArithmeticException("Complex components must be finite.");
        return new() { Type = LabType.Scalar(ValueKind.Complex), Complex = value };
    }
    public static Value EnumValue(LabType type, int index) { type.Validate(); if (type.Kind != ValueKind.Enum || index < 0 || index >= type.Labels.Length) throw new ArgumentOutOfRangeException(nameof(index)); return new() { Type = type, Integer = index, Number = index, Text = type.Labels[index] }; }
    public static Value Variant(Value value) => new() { Type = LabType.Scalar(ValueKind.Variant), Contained = value ?? throw new ArgumentNullException(nameof(value)) };
    public static Value Cluster(LabType type, IEnumerable<KeyValuePair<string, Value>> fields)
    {
        type.Validate(); var map = fields.ToImmutableDictionary(StringComparer.Ordinal);
        if (type.Kind != ValueKind.Cluster || map.Count != type.Fields.Length || type.Fields.Any(f => !map.TryGetValue(f.Name, out var v) || !v.Type.Equals(f.Type))) throw new ArgumentException("Cluster values must exactly match the declared fields.");
        return new() { Type = type, Fields = map };
    }
    public static Value Error(bool status = false, int code = 0, string source = "") => new() { Type = LabType.Error, Boolean = status, Integer = code, Text = source, Fields = new Dictionary<string, Value> { ["status"] = Bool(status), ["code"] = Signed(code, ValueKind.Int32), ["source"] = String(source) }.ToImmutableDictionary(StringComparer.Ordinal) };
    public static Value Vector(IEnumerable<double> values) => Series(values, 1, 0, false);
    public static Value Series(IEnumerable<double> values, double sampleRate, double startTime = 0, bool waveform = true)
    {
        if (!double.IsFinite(sampleRate) || sampleRate <= 0 || !double.IsFinite(startTime)) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        var samples = values.Take(65537).ToImmutableArray();
        if (samples.Length > 65536 || samples.Any(x => !double.IsFinite(x))) throw new ArgumentException("A series must contain at most 65,536 finite samples.");
        return new() { Type = waveform ? LabType.Scalar(ValueKind.Waveform) : LabType.Array(LabType.Number), Samples = samples, Shape = waveform ? [] : [samples.Length], SampleRate = sampleRate, StartTime = startTime };
    }
    public static Value Array(LabType elementType, IEnumerable<int> shape, IEnumerable<Value> values)
    {
        var dimensions = shape.ToImmutableArray(); var type = LabType.Array(elementType, dimensions.Length); type.Validate();
        var count = ShapeCount(dimensions); var items = values.Take(65537).ToImmutableArray();
        if (items.Length != count || items.Any(v => v is null || !v.Type.Equals(elementType))) throw new ArgumentException("Array shape, element count or element types do not match.");
        return new() { Type = type, Shape = dimensions, Elements = elementType.Kind == ValueKind.Number ? [] : items, Samples = elementType.IsNumeric ? items.Select(v => v.Number).ToImmutableArray() : [] };
    }
    public static int ShapeCount(IEnumerable<int> dimensions)
    {
        long count = 1; foreach (var size in dimensions) { if (size is < 0 or > 65536) throw new ArgumentOutOfRangeException(nameof(dimensions)); count *= size; if (count > 65536) throw new ArgumentException("Array exceeds 65,536 elements."); } return (int)count;
    }
    public Value ElementAt(int index) => index < 0 || index >= Count ? Default(Type.Element ?? LabType.Number) : Elements.IsEmpty ? Numeric(Samples[index]) : Elements[index];
    public IEnumerable<Value> EnumerateElements() { for (var i = 0; i < Count; i++) yield return ElementAt(i); }
    public Value IndexFirst(int index)
    {
        if (Kind != ValueKind.Array) throw new InvalidOperationException("Expected an array.");
        if (Type.Rank == 1) return ElementAt(index);
        var rowShape = Shape.RemoveAt(0); var rowCount = ShapeCount(rowShape);
        return Array(Type.Element!, rowShape, Enumerable.Range(0, rowCount).Select(i => index < 0 || index >= Shape[0] ? Default(Type.Element!) : ElementAt(index * rowCount + i)));
    }
    public static Value Collect(LabType elementType, IReadOnlyList<Value> iterations)
    {
        if (elementType.Kind != ValueKind.Array) return Array(elementType, [iterations.Count], iterations);
        var shape = iterations.Count == 0 ? Enumerable.Repeat(0, elementType.Rank).ToImmutableArray() : iterations[0].Shape;
        if (iterations.Any(v => !v.Type.Equals(elementType) || !v.Shape.SequenceEqual(shape))) throw new ArgumentException("Indexed array outputs must have a rectangular shape.");
        return Array(elementType.Element!, new[] { iterations.Count }.Concat(shape), iterations.SelectMany(v => v.EnumerateElements()));
    }
    public static Value Default(LabType type) => type.Kind switch
    {
        ValueKind.Boolean => Bool(false), ValueKind.String => String(""), ValueKind.Waveform => Series([], 1),
        ValueKind.Array => Array(type.Element!, Enumerable.Repeat(0, type.Rank), []),
        ValueKind.Cluster => Cluster(type, type.Fields.Select(f => KeyValuePair.Create(f.Name, Default(f.Type)))),
        ValueKind.Complex => ComplexNumber(System.Numerics.Complex.Zero), ValueKind.Error => Error(),
        ValueKind.Enum => EnumValue(type, 0), ValueKind.Variant => Variant(Numeric(0)),
        _ => type.IsUnsigned ? UnsignedInteger(0, type.Kind) : type.IsInteger ? Signed(0, type.Kind) : Real(0, type.Kind)
    };
    public override string ToString() => Kind switch
    {
        ValueKind.Boolean => Boolean ? "TRUE" : "FALSE", ValueKind.String => Text, ValueKind.Enum => Text,
        ValueKind.Array => "[" + string.Join("×", Shape) + "] " + Type.Element,
        ValueKind.Waveform => $"{Samples.Length:N0} samples · {SampleRate:G6} Hz",
        ValueKind.Cluster => "{" + string.Join(", ", Type.Fields.Select(f => f.Name + "=" + Fields[f.Name])) + "}",
        ValueKind.Error => $"{(Boolean ? "Error" : Integer != 0 ? "Warning" : "OK")} {Integer}: {Text}",
        ValueKind.Variant => "Variant: " + Contained,
        ValueKind.Complex => Complex.ToString("G7", CultureInfo.InvariantCulture),
        _ => Type.IsUnsigned ? Unsigned.ToString(CultureInfo.InvariantCulture) : Type.IsInteger ? Integer.ToString(CultureInfo.InvariantCulture) : Number.ToString("G7", CultureInfo.InvariantCulture)
    };
}
