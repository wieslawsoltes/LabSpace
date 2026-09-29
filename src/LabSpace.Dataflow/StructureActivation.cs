using LabSpace.Core;

namespace LabSpace.Dataflow;

/// <summary>One structure invocation. Histories and collection builders are owned by this activation; persistent state is staged until the root frame commits.</summary>
internal sealed class StructureActivation
{
    private readonly DataflowRuntime _runtime;
    private readonly CompiledNode _node;
    private readonly ExecutionBudget _budget;
    private readonly Dictionary<string, Value> _pending;
    private readonly Dictionary<string, Value> _arguments = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Value> _inputs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Value[]> _registers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<double>> _collections = new(StringComparer.Ordinal);
    private CompiledGraph _body;
    private readonly bool _sequence;
    private readonly Dictionary<string, Value> _locals = new(StringComparer.Ordinal);
    private int _sequenceIndex;
    private readonly string _path;
    private readonly int _count;
    private readonly bool _legacy;
    private Value _legacyState;
    private int _iteration;
    public ExecutionFrame? Child { get; private set; }
    public bool Completed { get; private set; }
    public Dictionary<string, Value> Results { get; } = new(StringComparer.Ordinal);

    public StructureActivation(DataflowRuntime runtime, CompiledNode node, Func<string, Value> input, ExecutionBudget budget, string parentPath, Dictionary<string, Value> pending)
    {
        _runtime = runtime; _node = node; _budget = budget; _pending = pending;
        var n = node.Model; var alternative = n.Kind == "case" && !input("selector").Boolean;
        _sequence = n.Kind == "sequence";
        var selected = n.Kind == "case-multi" ? node.Cases!.Select(input("selector")) : 0;
        _body = StructureFrames.HasFrames(n) ? node.Frames[selected] : alternative ? node.Alternative! : node.Body!;
        _path = parentPath + "/" + n.Id + (n.Kind == "case" ? alternative ? "/false" : "/true" : n.Kind == "case-multi" ? "/case:" + n.Frames[selected].Id : "");
        _legacy = n.Contract is null;
        _count = n.Kind is "while" ? 10000 : n.Kind == "for" ? Count(input("count").Number) : 1;
        _legacyState = _legacy ? input(n.Kind == "subvi" ? "x" : "initial") : Value.Numeric(0);
        if (n.Contract is not { } c) return;
        foreach (var t in c.Inputs)
        {
            var value = input(t.Name); _inputs[t.Name] = value;
            if (t.Indexing && n.Kind == "for") _count = Math.Min(_count, value.Samples.Length);
        }
        foreach (var r in c.Registers)
        {
            var history = new Value[r.HistoryDepth];
            for (var i = 0; i < history.Length; i++)
            {
                var suffix = r.Name + (i == 0 ? "" : ":" + i);
                history[i] = r.Initialized ? input("initial:" + suffix) : runtime.State(_path + "/register:" + suffix, ValueDefaults.Create(r.Type), pending);
            }
            _registers[r.Name] = history;
        }
        foreach (var t in c.Outputs)
        {
            Results[t.Name] = ValueDefaults.Create(t.ExternalType);
            if (t.Mode != TunnelMode.LastValue) _collections[t.Name] = new List<double>(Math.Min(_count, 4096));
        }
    }
    public void StepInto()
    {
        if (Completed) return;
        if (Child is null)
        {
            if (_iteration >= _count) { Finish(); return; }
            PrepareArguments();
            Child = new(_runtime, _body, _budget, _arguments,
                _sequence ? _path + "/frame:" + _node.Model.Frames[_sequenceIndex].Id : _path, _pending);
            return;
        }
        Child.StepInto();
        if (!Child.Completed) return;
        if (_sequence)
        {
            foreach (var (name, id) in _body.ConnectorOutputs) Results[name] = Child.Values[id];
            foreach (var (name, id) in _body.LocalOutputs) _locals["local:" + name] = Child.Values[id];
            Child = null; _sequenceIndex++;
            if (_sequenceIndex == _node.Frames.Count) { Finish(); return; }
            _body = _node.Frames[_sequenceIndex]; return;
        }
        Collect(Child);
        var conditional = _node.Model.Kind == "while" || _node.Model.Contract?.ConditionalFor == true;
        var stop = conditional && Child.Values[_body.ConditionNode!].Boolean;
        if (_node.Model.Contract?.ContinueWhenTrue == true) stop = conditional && !stop;
        _iteration++; Child = null;
        if (stop) Finish();
        else if (_iteration >= _count)
        {
            if (_node.Model.Kind == "while") throw new ExecutionLimitException("While loop did not terminate within 10,000 iterations.");
            Finish();
        }
    }
    private void PrepareArguments()
    {
        _arguments["i"] = Value.Numeric(_iteration); _arguments["N"] = Value.Numeric(_count);
        if (_sequence) foreach (var (name, value) in _locals) _arguments[name] = value;
        if (_legacy) { _arguments["state"] = _arguments["x"] = _legacyState; return; }
        foreach (var t in _node.Model.Contract!.Inputs)
        {
            var value = _inputs[t.Name];
            // While auto-indexing does not cap the iteration count; beyond the array it supplies the element default.
            _arguments[t.Name] = t.Indexing ? Value.Numeric(_iteration < value.Samples.Length ? value.Samples[_iteration] : 0) : value;
        }
        foreach (var (name, history) in _registers)
            for (var i = 0; i < history.Length; i++) _arguments[name + (i == 0 ? "" : ":" + i)] = history[i];
    }
    private Value Output(ExecutionFrame child, string name, ValueKind type) => _body.ConnectorOutputs.TryGetValue(name, out var id) ? child.Values[id] : ValueDefaults.Create(type);
    private void Collect(ExecutionFrame child)
    {
        if (_legacy) { _legacyState = child.Values[_body.ConnectorOutputs.Values.Single()]; return; }
        var c = _node.Model.Contract!;
        foreach (var t in c.Outputs)
        {
            var value = Output(child, t.Name, t.Type);
            if (t.Mode == TunnelMode.LastValue) { Results[t.Name] = value; continue; }
            var list = _collections[t.Name];
            if (t.Mode == TunnelMode.ConditionalIndexing && !Output(child, t.Condition, ValueKind.Boolean).Boolean) continue;
            var added = t.Mode == TunnelMode.Concatenating ? value.Samples.Length : 1;
            if (list.Count > 65536 - added) throw new ExecutionLimitException("Loop output exceeds the 65,536 element limit.");
            if (t.Mode == TunnelMode.Concatenating) list.AddRange(value.Samples);
            else list.Add(value.Number);
        }
        foreach (var r in c.Registers)
        {
            var history = _registers[r.Name]; var next = Output(child, r.Name, r.Type);
            for (var i = history.Length - 1; i > 0; i--) history[i] = history[i - 1];
            history[0] = next;
        }
    }
    private void Finish()
    {
        if (_legacy) Results["result"] = _legacyState;
        else
        {
            foreach (var (name, samples) in _collections) Results[name] = Value.Vector(samples);
            foreach (var r in _node.Model.Contract!.Registers)
            {
                var history = _registers[r.Name]; Results[r.Name] = history[0];
                if (!r.Initialized)
                    for (var i = 0; i < history.Length; i++) _pending[_path + "/register:" + r.Name + (i == 0 ? "" : ":" + i)] = history[i];
            }
        }
        Completed = true;
    }
    private static int Count(double count) => double.IsFinite(count) && count >= 0 && count <= 10000 && count == Math.Truncate(count) ? (int)count : throw new ArgumentOutOfRangeException(nameof(count), "count must be an integer in [0,10000].");
}
