using System.Collections.Immutable;
using System.Globalization;

namespace LabSpace.Core;

public enum ValueKind { Number, Boolean, String, Array, Waveform, Error, Complex }

/// <summary>Immutable runtime payload. Array factories copy caller buffers to prevent cross-wire mutation.</summary>
public sealed record Value
{
    public ValueKind Kind { get; private init; }
    public double Number { get; private init; }
    public bool Boolean { get; private init; }
    public string Text { get; private init; } = "";
    public ImmutableArray<double> Samples { get; private init; } = [];
    public double SampleRate { get; private init; } = 1;
    public double StartTime { get; private init; }
    public double Imaginary { get; private init; }
    public ErrorCluster Error { get; private init; } = new(false, 0, "");
    public static Value ErrorCluster(bool status, int code, string source = "")
    {
        if (source is null || source.Length > 1000000) throw new ArgumentException("Invalid error source.", nameof(source));
        return new() { Kind = ValueKind.Error, Error = new(status, code, source) };
    }
    public static Value Complex(double real, double imaginary)
    {
        if (!double.IsFinite(real) || !double.IsFinite(imaginary)) throw new ArithmeticException("Complex components must be finite.");
        return new() { Kind = ValueKind.Complex, Number = real, Imaginary = imaginary };
    }
    private Value() { }
    public static Value Numeric(double value)
    {
        if (!double.IsFinite(value)) throw new ArithmeticException("The operation produced a non-finite number.");
        return new() { Kind = ValueKind.Number, Number = value };
    }
    public static Value Bool(bool value) => new() { Kind = ValueKind.Boolean, Boolean = value };
    public static Value String(string value)
    {
        if (value?.Length > 1000000) throw new ArgumentException("Strings are limited to one million UTF-16 code units.");
        return new() { Kind = ValueKind.String, Text = value ?? "" };
    }
    public static Value Vector(IEnumerable<double> values) => Series(values, 1, 0, false);
    public static Value Series(IEnumerable<double> values, double sampleRate, double startTime = 0, bool waveform = true)
    {
        if (!double.IsFinite(sampleRate) || sampleRate <= 0 || !double.IsFinite(startTime)) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        var samples = values.Take(65537).ToImmutableArray();
        if (samples.Length > 65536 || samples.Any(x => !double.IsFinite(x))) throw new ArgumentException("A series must contain at most 65,536 finite samples.");
        return new() { Kind = waveform ? ValueKind.Waveform : ValueKind.Array, Samples = samples, SampleRate = sampleRate, StartTime = startTime };
    }
    public override string ToString() => Kind switch
    {
        ValueKind.Number => Number.ToString("G7", CultureInfo.InvariantCulture),
        ValueKind.Boolean => Boolean ? "TRUE" : "FALSE",
        ValueKind.String => Text,
        ValueKind.Error => $"{(Error.Status ? "Error" : Error.Code == 0 ? "No error" : "Warning")} {Error.Code}: {Error.Source}",
        ValueKind.Complex => Number.ToString("G7", CultureInfo.InvariantCulture) + (Imaginary < 0 ? " - " : " + ") + Math.Abs(Imaginary).ToString("G7", CultureInfo.InvariantCulture) + "i",
        _ => $"{Samples.Length:N0} samples" + (Kind == ValueKind.Waveform ? $" · {SampleRate:G6} Hz" : "")
    };
}
