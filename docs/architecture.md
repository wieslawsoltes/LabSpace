# Architecture and performance

LabSpace separates document models, signal kernels, execution, editing, rendering, controls and hosting. No engine requires a XAML visual tree. Desktop and browser use the same C# implementation; JavaScript is limited to browser file/recovery interop and opt-in read-only diagnostics.

## Dependency direction

```text
Core ── Signals
  ├── Documents
  └── Dataflow ── Editing ── Skia ── Controls ── Workbench ── App
Storage ───────────────────────────────────────┘
```

`LabSpace.Core` defines mutable authoring models (`LabProject`, `VirtualInstrument`, `Diagram`, `Node`, `Wire`, `PanelItem`), immutable runtime `Value` payloads and immutable `StructureContract` records. Diagram node identity also links a front-panel item. Each wire names a source output and a destination input. Runtime arrays are immutable; factories bound and copy caller data so downstream consumers cannot corrupt another branch.

The nine library projects are independently packable. The App project supplies the platform host, font assets and storage implementation. The UI projects target `net10.0-desktop` and `net10.0-browserwasm`; the engines use `net10.0`.

## Definitions and compilation

`NodeCatalog.Get(kind)` returns a general palette definition. `NodeCatalog.Resolve(node)` returns the instance's actual terminals, including typed connector nodes and immutable structure contracts. A weak cache keys resolution by model, kind, data type and contract identity. Replacing a contract invalidates its resolved definition without polling mutable lists.

`GraphCompiler` validates unique node/wire identities, named output/input existence, exact supported-type compatibility, one input driver, required inputs, parameter finiteness, combinational cycles, nesting, body connectors and loop conditions. Optional unwired outputs must be explicitly declared. Invalid diagrams remain editable but do not execute. Compiled sources are `SourceTerminal(nodeId, outputName)` values rather than only node IDs.

A topological plan is built once per code-affecting revision. An explicit Feedback node breaks a cycle. Its prior value is scoped to an invocation path: root feedback observes the previous completed frame; repeated nested invocations can observe a staged prior-iteration value. Mutable model parameters are read at execution time, while connector/type changes invalidate compilation.

## Resumable execution

`DataflowRuntime` owns logical time, deterministic random generation and committed feedback/register state. `ExecutionFrame` owns one compiled activation, typed arguments, primary values, named outputs and a cursor. `StructureActivation` owns the selected embedded body, iteration cursor, typed input values, register histories and numeric output builders.

`Step()` preserves top-level step-over behavior. `StepInto()` advances one resumable transition; entering/returning from a structure can be a transition without evaluating another function. `ActiveFrame` identifies the deepest activation for a debugger. `Values[nodeId]` is a primary-result convenience; `GetOutput(nodeId, outputName)` and `Outputs` expose named terminals.

For loops combine an explicit count with indexed input lengths. While loops follow their condition and supply zero after indexed numeric inputs end. Output modes include last value, numeric indexing, conditional numeric indexing and numeric-array concatenation. Initialized registers recreate histories for each invocation; uninitialized registers retain successful state by call-site path. Zero iterations preserve register initialization/prior values and return empty collections.

Each root frame shares one node budget and cancellation token through all descendants. Cancellation is checked on every transition, including empty-body loops. Pending feedback/register dictionaries commit only after the root succeeds. Failure/abort cannot commit half a register transaction. This does not roll back arbitrary side effects or random-generator advancement. The runtime/session are caller-owned, not concurrent reentrant instances.

## Editing and debugging session

`InstrumentSession` is the UI-independent command boundary. An ordinary edit validates a before/after snapshot, rolls back on error, and records one undo unit. History is limited to 64 snapshots and 32 MiB per direction. Pointer gestures use one before snapshot and preview geometry until release; cancellation restores it. Geometry-only edits do not recompile the graph.

`ConfigureStructure` applies a new immutable contract transactionally. It migrates legacy numeric connectors, synchronizes typed body nodes, and removes wires attached to deleted terminals. The staged UI editor does not modify model objects before Apply. Cancel is therefore not an edit. Undo restores removed wires and metadata together.

The session exposes named-output probes and the active nested frame. Breakpoints compare the next node and invocation path before execution. F10 steps over, F11 steps into; editing navigation remains independent of execution navigation. VI/body navigation, Undo/Redo, project replacement and code-affecting edits reset execution state.

The workbench's target timer interval is 40 ms. Pump yields after 4,096 transitions or roughly 8 ms checked after the first 16 transitions. Highlight yields after each transition. An individual primitive still runs to completion: this is cooperative responsiveness, not preemption or hard real time. File dialogs pause execution pumping while open. Recovery writes are serialized and throttled separately.

## Rendering

Both editor surfaces use Uno's `SKCanvasElement`, contributing Skia drawing commands to the host compositor. Input and drawing use device-independent units with one shared pan/zoom transform. Zoom preserves the world coordinate under the pointer; wheel, middle-button pan and Fit use that transform.

`DiagramRenderer` caches wire paths by endpoint/port/geometry signature. Drawing and hit-testing use the same geometry. Only visible nodes, wires and grid work are rasterized. Scalar wires use a thinner stroke than arrays/waveforms. Compact terminals, control labels on the left, indicator labels on the right, named structure outputs and triangular register markers improve the classic diagram appearance.

Structure previews are actual body diagrams recorded into `SKPicture`, not placeholder graphics. Preview capture is bounded to 256 nodes/1,024 wires and cached by document revision and diagram identity. Continuous playback does not rebuild unchanged previews. A nested structure inside a preview is represented by its glyph, not an unbounded recursive rendering.

The panel renderer reuses paint/typeface resources, culls offscreen items, bounds chart history, and uses extrema-preserving waveform decimation. This retains narrow peaks rather than selecting every Nth sample. Rendering still scans models for some signatures/selection operations; it is not claimed to be a million-node editor or fully incremental spatial index.

Skia acceleration/fallback depends on the Uno host. Dataflow/DSP are managed CPU kernels, not WebGPU compute. Headless browser SwiftShader validation is not a physical-GPU benchmark. A Skia 4.x managed-only upgrade is unsafe with the pinned Uno native assets; the resolved ABI audit requires matched SkiaSharp 3.119.4 packages.

## Custom controls and platform services

`FrontPanelSurface`, `DiagramSurface`, `CanvasViewport`, `LabButton`, `LabPane`, `FunctionPalette`, `PropertyInspector`, `QuickDropControl`, `StructureContractEditor` and `InstrumentWorkbench` are reusable Uno components. Quick Drop requests cursor placement rather than silently inserting immediately. The connector editor stages an immutable draft. Native text, ComboBox/CheckBox, picker and dialog primitives retain platform input behavior; they are not all independently reimplemented low-level controls.

Canvas accessibility remains a separate parity boundary: richer per-node/control automation peers are needed. The keyboard shortcut fallback observes handled routed Ctrl+Space events so buttons cannot consume Quick Drop as ordinary Space activation. Ordinary text-field undo and clipboard behavior remain intact.

## Documents and limits

Source-generated JSON accepts format 1 or 2 and migrates loaded version-1 projects to version 2. Format 2 stores contracts, connector types and named outputs. Old LabSpace 0.1 cannot load these files. Import does not evaluate code or install plugins. Unsupported kinds/types are rejected rather than approximated.

| Resource | Bound |
| --- | --- |
| Project JSON | 8 MiB |
| VIs / project | 64 |
| Nodes / diagram | 4,096 |
| Wires / diagram | 16,384 |
| Nodes / project | 16,384 |
| Nested diagram levels | 12 |
| Loop iterations | 10,000 |
| Evaluated nodes / root frame | 100,000 |
| Samples / array or waveform | 65,536 |
| Input / output tunnels | 32 / 32 |
| Registers / structure | 16 |
| History depth / register | 16 |
| Chart history samples | 4,096 |
| Undo or redo snapshots | 64 and 32 MiB |

Browser recovery is per-origin IndexedDB. Native recovery is stored in application data. Unreadable recovery is preserved rather than automatically replaced by a fresh example. Recovery is not a durable substitute for explicitly saved files.

## Validation

Engine tests cover calculations, compilation, migration, transactions, named outputs, indexed/conditional collections, register histories, zero-iteration behavior, rollback and cooperative cancellation. Browser acceptance uses real keyboard/pointer events and reads opt-in state/hit-target snapshots. Tests do not use those snapshots to mutate the model.

Three-OS desktop builds, browser acceptance and source-provenance checks gate Pages. The same interactions run against the deployed application. Release packaging audits all nine libraries and matching native Skia assets. `tools/LabSpace.Benchmarks` records engine/FFT timings and allocation counts with machine/runtime metadata; those measurements do not represent GPU draw time, UI frame time or other machines.

Full G types, linked/reentrant VIs, driver systems, native compilation and FPGA/real-time execution remain distinct future implementations, not capabilities implied by these abstractions. See [compatibility](compatibility.md).


## Advanced execution (0.3)

`CaseDispatchTable` compiles bounded label sets into immutable dispatch patterns with overlap/default validation. `CompiledNode.Frames` holds each independently validated child plan. Sequence compilation threads a typed local-symbol environment from writer frames into later reader frames; output ownership is unique across frames. `StructureActivation` creates child frames with stable frame-ID invocation paths, propagates locals after child completion and delays external sequence outputs until all children finish.

`FormulaProgram` compiles a bounded scalar subset into forward-only bytecode. It tracks definitely assigned variables, checks names/arity/depth/token/instruction budgets, and implements short-circuit and ternary operators with forward branches. Programs are retained in graph plans; evaluation has isolated scalar slots and validates finite outputs. There is no native-code evaluation or ambient I/O.

`ErrorCluster` and complex values extend immutable runtime values, defaults, named output kernels, wiring validation and rendering. Formats 1/2 migrate to 3. Staged editors own deep drafts; signatures/labels are validated before edits, and undo snapshots restore diagrams, connectors and external wires atomically. Frame visibility affects preview caching, never runtime dispatch. Root compilation remains authoritative when editing nested frames.
