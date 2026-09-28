using System.Globalization;
using LabSpace.Core;
using LabSpace.Signals;

namespace LabSpace.Dataflow;

public sealed class ExecutionLimitException(string message) : Exception(message);
public sealed class NodeExecutionException(string nodeId, string label, Exception inner) : Exception($"{label}: {inner.Message}", inner)
{
    public string NodeId { get; } = nodeId;
}
public sealed class ExecutionBudget(int maximum, CancellationToken cancellation = default)
{
    public int Evaluated { get; private set; }
    public void CheckCancellation() => cancellation.ThrowIfCancellationRequested();
    public void Consume() { CheckCancellation(); if (++Evaluated > maximum) throw new ExecutionLimitException("The execution budget was exceeded. Reduce loop counts or diagram size."); }
}
public sealed class DataflowRuntime
{
    private readonly Dictionary<string, Value> _feedback = [];
    private Random _random = new(42);
    public double Time { get; private set; }
    public long Frames { get; private set; }
    public ExecutionFrame Start(CompiledGraph graph, CancellationToken cancellation = default, int maximumNodes = 100000) => new(this, graph, new(maximumNodes, cancellation), new Dictionary<string, Value>(), "root");
    public ExecutionFrame Run(CompiledGraph graph, CancellationToken cancellation = default, int maximumNodes = 100000)
    {
        var frame = Start(graph, cancellation, maximumNodes); while (!frame.Completed) frame.Step(); return frame;
    }
    public void Reset() { _feedback.Clear(); _random = new(42); Time = 0; Frames = 0; }
    internal Value State(string key, Value initial, Dictionary<string, Value> pending) => pending.TryGetValue(key, out var staged) ? staged : _feedback.GetValueOrDefault(key, initial);
    internal void Commit(Dictionary<string, Value> pending) { foreach (var pair in pending) _feedback[pair.Key] = pair.Value; Time += .04; Frames++; }
    internal double Random() => _random.NextDouble();
    internal Value Evaluate(CompiledNode compiled, Func<string, Value> input, ExecutionBudget budget, IReadOnlyDictionary<string, Value> args, string path, Dictionary<string, Value> pending)
    {
        var n = compiled.Model; double N(string name) => input(name).Number; bool B(string name) => input(name).Boolean; var kind = n.Kind;
        switch (kind)
        {
            case "constant": case "control": return Value.Numeric(n.Value);
            case "bool": case "bool-control": return Value.Bool(n.Value != 0);
            case "string": case "string-control": return Value.String(n.Text);
            case "indicator": case "bool-indicator": case "string-indicator": case "array-indicator": case "graph": case "chart": case "output": case "stop": return input("x");
            case "input": return args.TryGetValue(n.Text, out var arg) ? arg : ValueDefaults.Create(n.DataType, n.Value);
            case "add": return Value.Numeric(N("x") + N("y"));
            case "subtract": return Value.Numeric(N("x") - N("y"));
            case "multiply": return Value.Numeric(N("x") * N("y"));
            case "divide": if (N("y") == 0) throw new DivideByZeroException("Cannot divide by zero."); return Value.Numeric(N("x") / N("y"));
            case "power": return Value.Numeric(Math.Pow(N("x"), N("y")));
            case "min": return Value.Numeric(Math.Min(N("x"), N("y")));
            case "max": return Value.Numeric(Math.Max(N("x"), N("y")));
            case "sin": return Value.Numeric(Math.Sin(N("x")));
            case "cos": return Value.Numeric(Math.Cos(N("x")));
            case "sqrt": return Value.Numeric(Math.Sqrt(N("x")));
            case "abs": return Value.Numeric(Math.Abs(N("x")));
            case "round": return Value.Numeric(Math.Round(N("x")));
            case "greater": return Value.Bool(N("x") > N("y"));
            case "less": return Value.Bool(N("x") < N("y"));
            case "equal": return Value.Bool(N("x") == N("y"));
            case "and": return Value.Bool(B("x") && B("y"));
            case "or": return Value.Bool(B("x") || B("y"));
            case "not": return Value.Bool(!B("x"));
            case "select": return B("selector") ? input("x") : input("y");
            case "concat": var text = input("x").Text + input("y").Text; if (text.Length > 1000000) throw new ExecutionLimitException("String length limit exceeded."); return Value.String(text);
            case "length": return Value.Numeric(input("x").Text.Length);
            case "format": return Value.String(input("x").ToString());
            case "array": return Value.Vector(n.Text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(x => double.Parse(x, CultureInfo.InvariantCulture)));
            case "build-array": return Value.Vector([N("x"), N("y")]);
            case "array-size": return Value.Numeric(input("x").Samples.Length);
            case "index": var index = N("index"); var array = input("x").Samples; if (index < 0 || index >= array.Length || index != Math.Truncate(index)) throw new ArgumentOutOfRangeException("index", "Array index is outside the array or is not an integer."); return Value.Numeric(array[(int)index]);
            case "sum": return Value.Numeric(input("x").Samples.Sum());
            case "mean": return Value.Numeric(input("x").Samples.Average());
            case "simulate": return SignalMath.Generate(Integer(n.Parameter("count", 512), 2, 65536, "count"), n.Parameter("rate", 2000), N("frequency"), N("amplitude"), Time, n.Text.Length == 0 ? "Sine" : n.Text, n.Parameter("noise", 0), unchecked((int)Frames + 42));
            case "gain": return SignalMath.Transform(input("x"), x => x * N("gain"));
            case "offset": return SignalMath.Transform(input("x"), x => x + N("offset"));
            case "filter": return SignalMath.MovingAverage(input("x"), Integer(n.Parameter("window", 8), 1, 1024, "window"));
            case "rms": return Value.Numeric(SignalMath.Rms(input("x").Samples));
            case "peak": return Value.Numeric(input("x").Samples.Max() - input("x").Samples.Min());
            case "fft": return SignalMath.Spectrum(input("x"));
            case "to-array": return Value.Vector(input("x").Samples);
            case "time": return Value.Numeric(Time);
            case "random": return Value.Numeric(Random());
            case "feedback": return State(path + "/" + n.Id, Value.Numeric(n.Value), pending);
            default: return ExtendedKernels.Evaluate(n, input);
        }
    }
    private static int Integer(double value, int minimum, int maximum, string name)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum || value != Math.Truncate(value)) throw new ArgumentOutOfRangeException(name, $"{name} must be an integer between {minimum} and {maximum}.");
        return (int)value;
    }
}
