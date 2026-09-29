using LabSpace.Core;

namespace LabSpace.Dataflow;

internal interface IStructureActivation
{
    ExecutionFrame? Child { get; }
    bool Completed { get; }
    Dictionary<string, Value> Results { get; }
    void StepInto();
}

/// <summary>Case and sequence activations share the root's cancellation, budget and transaction.</summary>
internal sealed class FrameActivation : IStructureActivation
{
    private readonly DataflowRuntime _runtime;
    private readonly CompiledNode _node;
    private readonly FrameProgram _program;
    private readonly ExecutionBudget _budget;
    private readonly Dictionary<string, Value> _pending;
    private readonly Dictionary<string, Value> _arguments = new(StringComparer.Ordinal);
    private readonly string _path;
    private int _index;
    public ExecutionFrame? Child { get; private set; }
    public bool Completed { get; private set; }
    public Dictionary<string, Value> Results { get; } = new(StringComparer.Ordinal);
    public FrameActivation(DataflowRuntime runtime, CompiledNode node, FrameProgram program, Func<string, Value> input, ExecutionBudget budget, string path, Dictionary<string, Value> pending)
    {
        _runtime = runtime; _node = node; _program = program; _budget = budget; _pending = pending; _path = path + "/" + node.Model.Id;
        _index = program.IsSequence ? 0 : program.Select(input("selector"));
        foreach (var tunnel in node.Model.Contract!.Inputs) _arguments[tunnel.Name] = input(tunnel.Name);
        foreach (var tunnel in node.Model.Contract.Outputs) Results[tunnel.Name] = ValueDefaults.Create(tunnel.Type);
    }
    public void StepInto()
    {
        if (Completed) return;
        _budget.CheckCancellation();
        var frame = _program.Frames[_index];
        if (Child is null)
        {
            _budget.Consume();
            Child = new(_runtime, frame.Graph, _budget, _arguments, _path + "/" + frame.Id, _pending); return;
        }
        Child.StepInto();
        if (!Child.Completed) return;
        foreach (var tunnel in _node.Model.Contract!.Outputs)
            if (frame.Graph.ConnectorOutputs.TryGetValue(tunnel.Name, out var id)) Results[tunnel.Name] = Child.Values[id];
        foreach (var local in _node.Model.Contract.Locals.Where(l => l.SourceFrameId == frame.Id))
            _arguments[local.Name] = Child.Values[frame.Graph.ConnectorOutputs[local.Name]];
        Child = null;
        if (!_program.IsSequence || ++_index == _program.Frames.Count) Completed = true;
    }
}
