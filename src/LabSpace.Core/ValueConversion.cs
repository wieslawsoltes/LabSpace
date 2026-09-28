using System.Numerics;
using System.Text.Json;
using System.Globalization;

namespace LabSpace.Core;

public static class ValueConversion
{
    public static bool CanConvert(LabType from, LabType to) => from.Equals(to) || (from.IsNumeric && to.IsNumeric)
        || (from.IsNumeric && to.Kind == ValueKind.Complex)
        || (from.Kind == ValueKind.Array && to.Kind == ValueKind.Array && from.Rank == to.Rank && CanConvert(from.Element!, to.Element!));
    public static Value Convert(Value value, LabType target)
    {
        if (value.Type.Equals(target)) return value;
        if (!CanConvert(value.Type, target)) throw new ArgumentException($"Cannot coerce {value.Type} to {target}.");
        if (target.Kind == ValueKind.Array) return Value.Array(target.Element!, value.Shape, value.EnumerateElements().Select(v => Convert(v, target.Element!)));
        if (target.Kind == ValueKind.Complex) return Value.ComplexNumber(new(value.Number, 0));
        if (!target.IsInteger) return Value.Real(value.Number, target.Kind);
        var integer = value.Type.IsInteger ? ExactInteger(value) : new BigInteger(Math.Round(value.Number, MidpointRounding.ToEven));
        var (min, max) = Range(target); integer = BigInteger.Clamp(integer, min, max);
        return target.IsUnsigned ? Value.UnsignedInteger((ulong)integer, target.Kind) : Value.Signed((long)integer, target.Kind);
    }
    public static BigInteger ExactInteger(Value value) => value.Type.IsUnsigned ? new(value.Unsigned) : new(value.Integer);
    public static (BigInteger Minimum, BigInteger Maximum) Range(LabType type) => type.IsUnsigned ? (BigInteger.Zero, (BigInteger.One << type.BitWidth) - 1) : (-(BigInteger.One << (type.BitWidth - 1)), (BigInteger.One << (type.BitWidth - 1)) - 1);
    public static Value WrapInteger(BigInteger number, LabType type)
    {
        var modulus = BigInteger.One << type.BitWidth; number = (number % modulus + modulus) % modulus;
        if (!type.IsUnsigned && number >= modulus / 2) number -= modulus;
        return type.IsUnsigned ? Value.UnsignedInteger((ulong)number, type.Kind) : Value.Signed((long)number, type.Kind);
    }
    public static Value Binary(string operation, Value x, Value y, LabType type)
    {
        if (type.Kind == ValueKind.Array)
        {
            if (!x.Shape.SequenceEqual(y.Shape)) throw new ArgumentException("Elementwise arithmetic requires identical array shapes.");
            return Value.Array(type.Element!, x.Shape, Enumerable.Range(0, x.Count).Select(i => Binary(operation, x.ElementAt(i), y.ElementAt(i), type.Element!)));
        }
        if (type.IsInteger && operation is "add" or "subtract" or "multiply" or "min" or "max")
        {
            var a = ExactInteger(x); var b = ExactInteger(y);
            return WrapInteger(operation switch { "add" => a + b, "subtract" => a - b, "multiply" => a * b, "min" => BigInteger.Min(a, b), _ => BigInteger.Max(a, b) }, type);
        }
        if (type.Kind == ValueKind.Complex)
        {
            var a = x.Complex; var b = y.Complex;
            return Value.ComplexNumber(operation switch { "add" => a + b, "subtract" => a - b, "multiply" => a * b, "divide" => b == Complex.Zero ? throw new DivideByZeroException() : a / b, "power" => Complex.Pow(a, b), _ => throw new ArgumentException("Complex numbers have no total order.") });
        }
        var result = operation switch { "add" => x.Number + y.Number, "subtract" => x.Number - y.Number, "multiply" => x.Number * y.Number, "divide" => y.Number == 0 ? throw new DivideByZeroException() : x.Number / y.Number, "power" => Math.Pow(x.Number, y.Number), "min" => Math.Min(x.Number, y.Number), "max" => Math.Max(x.Number, y.Number), _ => throw new ArgumentException("Unknown arithmetic operation.") };
        return Convert(Value.Numeric(result), type);
    }
}

/// <summary>Strict data literals, not executable expressions. JSON numbers retain exact integer bits.</summary>
public static class ValueLiteral
{
    public static Value Parse(LabType type, string text)
    {
        type.Validate(); if (text.Length > 1000000) throw new ArgumentException("Literal exceeds the length limit.");
        if (type.Kind == ValueKind.String) return Value.String(text);
        if (string.IsNullOrWhiteSpace(text)) return Value.Default(type);
        using var document = JsonDocument.Parse(text, new() { MaxDepth = 24 });
        return Read(type, document.RootElement);
    }
    private static Value Read(LabType type, JsonElement json)
    {
        if (type.IsUnsigned) return Value.UnsignedInteger(json.GetUInt64(), type.Kind);
        if (type.IsInteger) return Value.Signed(json.GetInt64(), type.Kind);
        switch (type.Kind)
        {
            case ValueKind.Number: case ValueKind.Single: return Value.Real(json.GetDouble(), type.Kind);
            case ValueKind.Boolean: return Value.Bool(json.GetBoolean());
            case ValueKind.String: return Value.String(json.GetString()!);
            case ValueKind.Complex: ExactProperties(json, "real", "imaginary"); return Value.ComplexNumber(new(json.GetProperty("real").GetDouble(), json.GetProperty("imaginary").GetDouble()));
            case ValueKind.Enum:
                var index = json.ValueKind == JsonValueKind.String ? type.Labels.IndexOf(json.GetString()!) : json.GetInt32();
                return Value.EnumValue(type, index);
            case ValueKind.Cluster:
                ExactProperties(json, type.Fields.Select(f => f.Name).ToArray());
                return Value.Cluster(type, type.Fields.Select(f => KeyValuePair.Create(f.Name, Read(f.Type, json.GetProperty(f.Name)))));
            case ValueKind.Error:
                ExactProperties(json, "status", "code", "source"); return Value.Error(json.GetProperty("status").GetBoolean(), json.GetProperty("code").GetInt32(), json.GetProperty("source").GetString()!);
            case ValueKind.Array:
                var shape = Enumerable.Repeat(-1, type.Rank).ToArray(); var values = new List<Value>();
                void Visit(JsonElement e, int dimension)
                {
                    if (dimension == type.Rank) { if (values.Count == 65536) throw new ArgumentException("Array literal is too large."); values.Add(Read(type.Element!, e)); return; }
                    var length = e.GetArrayLength(); if (shape[dimension] == -1) shape[dimension] = length; else if (shape[dimension] != length) throw new ArgumentException("Array literals must be rectangular.");
                    foreach (var child in e.EnumerateArray()) Visit(child, dimension + 1);
                }
                Visit(json, 0); for (var i = 0; i < shape.Length; i++) if (shape[i] == -1) shape[i] = 0;
                return Value.Array(type.Element!, shape, values);
            case ValueKind.Waveform:
                ExactProperties(json, "samples", "rate", "start"); return Value.Series(json.GetProperty("samples").EnumerateArray().Select(x => x.GetDouble()), json.GetProperty("rate").GetDouble(), json.GetProperty("start").GetDouble());
            default: throw new NotSupportedException("Construct variants with To Variant rather than a literal.");
        }
    }
    private static void ExactProperties(JsonElement json, params string[] names)
    {
        var properties = json.EnumerateObject().Select(p => p.Name).ToArray();
        if (properties.Length != names.Length || properties.Distinct(StringComparer.Ordinal).Count() != properties.Length || properties.Except(names, StringComparer.Ordinal).Any()) throw new ArgumentException("Literal fields must exactly match the declared type.");
    }
}
