using System.Diagnostics;
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
    public void Consume() { cancellation.ThrowIfCancellationRequested(); if (++Evaluated > maximum) throw new ExecutionLimitException("The execution budget was exceeded. Reduce loop counts or diagram size."); }
}
public sealed class DataflowRuntime
{
    private readonly Dictionary<string, Value> _feedback = [];
    private readonly Random _random = new(42);
    public double Time { get; private set; }
    public long Frames { get; private set; }
    public ExecutionFrame Start(CompiledGraph graph, CancellationToken cancellation = default, int maximumNodes = 100000) => new(this, graph, new(maximumNodes, cancellation), new Dictionary<string, double>(), "root");
    public ExecutionFrame Run(CompiledGraph graph, CancellationToken cancellation = default, int maximumNodes = 100000)
    {
        var frame = Start(graph, cancellation, maximumNodes); while (!frame.Completed) frame.Step(); return frame;
    }
    public void Reset() { _feedback.Clear(); Time = 0; Frames = 0; }
    internal Value Feedback(string key, double initial) => _feedback.TryGetValue(key, out var value) ? value : Value.Numeric(initial);
    internal void Commit(Dictionary<string, Value> pending) { foreach (var pair in pending) _feedback[pair.Key] = pair.Value; Time += .04; Frames++; }
    internal double Random() => _random.NextDouble();
    internal Value Evaluate(CompiledNode compiled, Func<string, Value> input, ExecutionBudget budget, IReadOnlyDictionary<string, double> args, string path, Dictionary<string, Value> pending)
    {
        var n = compiled.Model; double N(string name) => input(name).Number; bool B(string name) => input(name).Boolean; var kind = n.Kind;
        switch (kind)
        {
            case "constant": case "control": return Value.Numeric(n.Value);
            case "bool": case "bool-control": return Value.Bool(n.Value != 0);
            case "string": case "string-control": return Value.String(n.Text);
            case "indicator": case "bool-indicator": case "string-indicator": case "array-indicator": case "graph": case "chart": case "output": case "stop": return input("x");
            case "input": return Value.Numeric(args.TryGetValue(n.Text, out var arg) ? arg : n.Value);
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
            case "feedback": return Feedback(path + "/" + n.Id, n.Value);
            case "for": case "while": case "case": case "subvi":
                var state = kind == "subvi" ? N("x") : N("initial");
                var count = kind == "for" ? Integer(N("count"), 0, 10000, "count") : kind == "while" ? 10000 : 1;
                var body = kind == "case" && !B("selector") ? compiled.Alternative! : compiled.Body!;
                var stopped = false;
                for (var i = 0; i < count; i++)
                {
                    var child = new ExecutionFrame(this, body, budget, new Dictionary<string, double> { ["state"] = state, ["i"] = i, ["x"] = state }, path + "/" + n.Id, pending);
                    while (!child.Completed) child.Step();
                    state = child.Values[body.Order.Single(x => x.Model.Kind == "output").Model.Id].Number;
                    if (kind == "while" && child.Values[body.Order.Single(x => x.Model.Kind == "stop").Model.Id].Boolean) { stopped = true; break; }
                }
                if (kind == "while" && !stopped) throw new ExecutionLimitException("While loop did not terminate within 10,000 iterations.");
                return Value.Numeric(state);
            default: throw new NotSupportedException($"Kernel '{kind}' is not implemented.");
        }
    }
    private static int Integer(double value, int minimum, int maximum, string name)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum || value != Math.Truncate(value)) throw new ArgumentOutOfRangeException(name, $"{name} must be an integer between {minimum} and {maximum}.");
        return (int)value;
    }
}

/// <summary>A resumable top-level frame. Nested structures execute as a single step-over operation.</summary>
public sealed class ExecutionFrame
{
    private readonly DataflowRuntime _runtime;
    private readonly CompiledGraph _graph;
    private readonly ExecutionBudget _budget;
    private readonly IReadOnlyDictionary<string, double> _arguments;
    private readonly string _path;
    private readonly Dictionary<string, Value> _pending;
    private readonly bool _root;
    private int _next;
    private readonly Stopwatch _watch = new();
    public Dictionary<string, Value> Values { get; } = [];
    public bool Completed { get; private set; }
    public string? NextNodeId => _next < _graph.Order.Count ? _graph.Order[_next].Model.Id : null;
    public string? LastNodeId { get; private set; }
    public int EvaluatedNodes => _budget.Evaluated;
    public double ElapsedMilliseconds => _watch.Elapsed.TotalMilliseconds;
    internal ExecutionFrame(DataflowRuntime runtime, CompiledGraph graph, ExecutionBudget budget, IReadOnlyDictionary<string, double> arguments, string path, Dictionary<string, Value>? pending = null)
    {
        _runtime = runtime; _graph = graph; _budget = budget; _arguments = arguments; _path = path; _root = pending is null; _pending = pending ?? [];
        // Feedback outputs are available before their new inputs, independently of placement order.
        foreach (var node in graph.Order.Where(x => x.Model.Kind == "feedback")) Values[node.Model.Id] = runtime.Feedback(path + "/" + node.Model.Id, node.Model.Value);
    }
    public void Step()
    {
        if (Completed) return;
        _watch.Start();
        try
        {
            if (_next < _graph.Order.Count)
            {
                var node = _graph.Order[_next]; _budget.Consume();
                Value Input(string name)
                {
                    if (node.Sources.TryGetValue(name, out var source)) return Values[source];
                    var port = node.Definition.Inputs.Single(p => p.Name == name);
                    return port.Kind switch { ValueKind.Boolean => Value.Bool(port.Default != 0), ValueKind.String => Value.String(""), _ => Value.Numeric(node.Model.Parameter(name, port.Default)) };
                }
                try { Values[node.Model.Id] = _runtime.Evaluate(node, Input, _budget, _arguments, _path, _pending); }
                catch (Exception ex) when (ex is not OperationCanceledException and not NodeExecutionException) { throw new NodeExecutionException(node.Model.Id, node.Model.Label, ex); }
                LastNodeId = node.Model.Id; _next++;
            }
            if (_next == _graph.Order.Count)
            {
                foreach (var node in _graph.Order.Where(n => n.Model.Kind == "feedback"))
                    if (node.Sources.TryGetValue("x", out var source)) _pending[_path + "/" + node.Model.Id] = Values[source];
                if (_root) _runtime.Commit(_pending);
                Completed = true;
            }
        }
        finally { _watch.Stop(); }
    }
}
