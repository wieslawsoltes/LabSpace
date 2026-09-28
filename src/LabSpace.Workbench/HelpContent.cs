namespace LabSpace.Workbench;

public static class HelpContent
{
    public const string UserGuide = """
        YOUR FIRST VIRTUAL INSTRUMENT

        LabSpace opens Signal Analysis.vi, a working simulated acquisition. The front panel is the instrument interface; the block diagram is its executable program. The example runs once at startup. No physical hardware is connected.

        1  OPERATE THE FRONT PANEL
        Drag the Amplitude knob vertically or horizontally. Click the Frequency numeric field to enter a value. Run again to update the graphs, RMS gauge and alarm. Run Continuously repeats the graph at a target interval of 40 ms; it is not a real-time scheduling guarantee. Click a plot to place a value cursor. Double-click a control to edit its value.

        2  EDIT THE PROGRAM
        Choose Block Diagram or press Ctrl+E. Drag a function from its body to reposition it. Choose a function from the right-hand palette to add it. Click an output terminal, then a compatible input terminal; dragging between terminals also works. Output terminals are filled squares on the right, inputs are hollow squares on the left. Numeric wires are orange; Boolean green; string pink; waveform brown. Each input has one driver; outputs can fan out to multiple inputs.

        3  ARRANGE THE PANEL
        Select Edit Front Panel in the toolbar. Move controls by dragging their bodies. Drag the lower-right corner of a selected control to resize. Choose Properties to change labels, ranges, dimensions and widget style. New controls also create their matching block-diagram terminals. Deleting a terminal deletes its associated panel control in the same undoable transaction.

        4  DEBUG
        The error-list button shows invalid wires and missing required inputs. Select a node and use Set Breakpoint in Properties, then Run: execution pauses before that node. Single Step evaluates one top-level node; a nested structure is stepped over atomically. Highlight Execution reveals the top-level evaluation sequence. Select a wire and Attach Probe to display its current value. Abort discards the incomplete frame. Feedback values commit only after a complete frame.

        5  STRUCTURES AND SUBVIS
        For Loop, While Loop, Case Structure and SubVI contain real nested numeric diagrams. Double-click to edit the body; use Parent Diagram to return. Connector Input reads state, i or x; Connector Output returns state. While Loop also needs one Boolean Loop Condition. For Loop count is an integer from zero to 10,000. While Loop must stop within 10,000 iterations. All nested work shares a 100,000-node evaluation budget. Case Structure evaluates only the selected branch; its FALSE branch is available in Properties. These are a documented subset, not full LabVIEW structure semantics.

        6  FILES AND RECOVERY
        Save downloads a .labspace.json project containing all VIs, diagrams and panel layouts. Open accepts this versioned format, not NI .vi or .lvproj binaries. Browser recovery is stored locally in IndexedDB and is periodically updated. The desktop host keeps a recovery file in the user's application-data directory. Explicit saves remain important; browser site-data clearing removes recovery. Export Waveform CSV exports the selected waveform or the first available result.

        NAVIGATION AND SHORTCUTS
        Mouse wheel: zoom at pointer. Middle drag: pan. Shift+wheel: horizontal pan. Fit to Window: frame the content. Ctrl+E: switch panel/diagram. Ctrl+R: Run. F6: continuous execution. F10: single step. Ctrl+S/O/N: save/open/new VI. On a focused canvas: Ctrl+Z/Y undo/redo, Ctrl+C/V copy/paste, Ctrl+D duplicate, Ctrl+A select all, Delete remove, arrow keys nudge. Escape cancels a canvas gesture. Some browser shortcuts are reserved; toolbar commands are always available.

        SIGNAL PROCESSING
        Simulate Signal supports sine, square and triangle waveforms with optional reproducible noise. Sampling frequency is limited to Nyquist. Moving Average is block-local, not a continuously stateful filter. FFT uses a periodic Hann window and coherent-gain-corrected one-sided amplitudes. The frequency axis uses sample-rate / sample-count spacing. RMS uses a scaled algorithm to avoid unnecessary overflow. Graph rendering preserves extrema through pixel-bucket decimation.
        """;
    public const string Compatibility = """
        INDEPENDENT EARLY IMPLEMENTATION

        LabSpace is an MIT-licensed visual instrumentation workbench. Its classic front-panel / block-diagram workflow is inspired by NI LabVIEW, but it is not NI LabVIEW, is not endorsed by NI, and does not include NI source, icons, drivers or proprietary runtime components.

        IMPLEMENTED
        Typed numeric, Boolean, string, numeric-array and waveform values; executable built-in arithmetic, comparison, logic, string, array and signal functions; numeric nested For / While / Case / SubVI bodies; explicit feedback state; top-level execution highlighting, breakpoints, step-over and probes; front-panel instruments; node and control editing; bounded undo/redo; versioned JSON and local recovery.

        NOT YET COMPATIBLE
        NI .vi, .ctl, .lvproj binary formats; the complete G language and connector type system; arbitrary clusters, variants, references, events, queues, channels, polymorphic VIs and project libraries; exact LabVIEW scheduling or reentrancy; full tunnels, shift registers and auto-indexing; step-into across nested structures; NI-DAQmx, VISA, instrument drivers, hardware configuration, FPGA or real-time targets; all LabVIEW palettes, dialogs, options, shortcuts and pixel-identical styling.

        EXECUTION AND SECURITY
        All bundled acquisition is simulated. This is not validated for operating physical equipment, safety-critical control or hard real-time use. A browser timer is not a deterministic acquisition clock. JSON import does not evaluate script or load plugins. File size, sample counts, graph nesting and execution budgets are bounded. Rendering is Skia through Uno; hardware acceleration and fallback depend on the host. The current implementation does not advertise WebGPU compute or a custom native GPU backend.

        COMPONENTS
        Core, Signals, Dataflow, Documents, Editing, Skia, Storage, Controls and Workbench are separate packable libraries. The C# engines and Uno workbench are shared between desktop and the browser. Native text-input and popup primitives are retained for input services; custom canvas content still needs richer per-element accessibility.
        """;
}
