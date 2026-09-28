using System.Diagnostics;
using LabSpace.Core;

namespace LabSpace.Dataflow;

/// <summary>A resumable activation. Step preserves step-over semantics; StepInto performs bounded nested work and permits cooperative UI scheduling.</summary>
public sealed class ExecutionFrame
{
    private readonly DataflowRuntime _runtime;
    private readonly ExecutionBudget _budget;
    private readonly IReadOnlyDictionary<string, Value> _arguments;
    private readonly Dictionary<string, Value> _pending;
    private readonly bool _root;
    private readonly Stopwatch _watch = new();
    private int _next;
    private StructureActivation? _structure;
    public CompiledGraph Graph { get; }
    public string Path { get; }
    public Dictionary<string, Value> Values { get; } = new(StringComparer.Ordinal);
    public Dictionary<SourceTerminal, Value> Outputs { get; } = [];
    public bool Completed { get; private set; }
    public string? NextNodeId => _next < Graph.Order.Count ? Graph.Order[_next].Model.Id : null;
    public string? LastNodeId { get; private set; }
    public int EvaluatedNodes => _budget.Evaluated;
    public double ElapsedMilliseconds => _watch.Elapsed.TotalMilliseconds;
    public ExecutionFrame ActiveFrame => _structure?.Child?.ActiveFrame ?? this;
    public Node? NextNode => _next < Graph.Order.Count ? Graph.Order[_next].Model : null;
    public bool IsInsideStructure => _structure is not null;

    internal ExecutionFrame(DataflowRuntime runtime, CompiledGraph graph, ExecutionBudget budget, IReadOnlyDictionary<string, Value> arguments, string path, Dictionary<string, Value>? pending = null)
    {
        _runtime = runtime; Graph = graph; _budget = budget; _arguments = arguments; Path = path;
        _root = pending is null; _pending = pending ?? [];
        foreach (var node in graph.Order.Where(x => x.Model.Kind == "feedback"))
        {
            var value = runtime.State(path + "/" + node.Model.Id, Value.Numeric(node.Model.Value), _pending);
            Values[node.Model.Id] = value; Outputs[new(node.Model.Id, "result")] = value;
        }
    }
    public Value GetOutput(string nodeId, string output = "result") => Outputs.TryGetValue(new(nodeId, output), out var value) ? value : output == "result" ? Values[nodeId] : throw new KeyNotFoundException($"Output '{output}' on node '{nodeId}' is not available.");
    public void Step()
    {
        var next = _next;
        do { StepInto(); } while (!Completed && _next == next);
    }
    public void StepInto()
    {
        if (Completed) return;
        _watch.Start();
        try
        {
            if (_next == Graph.Order.Count) { Finish(); return; }
            var node = Graph.Order[_next];
            try
            {
                if (_structure is not null)
                {
                    _structure.StepInto();
                    if (!_structure.Completed) return;
                    var results = _structure.Results;
                    foreach (var (name, result) in results) Outputs[new(node.Model.Id, name)] = result;
                    Values[node.Model.Id] = node.Definition.Outputs.Length > 0 ? results[node.Definition.Outputs[0].Name] : Value.Numeric(0);
                    _structure = null;
                }
                else
                {
                    _budget.Consume();
                    Value Input(string name)
                    {
                        if (node.Sources.TryGetValue(name, out var source)) return GetOutput(source.NodeId, source.Output);
                        var port = node.Definition.Inputs.First(p => p.Name == name);
                        return ValueDefaults.Create(port.Kind, node.Model.Parameter(name, port.Default));
                    }
                    if (node.Definition.IsStructure)
                    {
                        _structure = new(_runtime, node, Input, _budget, Path, _pending);
                        LastNodeId = node.Model.Id;
                        return;
                    }
                    var value = _runtime.Evaluate(node, Input, _budget, _arguments, Path, _pending);
                    Values[node.Model.Id] = value;
                    foreach (var output in node.Definition.Outputs) Outputs[new(node.Model.Id, output.Name)] = value;
                }
                LastNodeId = node.Model.Id; _next++;
                if (_next == Graph.Order.Count) Finish();
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not NodeExecutionException)
            {
                throw new NodeExecutionException(node.Model.Id, node.Model.Label, ex);
            }
        }
        finally { _watch.Stop(); }
    }
    private void Finish()
    {
        foreach (var node in Graph.Order.Where(n => n.Model.Kind == "feedback"))
            if (node.Sources.TryGetValue("x", out var source)) _pending[Path + "/" + node.Model.Id] = GetOutput(source.NodeId, source.Output);
        if (_root) _runtime.Commit(_pending);
        Completed = true;
    }
}
