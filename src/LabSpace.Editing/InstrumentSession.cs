using LabSpace.Core;
using LabSpace.Dataflow;
using LabSpace.Documents;

namespace LabSpace.Editing;

[Flags] public enum SessionChange { None = 0, View = 1, Selection = 2, Document = 4, Execution = 8, Navigation = 16, All = 31 }
public sealed record NavigationLevel(string NodeId, bool Alternative, int FrameIndex = -1);

/// <summary>UI-independent command and execution session. A caller-owned timer invokes Tick; no background thread touches the document.</summary>
public sealed partial class InstrumentSession
{
    private readonly List<string> _undo = [], _redo = [];
    private readonly List<NavigationLevel> _path = [];
    private readonly DataflowRuntime _runtime = new();
    private CompiledGraph? _plan;
    private ExecutionFrame? _frame;
    private string? _gesture, _clipboard, _skipBreakpoint;
    private bool _continuous;
    public LabProject Project { get; private set; }
    public string ActiveId { get; private set; }
    public VirtualInstrument Instrument => Project.Instruments.First(v => v.Id == ActiveId);
    public IReadOnlyList<NavigationLevel> Path => _path;
    public Diagram Diagram
    {
        get
        {
            var graph = Instrument.Diagram;
            foreach (var level in _path)
            {
                var n = graph.Nodes.FirstOrDefault(n => n.Id == level.NodeId);
                var child = ResolveChild(n, level);
                if (child is null) return Instrument.Diagram;
                graph = child;
            }
            return graph;
        }
    }
    public HashSet<string> Selection { get; } = [];
    public string? SelectedWire { get; private set; }
    public IReadOnlyDictionary<string, Value> Values { get; private set; } = new Dictionary<string, Value>();
    public IReadOnlyDictionary<SourceTerminal, Value> OutputValues { get; private set; } = new Dictionary<SourceTerminal, Value>();
    public ExecutionFrame? DebugFrame => _frame?.ActiveFrame;
    public Value? OutputValue(string node, string output = "result") => OutputValues.TryGetValue(new(node, output), out var value) ? value : output == "result" ? Values.GetValueOrDefault(node) : null;
    public Dictionary<string, Queue<double>> ChartHistory { get; } = [];
    public IReadOnlyList<Diagnostic> Diagnostics { get; private set; } = [];
    public long Revision { get; private set; }
    public bool Dirty { get; private set; }
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public bool IsRunning { get; private set; }
    public bool IsPaused { get; private set; }
    public bool Highlight { get; set; }
    public bool PanelEditMode { get; set; }
    public string? ActiveNode { get; private set; }
    public double LastMilliseconds { get; private set; }
    public int LastNodeCount { get; private set; }
    public long Frames => _runtime.Frames;
    public string Status { get; private set; } = "Ready";
    public event Action<SessionChange>? Changed;
    public InstrumentSession(LabProject project) { ProjectSerializer.Validate(project); Project = project; ActiveId = project.Instruments[0].Id; Validate(); }
    public Node? Find(string? id) => Diagram.Nodes.FirstOrDefault(n => n.Id == id);
    public Node? SelectedNode => Selection.Count == 1 ? Find(Selection.First()) : null;
    public void Notify(SessionChange change = SessionChange.View) => Changed?.Invoke(change);
    public void Message(string message) { Status = message; Notify(SessionChange.Execution); }
    public void MarkSaved() { Dirty = false; Notify(SessionChange.Document); }
    public void Select(string? node, bool additive = false)
    {
        if (!additive) Selection.Clear();
        if (node is not null) { if (additive && Selection.Contains(node)) Selection.Remove(node); else Selection.Add(node); }
        SelectedWire = null; Notify(SessionChange.Selection | SessionChange.View);
    }
    public void SelectWire(string? id) { Selection.Clear(); SelectedWire = id; Notify(SessionChange.Selection | SessionChange.View); }
    public void SelectAll() { Selection.Clear(); Selection.UnionWith(Diagram.Nodes.Select(n => n.Id)); SelectedWire = null; Notify(SessionChange.Selection | SessionChange.View); }
    public void Switch(string id)
    {
        if (!Project.Instruments.Any(v => v.Id == id)) return;
        Abort(); ActiveId = id; _path.Clear(); Selection.Clear(); SelectedWire = null; ResetExecution(); Validate(); Notify(SessionChange.All);
    }
    public void Enter(string id, bool alternative = false, int frameIndex = -1)
    {
        var n = Find(id);
        var level = new NavigationLevel(id, alternative, n is not null && StructureFrames.HasFrames(n) ? frameIndex < 0 ? n.VisibleFrame : frameIndex : -1);
        if (ResolveChild(n, level) is null) return;
        Abort(); _path.Add(level); Selection.Clear(); SelectedWire = null; ResetExecution(); Validate(); Notify(SessionChange.All);
    }
    public void Leave()
    {
        if (_path.Count == 0) return;
        Abort(); _path.RemoveAt(_path.Count - 1); Selection.Clear(); ResetExecution(); Validate(); Notify(SessionChange.All);
    }
    public void Replace(LabProject project)
    {
        ProjectSerializer.Validate(project); Abort(); Project = project; ActiveId = project.Instruments[0].Id; _path.Clear(); _undo.Clear(); _redo.Clear(); Selection.Clear(); SelectedWire = null; Dirty = false; Revision++; ResetExecution(); Validate(); Notify(SessionChange.All);
    }
    public void NewInstrument()
    {
        string? id = null; Edit(() => { var vi = Examples.Blank($"Untitled {Project.Instruments.Count + 1}.vi"); Project.Instruments.Add(vi); id = vi.Id; }); Switch(id!);
    }
    public void Edit(Action action, bool affectsCode = true)
    {
        var before = ProjectSerializer.Save(Project);
        if (affectsCode) Abort();
        try { action(); var after = ProjectSerializer.Save(Project); if (after == before) return; Push(_undo, before); _redo.Clear(); Touch(affectsCode); }
        catch { Restore(before); throw; }
    }
    private static void Push(List<string> history, string snapshot)
    {
        history.Add(snapshot); long length = history.Sum(x => (long)x.Length * 2);
        while (history.Count > 64 || length > 32 * 1024 * 1024) { length -= history[0].Length * 2L; history.RemoveAt(0); }
    }
    private void Touch(bool code)
    {
        Revision++; Dirty = true;
        if (code) { ResetExecution(); Validate(); }
        Notify(SessionChange.Document | SessionChange.View | SessionChange.Selection);
    }
    private void Restore(string snapshot)
    {
        Project = ProjectSerializer.Load(snapshot);
        if (!Project.Instruments.Any(x => x.Id == ActiveId)) { ActiveId = Project.Instruments[0].Id; _path.Clear(); }
        Selection.IntersectWith(Diagram.Nodes.Select(x => x.Id)); SelectedWire = null; ResetExecution(); Validate();
    }
    public void Undo()
    {
        if (!CanUndo) return; Abort(); Push(_redo, ProjectSerializer.Save(Project)); var snapshot = _undo[^1]; _undo.RemoveAt(_undo.Count - 1); Restore(snapshot); Touch(false);
    }
    public void Redo()
    {
        if (!CanRedo) return; Abort(); Push(_undo, ProjectSerializer.Save(Project)); var snapshot = _redo[^1]; _redo.RemoveAt(_redo.Count - 1); Restore(snapshot); Touch(false);
    }
    public void BeginGesture() { _gesture ??= ProjectSerializer.Save(Project); }
    public void EndGesture()
    {
        var before = _gesture; _gesture = null;
        if (before is null || before == ProjectSerializer.Save(Project)) return;
        Push(_undo, before); _redo.Clear(); Touch(false);
    }
    public void CancelGesture() { if (_gesture is null) return; var before = _gesture; _gesture = null; Restore(before); Notify(SessionChange.All); }
    public Node Add(string kind, double x, double y, string? widget = null, PointD? panelPosition = null)
    {
        Node? result = null;
        Edit(() =>
        {
            result = Examples.NewNode(kind, x, y); Diagram.Nodes.Add(result); var def = NodeCatalog.Get(kind);
            if (_path.Count == 0 && (def.IsControl || def.IsIndicator))
            {
                var visual = widget ?? (kind is "graph" or "chart" ? "Graph" : def.Output == ValueKind.Boolean ? (def.IsControl ? "Switch" : "LED") : def.Output == ValueKind.String ? "String" : def.Output == ValueKind.Array ? "Array" : def.Output == ValueKind.Error ? "Error" : def.Output == ValueKind.Complex ? "Complex" : "Numeric");
                Instrument.Panel.Add(new() { NodeId = result.Id, Widget = visual, Bounds = new(panelPosition?.X ?? 35 + Instrument.Panel.Count % 4 * 185, panelPosition?.Y ?? 70 + Instrument.Panel.Count / 4 * 155, visual is "Graph" or "Chart" ? 410 : visual == "Error" ? 300 : visual == "Complex" ? 240 : 160, visual is "Graph" or "Chart" ? 240 : visual == "Knob" ? 175 : visual == "Error" ? 160 : visual == "Complex" ? 110 : 95), Minimum = 0, Maximum = 100 });
            }
        }); Select(result!.Id); return result;
    }
    public void Connect(string from, string to, string input, string output = "result")
    {
        var a = Find(from) ?? throw new ArgumentException("Source node is missing."); var b = Find(to) ?? throw new ArgumentException("Target node is missing.");
        var source = NodeCatalog.Resolve(a).FindOutput(output) ?? throw new ArgumentException("Output terminal is missing."); var port = NodeCatalog.Resolve(b).Inputs.FirstOrDefault(p => p.Name == input) ?? throw new ArgumentException("Input terminal is missing.");
        if (source.Kind != port.Kind) throw new ArgumentException($"Wire type mismatch: {source.Kind} → {port.Kind}.");
        Edit(() =>
        {
            Diagram.Wires.RemoveAll(w => w.To == to && w.Input == input);
            Diagram.Wires.Add(new() { From = from, To = to, Input = input, Output = output });
            var error = GraphCompiler.Validate(Diagram).FirstOrDefault(e => e.Code is "CYCLE" or "TYPE" or "DRIVER");
            if (error is not null) throw new ArgumentException(error.Message);
        }); Message("Wire connected");
    }
    public void Delete()
    {
        Edit(() =>
        {
            Diagram.Wires.RemoveAll(w => w.Id == SelectedWire || Selection.Contains(w.From) || Selection.Contains(w.To));
            Diagram.Nodes.RemoveAll(n => Selection.Contains(n.Id));
            if (_path.Count == 0) Instrument.Panel.RemoveAll(p => Selection.Contains(p.NodeId));
            Selection.Clear(); SelectedWire = null;
        });
    }
    public void SetValue(string id, double value) { if (!double.IsFinite(value)) throw new ArgumentException("Enter a finite number."); Edit(() => { var n = Find(id); if (n is not null) n.Value = value; }, false); }
    public void SetText(string id, string text) => Edit(() => { var n = Find(id); if (n is not null) n.Text = text; }, Find(id)?.Kind is "input" or "output" or "sequence-read" or "sequence-write" or "formula");
    public void ToggleProbe() { var w = Diagram.Wires.FirstOrDefault(w => w.Id == SelectedWire); if (w is not null) Edit(() => w.Probe = !w.Probe, false); }
    public void ToggleBreakpoint() { var n = SelectedNode; if (n is not null) Edit(() => n.Breakpoint = !n.Breakpoint, false); }
    public void Copy()
    {
        var source = ProjectSerializer.Clone(new() { Name = "Clipboard", Instruments = [Instrument] }).Instruments[0];
        var graph = source.Diagram;
        foreach (var step in _path) { var node = graph.Nodes.First(x => x.Id == step.NodeId); graph = ResolveChild(node, step)!; }
        source.Diagram = graph; graph.Nodes.RemoveAll(n => !Selection.Contains(n.Id)); graph.Wires.RemoveAll(w => !Selection.Contains(w.From) || !Selection.Contains(w.To)); source.Panel.RemoveAll(p => !Selection.Contains(p.NodeId));
        if (_path.Count > 0) source.Panel.Clear();
        _clipboard = ProjectSerializer.Save(new() { Name = "Clipboard", Instruments = [source] }); Message($"Copied {graph.Nodes.Count} nodes");
    }
    public void Paste()
    {
        if (_clipboard is null) return;
        var clip = ProjectSerializer.Load(_clipboard).Instruments[0]; var ids = clip.Diagram.Nodes.ToDictionary(n => n.Id, _ => Guid.NewGuid().ToString("N"));
        Edit(() =>
        {
            Selection.Clear();
            foreach (var n in clip.Diagram.Nodes) { n.Id = ids[n.Id]; n.X += 30; n.Y += 30; Diagram.Nodes.Add(n); Selection.Add(n.Id); }
            foreach (var w in clip.Diagram.Wires) { w.Id = Guid.NewGuid().ToString("N"); w.From = ids[w.From]; w.To = ids[w.To]; Diagram.Wires.Add(w); }
            if (_path.Count == 0) foreach (var p in clip.Panel) { p.Id = Guid.NewGuid().ToString("N"); p.NodeId = ids[p.NodeId]; p.Bounds = p.Bounds with { X = p.Bounds.X + 30, Y = p.Bounds.Y + 30 }; Instrument.Panel.Add(p); }
        });
    }
    public void AutoLayout()
    {
        Edit(() =>
        {
            var nodes = Diagram.Nodes.ToDictionary(n => n.Id); var indegree = nodes.Keys.ToDictionary(x => x, _ => 0); var rank = nodes.Keys.ToDictionary(x => x, _ => 0);
            var edges = nodes.Keys.ToDictionary(x => x, _ => new List<string>());
            foreach (var w in Diagram.Wires) if (nodes.ContainsKey(w.From) && nodes.TryGetValue(w.To, out var target) && target.Kind != "feedback") { edges[w.From].Add(w.To); indegree[w.To]++; }
            var queue = new Queue<string>(nodes.Keys.Where(x => indegree[x] == 0));
            while (queue.TryDequeue(out var id)) foreach (var target in edges[id]) { rank[target] = Math.Max(rank[target], rank[id] + 1); if (--indegree[target] == 0) queue.Enqueue(target); }
            foreach (var group in Diagram.Nodes.GroupBy(n => rank[n.Id])) { var row = 0; foreach (var n in group) { n.X = 55 + group.Key * 360; n.Y = 65 + row++ * 230; } }
        }, false);
    }
    public void Validate() { _plan = null; Diagnostics = GraphCompiler.Validate(Instrument.Diagram); }
    private CompiledGraph Plan() => _plan ??= GraphCompiler.Compile(Instrument.Diagram);
    private void ResetExecution() { _plan = null; _frame = null; _runtime.Reset(); ChartHistory.Clear(); Values = new Dictionary<string, Value>(); OutputValues = new Dictionary<SourceTerminal, Value>(); ActiveNode = null; }
    public void Run(bool continuous = false)
    {
        _stepOutTarget = null; _continuous = continuous; IsPaused = false; IsRunning = true; Pump();
    }
    public void Tick() { if (IsRunning && !IsPaused) Pump(); }
    public void Pause() { if (!IsRunning && !IsPaused) return; IsPaused = !IsPaused; IsRunning = true; Message(IsPaused ? "Execution paused" : "Execution resumed"); }
    public void Abort() { IsRunning = false; IsPaused = false; _continuous = false; _stepOutTarget = null; _frame = null; _skipBreakpoint = null; ActiveNode = null; Status = "Ready"; Notify(SessionChange.Execution | SessionChange.View); }
    private void Pump()
    {
        try
        {
            _frame ??= _runtime.Start(Plan());
            var slice = System.Diagnostics.Stopwatch.StartNew(); var steps = 0;
            do
            {
                var active = _frame.ActiveFrame; var model = active.NextNode; var next = model is null ? null : active.Path + "/" + model.Id;
                if (model?.Breakpoint == true && !active.IsInsideStructure && _skipBreakpoint != next) { _skipBreakpoint = next; IsPaused = true; ActiveNode = model.Id; Status = $"Breakpoint: {model.Label}"; PublishFrame(); return; }
                _skipBreakpoint = null; _frame.StepInto(); steps++;
                if (_stepOutTarget?.Completed == true)
                {
                    _stepOutTarget = null;
                    if (!_frame.Completed) { IsRunning = false; IsPaused = true; PublishFrame(); Status = "Step out · paused in caller"; Notify(SessionChange.Execution | SessionChange.View); return; }
                }
                if (!_frame.Completed && (Highlight || steps >= 4096 || (steps >= 16 && slice.Elapsed.TotalMilliseconds >= 8))) break;
            } while (!_frame.Completed);
            PublishFrame();
            if (_frame.Completed) { CompleteFrame(); _frame = null; IsRunning = _continuous; Status = _continuous ? "Running continuously · SIMULATED" : "Execution complete"; }
            Notify(SessionChange.Execution | SessionChange.View);
        }
        catch (Exception error) { ExecutionError(error); }
    }
    public void Step()
    {
        try { _stepOutTarget = null; IsRunning = false; IsPaused = true; _frame ??= _runtime.Start(Plan()); _frame.Step(); PublishFrame(); if (_frame.Completed) { CompleteFrame(); _frame = null; IsPaused = false; } Status = IsPaused ? "Single step · paused" : "Execution complete"; Notify(SessionChange.Execution | SessionChange.View); }
        catch (Exception e) { ExecutionError(e); }
    }
    private void PublishFrame()
    {
        if (_frame is null) return; Values = _frame.Values; OutputValues = _frame.Outputs; ActiveNode = _frame.ActiveFrame.LastNodeId ?? _frame.LastNodeId; LastMilliseconds = _frame.ElapsedMilliseconds; LastNodeCount = _frame.EvaluatedNodes; Notify(SessionChange.Execution | SessionChange.View);
    }
    private void CompleteFrame()
    {
        foreach (var node in Instrument.Diagram.Nodes.Where(n => n.Kind == "chart"))
            if (Values.TryGetValue(node.Id, out var value))
            {
                if (!ChartHistory.TryGetValue(node.Id, out var history)) ChartHistory[node.Id] = history = new();
                foreach (var sample in value.Samples) { history.Enqueue(sample); if (history.Count > 4096) history.Dequeue(); }
            }
    }
    private void ExecutionError(Exception error)
    {
        IsRunning = false; IsPaused = false; _frame = null; ActiveNode = error is NodeExecutionException node ? node.NodeId : null;
        if (error is GraphValidationException validation) Diagnostics = validation.Diagnostics;
        Status = error.Message; Notify(SessionChange.Execution | SessionChange.View);
    }
}
