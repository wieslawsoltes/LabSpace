using System.Globalization;
using System.Text;
using LabSpace.Core;

namespace LabSpace.Dataflow;

internal static class ExtendedKernels
{
    public static Value Evaluate(Node node, Func<string, Value> input)
    {
        double N(string name) => input(name).Number;
        bool B(string name) => input(name).Boolean;
        string S(string name) => input(name).Text;
        switch (node.Kind)
        {
            case "negate": return Value.Numeric(-N("x"));
            case "increment": return Value.Numeric(N("x") + 1);
            case "decrement": return Value.Numeric(N("x") - 1);
            case "floor": return Value.Numeric(Math.Floor(N("x")));
            case "ceiling": return Value.Numeric(Math.Ceiling(N("x")));
            case "truncate": return Value.Numeric(Math.Truncate(N("x")));
            case "exp": return Value.Numeric(Math.Exp(N("x")));
            case "log": return Value.Numeric(Math.Log(N("x")));
            case "log10": return Value.Numeric(Math.Log10(N("x")));
            case "tan": return Value.Numeric(Math.Tan(N("x")));
            case "sign": return Value.Numeric(Math.Sign(N("x")));
            case "reciprocal": return Value.Numeric(1 / N("x"));
            case "pi": return Value.Numeric(Math.PI);
            case "atan2": return Value.Numeric(Math.Atan2(N("y"), N("x")));
            case "remainder": return Value.Numeric(N("x") % N("y"));
            case "in-range": return Value.Bool(N("x") >= N("minimum") && N("x") <= N("maximum"));
            case "xor": return Value.Bool(B("x") ^ B("y"));
            case "nand": return Value.Bool(!(B("x") && B("y")));
            case "nor": return Value.Bool(!(B("x") || B("y")));
            case "select-boolean": case "select-string": case "select-array": case "select-waveform": return B("selector") ? input("x") : input("y");
            case "upper": return Value.String(S("x").ToUpperInvariant());
            case "lower": return Value.String(S("x").ToLowerInvariant());
            case "trim": return Value.String(S("x").Trim());
            case "reverse-string":
                var runes = S("x").EnumerateRunes().ToArray(); var reverse = new StringBuilder(S("x").Length);
                for (var i = runes.Length - 1; i >= 0; i--) reverse.Append(runes[i].ToString());
                return Value.String(reverse.ToString());
            case "substring":
                var text = S("x"); var offset = Math.Min(Index(N("offset"), 1000000), text.Length); var length = Math.Min(Index(N("length"), 1000000), text.Length - offset);
                return Value.String(text.Substring(offset, length));
            case "replace-string":
                var source = S("x"); var search = S("search"); var replacement = S("replacement");
                if (search.Length == 0) throw new ArgumentException("Search text must not be empty.");
                long size = source.Length; var position = 0;
                while ((position = source.IndexOf(search, position, StringComparison.Ordinal)) >= 0)
                {
                    size += replacement.Length - search.Length; position += search.Length;
                    if (size > 1000000) throw new ExecutionLimitException("Replacement exceeds the string limit.");
                }
                return Value.String(source.Replace(search, replacement, StringComparison.Ordinal));
            case "parse-number": return Value.Numeric(double.Parse(S("x"), NumberStyles.Float, CultureInfo.InvariantCulture));
            case "utf8-length": return Value.Numeric(Encoding.UTF8.GetByteCount(S("x")));
            case "initialize-array": return Value.Vector(Enumerable.Repeat(N("value"), Index(N("count"), 65536)));
            case "concat-array": return Value.Vector(input("x").Samples.Concat(input("y").Samples));
            case "reverse-array": return Value.Vector(input("x").Samples.Reverse());
            case "sort-array": var sorted = input("x").Samples.ToArray(); Array.Sort(sorted); return Value.Vector(sorted);
            case "subset-array": return Value.Vector(input("x").Samples.Skip(Index(N("offset"), 65536)).Take(Index(N("length"), 65536)));
            case "replace-array":
                var array = input("x").Samples.ToArray(); var index = Index(N("index"), 65536);
                if (index < array.Length) array[index] = N("value");
                return Value.Vector(array);
            case "search-array": return Value.Numeric(input("x").Samples.IndexOf(N("value")));
            case "build-waveform": return Value.Series(input("x").Samples, N("rate"), N("start"));
            case "waveform-rate": return Value.Numeric(input("x").SampleRate);
            case "waveform-dt": return Value.Numeric(1 / input("x").SampleRate);
            default: throw new NotSupportedException($"Kernel '{node.Kind}' is not implemented.");
        }
    }
    private static int Index(double value, int maximum) => double.IsFinite(value) && value >= 0 && value <= maximum && value == Math.Truncate(value) ? (int)value : throw new ArgumentOutOfRangeException(nameof(value), $"Expected an integer in [0,{maximum}].");
}
