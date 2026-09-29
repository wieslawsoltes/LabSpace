using System.Collections.ObjectModel;
using LabSpace.Core;
using LabSpace.Dataflow;

namespace LabSpace.Editing;

/// <summary>Wire identity includes VI and activation path; node IDs alone are only diagram-local.</summary>
public readonly record struct DebugAddress(string InstrumentId, string Path, string ObjectId);
public sealed record ProbeSnapshot(DebugAddress Address, string Instrument, string Diagram, string Source,
    string Target, ValueKind Type, Value? Value, bool Live, bool Retained);

public sealed partial class InstrumentSession
{
    private readonly record struct FrameAddress(string InstrumentId, string Path);
    private sealed record RetainedFrame(IReadOnlyDictionary<string, Value> Values,
        IReadOnlyDictionary<SourceTerminal, Value> Outputs, long Bytes, long Sequence);
    private sealed record DiagramLocation(VirtualInstrument Instrument, Diagram Diagram, string Path,
        string Caption, IReadOnlyList<NavigationLevel> Navigation);
    private readonly Dictionary<DebugAddress, bool> _probeOverrides = [];
    private readonly Dictionary<DebugAddress, bool> _breakpointOverrides = [];
    private readonly Dictionary<FrameAddress, RetainedFrame> _retained = [];
    private long _captureSequence;
    private const long MaximumRetainedBytes = 16 * 1024 * 1024;
    private const int MaximumRetainedFrames = 128;
    private const int MaximumProbeCount = 512;
    public bool DebuggingEnabled { get; private set; } = true;
    public bool RetainWireValues { get; private set; } = true;
    public long DebugRevision { get; private set; }
    public long RetainedDebugBytes { get; private set; }
    public int RetainedDebugFrames => _retained.Count;

    public string VisibleDebugPath
    {
        get
        {
            var graph = Instrument.Diagram; var path = "root";
            foreach (var level in _path)
            {
                var owner = graph.Nodes.FirstOrDefault(n => n.Id == level.NodeId);
                if (owner is null || ResolveChild(owner, level) is not { } child) return "invalid";
                path += ChildSuffix(owner, level); graph = child;
            }
            return path;
        }
    }
    private static string ChildSuffix(Node owner, NavigationLevel level) => "/" + owner.Id + (level.FrameIndex >= 0
        ? (owner.Kind == "sequence" ? "/frame:" : "/case:") + owner.Frames[level.FrameIndex].Id
        : owner.Kind == "case" ? level.Alternative ? "/false" : "/true" : "");
    private ExecutionFrame? LiveFrameAt(string path)
    {
        for (var frame = _frame; frame is not null; frame = frame.ChildFrame)
            if (frame.Path == path) return frame;
        return null;
    }
    private static Value? Lookup(IReadOnlyDictionary<string, Value> values,
        IReadOnlyDictionary<SourceTerminal, Value> outputs, string node, string output) =>
        outputs.TryGetValue(new(node, output), out var value) ? value : output == "result" ? values.GetValueOrDefault(node) : null;
    private bool BreakpointAt(string vi, string path, Node node) =>
        _breakpointOverrides.GetValueOrDefault(new(vi, path, node.Id), node.Breakpoint);
    public bool IsBreakpoint(Node node) => BreakpointAt(ActiveId, VisibleDebugPath, node);
    public bool IsProbe(Wire wire) => _probeOverrides.GetValueOrDefault(new(ActiveId, VisibleDebugPath, wire.Id), wire.Probe);
    public void ToggleBreakpoint(string nodeId)
    {
        var node = Find(nodeId) ?? throw new ArgumentException("Node not found.", nameof(nodeId));
        _breakpointOverrides[new(ActiveId, VisibleDebugPath, nodeId)] = !IsBreakpoint(node);
        DebugChanged();
    }
    public void ToggleProbe(string wireId)
    {
        var wire = Diagram.Wires.FirstOrDefault(w => w.Id == wireId) ?? throw new ArgumentException("Wire not found.", nameof(wireId));
        if (!IsProbe(wire) && GetProbes().Count >= MaximumProbeCount)
            throw new InvalidOperationException("The debugger supports at most 512 probes. Remove a probe first.");
        _probeOverrides[new(ActiveId, VisibleDebugPath, wireId)] = !IsProbe(wire);
        DebugChanged();
    }
    public void RemoveProbe(DebugAddress address) { _probeOverrides[address] = false; DebugChanged(); }
    public void SetDebuggingEnabled(bool enabled)
    {
        if (DebuggingEnabled == enabled) return;
        DebuggingEnabled = enabled; _skipBreakpoint = null; DebugChanged();
    }
    public void SetRetainWireValues(bool retain)
    {
        if (RetainWireValues == retain) return;
        RetainWireValues = retain;
        if (!retain) ClearDebugValues();
        DebugChanged();
    }
    public void ClearDebugValues() => ClearDebugValues(null);
    private void ClearDebugValues(string? instrument)
    {
        foreach (var key in _retained.Keys.Where(k => instrument is null || k.InstrumentId == instrument).ToArray())
        { RetainedDebugBytes -= _retained[key].Bytes; _retained.Remove(key); }
        DebugRevision++;
    }
    public void ClearDebuggerValues() { ClearDebugValues(); DebugChanged(); }
    private void ClearDebugger()
    {
        _probeOverrides.Clear(); _breakpointOverrides.Clear(); ClearDebugValues();
    }
    private void DebugChanged() { DebugRevision++; Notify(SessionChange.Debug | SessionChange.View); }
    private void CaptureDebugFrame(ExecutionFrame frame)
    {
        if (!DebuggingEnabled || !RetainWireValues) return;
        var key = new FrameAddress(ActiveId, frame.Path);
        if (_retained.Remove(key, out var prior)) RetainedDebugBytes -= prior.Bytes;
        // Conservative retained-size accounting. Shared immutable payloads count once per frame.
        var seen = new HashSet<Value>(ReferenceEqualityComparer.Instance);
        long bytes = 256 + frame.Values.Count * 96L + frame.Outputs.Count * 128L;
        foreach (var value in frame.Values.Values.Concat(frame.Outputs.Values))
            if (seen.Add(value)) bytes += 160 + value.Samples.Length * 8L + value.Text.Length * 2L + value.Error.Source.Length * 2L;
        if (bytes <= MaximumRetainedBytes)
        {
            _retained[key] = new(new ReadOnlyDictionary<string, Value>(frame.Values),
                new ReadOnlyDictionary<SourceTerminal, Value>(frame.Outputs), bytes, ++_captureSequence);
            RetainedDebugBytes += bytes;
        }
        while (_retained.Count > MaximumRetainedFrames || RetainedDebugBytes > MaximumRetainedBytes)
        {
            var oldest = _retained.MinBy(p => p.Value.Sequence);
            RetainedDebugBytes -= oldest.Value.Bytes; _retained.Remove(oldest.Key);
        }
        DebugRevision++;
    }
    private void RestoreRetainedRoot()
    {
        if (_retained.TryGetValue(new(ActiveId, "root"), out var frame))
        { Values = frame.Values; OutputValues = frame.Outputs; }
    }
    private IEnumerable<DiagramLocation> DebugLocations()
    {
        foreach (var vi in Project.Instruments)
            foreach (var location in Visit(vi, vi.Diagram, "root", "Block Diagram", [], 0)) yield return location;
        static IEnumerable<DiagramLocation> Visit(VirtualInstrument vi, Diagram graph, string path, string caption,
            IReadOnlyList<NavigationLevel> navigation, int depth)
        {
            yield return new(vi, graph, path, caption, navigation);
            if (depth >= 12) yield break;
            foreach (var node in graph.Nodes)
            {
                var levels = new List<NavigationLevel>();
                if (StructureFrames.HasFrames(node))
                    for (var i = 0; i < node.Frames.Count; i++) levels.Add(new(node.Id, false, i));
                else
                {
                    if (node.Body is not null) levels.Add(new(node.Id, false));
                    if (node.Alternative is not null) levels.Add(new(node.Id, true));
                }
                foreach (var level in levels)
                {
                    var child = ResolveChild(node, level)!;
                    var label = node.Label + (level.FrameIndex >= 0 ? " [" + node.Frames[level.FrameIndex].Label + "]" : level.Alternative ? " [False]" : "");
                    foreach (var found in Visit(vi, child, path + ChildSuffix(node, level), caption + " / " + label,
                        navigation.Concat([level]).ToArray(), depth + 1)) yield return found;
                }
            }
        }
    }
    /// <summary>Enumerates all loaded VIs, including hidden Case/Sequence diagrams. It never executes code.</summary>
    public IReadOnlyList<ProbeSnapshot> GetProbes()
    {
        var result = new List<ProbeSnapshot>();
        foreach (var location in DebugLocations())
        {
            var nodes = location.Diagram.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
            var live = location.Instrument.Id == ActiveId ? LiveFrameAt(location.Path) : null;
            _retained.TryGetValue(new(location.Instrument.Id, location.Path), out var retained);
            foreach (var wire in location.Diagram.Wires)
            {
                var address = new DebugAddress(location.Instrument.Id, location.Path, wire.Id);
                if (!_probeOverrides.GetValueOrDefault(address, wire.Probe) || !nodes.TryGetValue(wire.From, out var from) || !nodes.TryGetValue(wire.To, out var to)) continue;
                var port = NodeCatalog.Resolve(from).FindOutput(wire.Output);
                if (port is null) continue;
                var value = live is not null ? Lookup(live.Values, live.Outputs, wire.From, wire.Output) : null;
                var isLive = value is not null;
                value ??= retained is not null ? Lookup(retained.Values, retained.Outputs, wire.From, wire.Output) : null;
                result.Add(new(address, location.Instrument.Name, location.Caption, from.Label + "." + port.Name,
                    to.Label + "." + wire.Input, port.Kind, value, isLive, !isLive && value is not null));
                if (result.Count == MaximumProbeCount) return result;
            }
        }
        return result;
    }
    /// <summary>Reveals a probe's model without disturbing a paused execution in the same VI.</summary>
    public void LocateProbe(DebugAddress address)
    {
        var location = DebugLocations().FirstOrDefault(l => l.Instrument.Id == address.InstrumentId && l.Path == address.Path
            && l.Diagram.Wires.Any(w => w.Id == address.ObjectId));
        if (location is null) { Message("The probed wire no longer exists."); return; }
        if (ActiveId != address.InstrumentId) Switch(address.InstrumentId);
        _path.Clear(); _path.AddRange(location.Navigation); Selection.Clear(); SelectedWire = address.ObjectId;
        Notify(SessionChange.Navigation | SessionChange.Selection | SessionChange.View);
    }
}
