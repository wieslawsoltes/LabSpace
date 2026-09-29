using System.Numerics;
using LabSpace.Core;

namespace LabSpace.Dataflow;

internal static class AdvancedKernels
{
    public static bool TryNamed(CompiledNode node, Func<string, Value> input, out IReadOnlyDictionary<string, Value> result)
    {
        result = null!;
        switch (node.Model.Kind)
        {
            case "formula":
                result = node.Formula!.Evaluate(name => input(name).Number).ToDictionary(p => p.Key, p => Value.Numeric(p.Value), StringComparer.Ordinal);
                return true;
            case "error-unbundle":
                var error = input("x").Error;
                result = new Dictionary<string, Value> { ["status"] = Value.Bool(error.Status), ["code"] = Value.Numeric(error.Code), ["source"] = Value.String(error.Source) };
                return true;
            case "complex-parts":
                var complex = input("x").Complex;
                result = new Dictionary<string, Value> { ["real"] = Value.Numeric(complex.Real), ["imaginary"] = Value.Numeric(complex.Imaginary) };
                return true;
            default: return false;
        }
    }
    public static bool TryEvaluate(Node node, Func<string, Value> input, out Value value)
    {
        static int Code(double number) => number >= int.MinValue && number <= int.MaxValue && number == Math.Truncate(number)
            ? (int)number : throw new ArgumentOutOfRangeException(nameof(number), "Error code must be a signed 32-bit integer.");
        Value ComplexResult(Complex c) => Value.ComplexValue(c.Real, c.Imaginary);
        value = null!;
        switch (node.Kind)
        {
            case "error-constant": case "error-control": value = Value.ErrorValue(node.Parameter("status", 0) != 0, Code(node.Value), node.Text); break;
            case "error-bundle": value = Value.ErrorValue(input("status").Boolean, Code(input("code").Number), input("source").Text); break;
            case "error-clear": _ = input("x"); value = Value.ErrorValue(); break;
            case "error-merge":
                var a = input("x"); var b = input("y");
                value = a.Error.Status ? a : b.Error.Status ? b : a.Error.Code != 0 ? a : b.Error.Code != 0 ? b : Value.ErrorValue(); break;
            case "complex": case "complex-control": value = Value.ComplexValue(node.Value, node.Parameter("imaginary", 0)); break;
            case "complex-build": value = Value.ComplexValue(input("real").Number, input("imaginary").Number); break;
            case "complex-add": value = ComplexResult(input("x").Complex + input("y").Complex); break;
            case "complex-subtract": value = ComplexResult(input("x").Complex - input("y").Complex); break;
            case "complex-multiply": value = ComplexResult(input("x").Complex * input("y").Complex); break;
            case "complex-divide":
                var denominator = input("y").Complex;
                if (denominator == Complex.Zero) throw new DivideByZeroException();
                value = ComplexResult(input("x").Complex / denominator); break;
            case "complex-conjugate": value = ComplexResult(Complex.Conjugate(input("x").Complex)); break;
            case "complex-magnitude": value = Value.Numeric(Complex.Abs(input("x").Complex)); break;
            case "complex-phase": value = Value.Numeric(input("x").Complex.Phase); break;
            default: return false;
        }
        return true;
    }
}
