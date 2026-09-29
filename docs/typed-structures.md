# Typed structures and nested execution

LabSpace 0.2 adds explicit connector contracts to For, While, Boolean Case and embedded SubVI nodes. Contracts are editable in the studio and reusable from UI-independent C# libraries. This is a documented subset of graphical dataflow, not a parser or runtime for NI binary VIs.

## Executable example

Open **Indexed Accumulator.vi** and Run. Input samples `1,2,3,4,5` produce named `state = 15` and `totals = [1,3,6,10,15]`. The thumbnail is recorded from the actual body. Double-click to edit it.

Select the loop and open **Tunnels and shift registers**. Cancel does not alter the document/history. Apply synchronizes connector nodes in one transaction. Turning off Initialized removes that register's external initialization terminal and attached wire. Two subsequent runs return `15` and `30`; Undo restores initialization and wiring.

Removing or renaming terminals removes attached wires. A type change can leave a wire invalid; the compiler reports it rather than silently coercing. New output connectors remain unwired until connected unless you explicitly permit their default.

## Execute without Uno

Reference `LabSpace.Documents` and `LabSpace.Dataflow`:

```csharp
using LabSpace.Dataflow;
using LabSpace.Documents;

var vi = StructuredExamples.IndexedAccumulator();
var loop = vi.Diagram.Nodes.Single(node => node.Kind == "for");
var graph = GraphCompiler.Compile(vi.Diagram);
var runtime = new DataflowRuntime();

using var cancellation = new CancellationTokenSource();
var frame = runtime.Run(graph, cancellation.Token, maximumNodes: 100_000);
Console.WriteLine(frame.GetOutput(loop.Id, "state").Number); // 15
Console.WriteLine(string.Join(", ", frame.GetOutput(loop.Id, "totals").Samples));
// 1, 3, 6, 10, 15
```

`ExecutionFrame.Values[nodeId]` is the primary-output convenience API. `GetOutput(nodeId, name)` and `Outputs[SourceTerminal]` expose specific outputs. A legacy `Output = "result"` wire resolves the first output only when there is no explicitly named `result` terminal. New editor wiring records the selected output name.

## Define the interface

The example declares this immutable contract:

```csharp
using LabSpace.Core;

var contract = new StructureContract
{
    Inputs =
    [
        new InputTunnel
        {
            Name = "sample",
            Type = ValueKind.Number,
            Indexing = true
        }
    ],
    Outputs =
    [
        new OutputTunnel
        {
            Name = "totals",
            Type = ValueKind.Number,
            Mode = TunnelMode.Indexing
        }
    ],
    Registers =
    [
        new ShiftRegister
        {
            Name = "state",
            Type = ValueKind.Number,
            Initialized = true,
            HistoryDepth = 1
        }
    ],
    PrimaryOutput = "state"
};
```

`InputTunnel.Type` describes the value inside the body. Numeric indexing changes its external type to Array. A typed Connector Input named `sample` reads each element. A register exposes external `initial:state`, body input `state`, body output `state` and final external output `state`.

Use `InstrumentSession.ConfigureStructure(nodeId, contract)` for an undoable edit that synchronizes connectors. Direct model construction is supported but requires a valid body. Use `NodeCatalog.Resolve(node)` for actual instance terminals; `NodeCatalog.Get(kind)` returns the general palette definition. Contract replacement invalidates the weak identity cache.

## Collection rules

| Mode | Body value | External value | Behavior |
| --- | --- | --- | --- |
| LastValue | Any supported type | Same type | Last iteration; type default at zero iterations |
| Indexing | Number | Numeric array | One value per iteration |
| ConditionalIndexing | Number plus a named Boolean connector | Numeric array | Includes values only when that connector is true |
| Concatenating | Numeric array | Numeric array | Appends arrays in iteration order |

Collections are bounded to 65,536 values. Indexed collections do not yet handle arbitrary element/rank types. Ordinary tunnels/registers carry finite doubles, Booleans, Unicode strings, numeric 1D arrays and waveforms. A conditional collection's `Condition` names a Boolean body output; its default name is `include`.

## Count and termination

For uses the minimum of the explicit integer count and every indexed input length. With indexed inputs and no count override, the count defaults to the safety bound 10,000; without indexed inputs it defaults to 10. An empty indexed array produces zero iterations. Fractional, negative and excessive counts are rejected.

While auto-indexing does not cap iteration count. Beyond an input array it supplies numeric zero. Normal While stops after an iteration whose condition is true; ContinueWhenTrue inverts that rule. ConditionalFor combines its count/index bound with a condition terminal. The terminating iteration contributes its results. While reports an error after 10,000 iterations without termination.

At zero For iterations, initialized registers return initialization values; uninitialized registers return prior committed state or type defaults. Indexed/concatenated collections are empty and last-value output tunnels return their type default. No body invocation is fabricated to produce outputs.

## Stacked history and persistence

`HistoryDepth = 3` creates body inputs `state`, `state:1` and `state:2`. The first is the newest value. After each iteration, history shifts and the body's `state` output becomes the new first entry. Initialized histories expose corresponding optional outside inputs `initial:state`, `initial:state:1` and `initial:state:2`. Unwired initialization uses type defaults; numeric/Boolean defaults can be set in node parameters.

Initialized histories are recreated for every invocation. Uninitialized histories are scoped to the runtime's invocation path, including a selected Case branch. Pending history and feedback updates commit only when the root frame succeeds. Failed/aborted frames do not commit those dictionaries. This guarantee does not cover arbitrary side effects or random-generator advancement.

`DataflowRuntime.Reset()` clears persistent state and logical time. The editor resets execution on project/VI/body navigation, code-affecting changes and Undo/Redo. Geometry-only edits do not reset the running frame. Runtime/frame/session instances are caller-owned and not concurrent reentrant runtimes.

## Case and SubVI

Boolean Case has a required selector and two embedded diagrams. Only the selected branch executes. Both branches must match declared outputs unless a tunnel explicitly allows an unwired default. TRUE/FALSE editor buttons select what you inspect, not the execution branch.

Embedded SubVI runs one body invocation with named typed arguments/results. It is not a linked NI VI, dependency resolver, recursive/reentrant call system or a complete NI connector-pane pattern editor. Legacy numeric For/While/Case/SubVI bodies remain supported and migrate on Apply.

## Cooperative execution and debugging

`Step()` retains top-level step-over behavior. `StepInto()` performs one resumable activation transition, which may enter/return from a structure without evaluating a primitive. `ActiveFrame` exposes the deepest active invocation's path, next node, graph and values.

The workbench targets a 40 ms timer, yielding after 4,096 transitions or roughly 8 ms checked after the first 16 transitions. Highlight yields after every transition. Individual kernels run to completion, so this is neither preemptive nor hard real time. Cancellation is checked even in empty bodies, and one root node budget is shared by descendants.

F10 steps over; F11 steps into. The context-help pane shows active values without changing editor navigation. Set a nested breakpoint, return to the caller and Run to stop at it. Abort discards the activation. Step-out, conditional breakpoints and NI profiling equivalence are not implemented.

## Serialization, limits and boundaries

Source-generated JSON stores contracts, connector types and named outputs. LabSpace 0.3 reads format versions 1, 2 and 3 and migrates older projects to version 3. Earlier releases cannot read version-3 files; retain originals when testing migration. Unknown kinds/types are rejected. Known but unwired diagrams remain editable and are marked broken for execution.

Contracts allow at most 32 input tunnels, 32 outputs, 16 registers and 16 history entries per register. Graphs are limited to 12 nested levels and 100,000 evaluated nodes per root frame. Full G types/coercions, general/n-dimensional arrays, event/sequence structures, linked VIs, drivers, FPGA and hard-real-time execution remain outside this increment. See [compatibility](compatibility.md).
