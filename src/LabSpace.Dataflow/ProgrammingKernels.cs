using LabSpace.Core;
using System.Numerics;

namespace LabSpace.Dataflow;

internal static class ProgrammingKernels
{
    public static bool IsMultiOutput(string kind) => kind is "error-unbundle" or "complex-parts";
    public static bool Supports(string kind) => kind.StartsWith("error-", StringComparison.Ordinal) || kind.StartsWith("complex-", StringComparison.Ordinal) || kind is "select-error" or "select-complex" or "waveform-constant";
    public static Dictionary<string, Value> Evaluate(Node node, Func<string, Value> input)
    {
        double N(string name) => input(name).Number;
        Complex C(string name) { var v = input(name); return new(v.Number, v.Imaginary); }
        static Value V(Complex value) => Value.Complex(value.Real, value.Imaginary);
        if (node.Kind == "error-unbundle")
        {
            var e = input("x").Error;
            return new() { ["status"] = Value.Bool(e.Status), ["code"] = Value.Numeric(e.Code), ["source"] = Value.String(e.Source) };
        }
        if (node.Kind == "complex-parts")
        {
            var v = input("x"); return new() { ["real"] = Value.Numeric(v.Number), ["imaginary"] = Value.Numeric(v.Imaginary) };
        }
        var result = node.Kind switch
        {
            "waveform-constant" => Value.Series(node.Text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(s => double.Parse(s, System.Globalization.CultureInfo.InvariantCulture)), node.Parameter("rate", 1), node.Parameter("start", 0)),
            "error-constant" => Value.ErrorCluster(node.Parameter("status", node.Value == 0 ? 0 : 1) != 0, Code(node.Value), node.Text),
            "error-bundle" => Value.ErrorCluster(input("status").Boolean, Code(N("code")), input("source").Text),
            "error-indicator" or "complex-indicator" => input("x"),
            "error-clear" => Value.ErrorCluster(false, 0),
            // Errors take precedence over warnings; input order breaks ties.
            "error-merge" => Merge(input("x"), input("y")),
            "complex-constant" => Value.Complex(node.Value, node.Parameter("imaginary", 0)),
            "complex-build" => Value.Complex(N("real"), N("imaginary")),
            "complex-add" => V(C("x") + C("y")),
            "complex-subtract" => V(C("x") - C("y")),
            "complex-multiply" => V(C("x") * C("y")),
            "complex-divide" => C("y") == Complex.Zero ? throw new DivideByZeroException() : V(C("x") / C("y")),
            "complex-conjugate" => V(Complex.Conjugate(C("x"))),
            "complex-magnitude" => Value.Numeric(Complex.Abs(C("x"))),
            "complex-phase" => Value.Numeric(C("x").Phase),
            "select-error" or "select-complex" => input("selector").Boolean ? input("x") : input("y"),
            _ => throw new ArgumentException("Unknown programming function: " + node.Kind)
        };
        return new() { ["result"] = result };
    }
    private static int Code(double value) => value >= int.MinValue && value <= int.MaxValue && value == Math.Truncate(value) ? (int)value : throw new ArgumentOutOfRangeException(nameof(value), "Error code must be a signed 32-bit integer.");
    private static Value Merge(Value x, Value y) => x.Error.Status ? x : y.Error.Status ? y : x.Error.Code != 0 ? x : y.Error.Code != 0 ? y : x;
}
