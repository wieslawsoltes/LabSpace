using System.Text;
using System.Text.Json;
using LabSpace.Core;
using LabSpace.Skia;
using LabSpace.Workbench;
using Microsoft.UI.Xaml;
using Windows.Foundation;

namespace LabSpace.App;

/// <summary>Opt-in read-only state and hit-target snapshots. Browser tests still use actual pointer and keyboard input.</summary>
internal sealed class BrowserDiagnostics : IDisposable
{
    private readonly InstrumentWorkbench _workbench;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    public BrowserDiagnostics(InstrumentWorkbench workbench) { _workbench = workbench; _timer.Tick += (_, _) => Publish(); _timer.Start(); }
    private void Publish()
    {
        var session = _workbench.Session;
        using var memory = new MemoryStream();
        using (var json = new Utf8JsonWriter(memory))
        {
            json.WriteStartObject(); json.WriteBoolean("ready", _workbench.ActualWidth > 100); json.WriteString("view", _workbench.View.ToString()); json.WriteString("instrument", session.Instrument.Name); json.WriteString("activeId", session.ActiveId); json.WriteNumber("revision", session.Revision); json.WriteNumber("frames", session.Frames); json.WriteNumber("errors", session.Diagnostics.Count); json.WriteBoolean("running", session.IsRunning); json.WriteBoolean("paused", session.IsPaused); json.WriteBoolean("dirty", session.Dirty); json.WriteBoolean("canUndo", session.CanUndo); json.WriteString("status", session.Status); json.WriteNumber("width", _workbench.ActualWidth); json.WriteNumber("height", _workbench.ActualHeight); json.WriteNumber("depth", session.Path.Count);
            json.WriteString("overlay", _workbench.OverlayMode); json.WriteString("placement", _workbench.BlockDiagram.PlacementKind ?? _workbench.FrontPanel.PlacementKind);
            json.WriteNumber("debugDepth", session.DebugFrame?.Path.Count(c => c == '/') ?? 0);
            json.WriteNumber("functions", NodeCatalog.All.Count);
            json.WriteStartObject("overlayFields"); foreach (var (name, element) in _workbench.OverlayFields) Bounds(json, name, _workbench.ElementBounds(element)); json.WriteEndObject();
            json.WriteStartObject("commands"); foreach (var (name, element) in _workbench.Commands) Bounds(json, name, _workbench.ElementBounds(element)); json.WriteEndObject();
            json.WriteStartObject("palette"); foreach (var (name, element) in _workbench.Palette.Entries) Bounds(json, name, _workbench.ElementBounds(element)); json.WriteEndObject();
            json.WriteStartObject("instruments"); foreach (var (id, element) in _workbench.InstrumentButtons) Bounds(json, id, _workbench.ElementBounds(element)); json.WriteEndObject();
            var surface = _workbench.BlockDiagram; var origin = _workbench.ElementBounds(surface); var front = _workbench.FrontPanel; var frontOrigin = _workbench.ElementBounds(front);
            Bounds(json, "diagramBounds", origin); Bounds(json, "panelBounds", frontOrigin);
            json.WriteStartArray("nodes");
            foreach (var node in session.Diagram.Nodes)
            {
                var rect = DiagramGeometry.Bounds(node); var p = surface.ToScreen(new(node.X, node.Y)); var output = DiagramGeometry.Output(node); var outputPoint = surface.ToScreen(new(output.X, output.Y));
                json.WriteStartObject(); json.WriteString("id", node.Id); json.WriteString("kind", node.Kind); json.WriteString("label", node.Label); json.WriteNumber("value", node.Value); json.WriteNumber("modelX", node.X); json.WriteNumber("modelY", node.Y); json.WriteBoolean("selected", session.Selection.Contains(node.Id));
                Bounds(json, "bounds", new(origin.X + p.X, origin.Y + p.Y, rect.Width * surface.Zoom, rect.Height * surface.Zoom)); Point(json, "output", origin.X + outputPoint.X, origin.Y + outputPoint.Y);
                json.WriteStartObject("inputs"); if (NodeCatalog.TryGet(node.Kind, out _)) for (var i = 0; i < NodeCatalog.Resolve(node).Inputs.Length; i++) { var q = DiagramGeometry.Input(node, i); var screen = surface.ToScreen(new(q.X, q.Y)); Point(json, NodeCatalog.Resolve(node).Inputs[i].Name, origin.X + screen.X, origin.Y + screen.Y); } json.WriteEndObject();
                var definition = NodeCatalog.Resolve(node);
                json.WriteStartObject("outputs");
                for (var i = 0; i < definition.Outputs.Length; i++) { var q = DiagramGeometry.Output(node, i); var screen = surface.ToScreen(new(q.X, q.Y)); Point(json, definition.Outputs[i].Name, origin.X + screen.X, origin.Y + screen.Y); }
                json.WriteEndObject(); json.WriteBoolean("typed", node.Contract is not null);
                if (node.Contract is { Registers.Length: > 0 } contract) json.WriteBoolean("registerInitialized", contract.Registers[0].Initialized);
                if (session.Values.TryGetValue(node.Id, out var result)) { json.WriteString("result", result.ToString()); if (result.Kind == ValueKind.Number) json.WriteNumber("number", result.Number); json.WriteNumber("samples", result.Samples.Length); json.WriteStartArray("sampleValues"); foreach (var value in result.Samples.Take(16)) json.WriteNumberValue(value); json.WriteEndArray(); }
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteStartArray("panel"); foreach (var item in session.Instrument.Panel) { var p = front.ToScreen(new(item.Bounds.X, item.Bounds.Y)); json.WriteStartObject(); json.WriteString("id", item.Id); json.WriteString("nodeId", item.NodeId); json.WriteString("widget", item.Widget); Bounds(json, "bounds", new(frontOrigin.X + p.X, frontOrigin.Y + p.Y, item.Bounds.Width * front.Zoom, item.Bounds.Height * front.Zoom)); json.WriteEndObject(); } json.WriteEndArray();
            json.WriteStartArray("wires"); foreach (var wire in session.Diagram.Wires) { json.WriteStartObject(); json.WriteString("id", wire.Id); json.WriteString("from", wire.From); json.WriteString("to", wire.To); json.WriteString("input", wire.Input); json.WriteString("output", wire.Output); json.WriteEndObject(); } json.WriteEndArray();
            json.WriteEndObject();
        }
        BrowserFiles.PublishDiagnostics(Encoding.UTF8.GetString(memory.ToArray()));
    }
    private static void Point(Utf8JsonWriter json, string name, double x, double y) { json.WriteStartObject(name); json.WriteNumber("x", x); json.WriteNumber("y", y); json.WriteEndObject(); }
    private static void Bounds(Utf8JsonWriter json, string name, Rect r) { json.WriteStartObject(name); json.WriteNumber("x", r.X); json.WriteNumber("y", r.Y); json.WriteNumber("width", r.Width); json.WriteNumber("height", r.Height); json.WriteEndObject(); }
    public void Dispose() => _timer.Stop();
}
