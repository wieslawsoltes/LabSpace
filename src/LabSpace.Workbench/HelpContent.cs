namespace LabSpace.Workbench;

public static class HelpContent
{
    public const string UserGuide = """
        ADVANCED STRUCTURES AND TYPED VALUES

        Multi-case structures choose one Boolean, numeric, string or error case. Arrow buttons on the frame caption change the visible body without changing execution selection. Click the caption or Cases and sequence frames to edit a deep draft: labels, Default, duplicate, add, remove and reorder. Apply validates labels; Cancel changes nothing. Numeric ranges include both ends; string ranges exclude the upper end. A new unwired required output intentionally breaks Run until connected.

        Sequence frames execute in order. A Sequence local write publishes a named typed value to later Sequence local read nodes. One frame owns each local writer. Use Previous/Next frame while inside a sequence. Running inside an editor still runs the root VI, so locals retain their caller context. Reordering a reader before its writer reports an error; Undo restores both frames and wiring.

        Double-click a Formula Node to edit named inputs, named outputs and source. Inputs are read-only. Enter a scalar expression or assignments such as temp = x * 2; result = temp + 1;. All outputs must be assigned. Arithmetic, comparisons, &&, || and ?: are supported. ln is natural log; log/log10 are base ten; log2 is base two. Invalid source stays in the editor with a character position. This bounded subset cannot run loops, native code or I/O.

        Error controls edit Boolean status, signed 32-bit code and source. Nonzero code with FALSE status is a warning. Merge Errors prefers the first error, otherwise the first warning. Complex controls edit real/imaginary finite doubles. Indicators display actual diagram values.

        Right-click a terminal to Create Constant, Create Control, Create Indicator, Branch Wire or Disconnect, as appropriate for its direction/type. Creation is one undoable transaction. Select a structure or formula and drag its lower-right handle to resize. Ctrl+F11 steps out of the active execution frame; F11 steps in and F10 steps over. No physical I/O or NI binary compatibility is implied.

        YOUR FIRST VIRTUAL INSTRUMENT

        LabSpace opens Signal Analysis.vi, a working simulated acquisition. The front panel is the instrument interface; the block diagram is its executable program. The example runs once at startup. No physical hardware is connected. Arithmetic.vi, Stateful Loop.vi, Indexed Accumulator.vi, Case Dispatch.vi, Sequence Pipeline.vi and Errors and Complex.vi are also available in the project explorer.

        1  OPERATE THE FRONT PANEL
        Drag the Amplitude knob vertically or horizontally. Click Frequency to enter a value. Run updates the graphs, RMS gauge and alarm. Run Continuously targets a 40 ms timer interval; this is not real-time scheduling. Click a plot to place a value cursor. Double-click a control to edit its value.

        2  EDIT THE PROGRAM
        Choose Block Diagram or press Ctrl+E. Move functions by dragging their bodies. Click an output terminal, then a compatible input; dragging directly also works. Each input has one driver, while outputs can fan out. Structures have separately named outputs: wire the terminal for the value you need. Numeric wires are orange, Boolean green, string pink and waveform brown. Array wires are thicker.

        3  QUICK DROP AND PANEL DESIGN
        Press Ctrl+Space to search for a function or control. Use Up/Down, press Enter, then click the canvas to place it. Escape cancels without changing the project. Right-click either canvas for its context palette. Palette tiles also insert directly. On the front panel, Edit Front Panel enables moving controls; drag the lower-right corner to resize. Properties edits labels, ranges, dimensions and widget style. Controls and their diagram terminals are linked in one undoable transaction.

        4  TUNNELS AND SHIFT REGISTERS
        Select a For, While, Case or SubVI and open Tunnels and shift registers from the toolbar or Properties. Changes remain a draft until Apply; Cancel does not touch the model. Apply creates or updates the named body connectors in one undoable edit. Removing or renaming a terminal removes its attached wires. Type changes can leave wires invalid, which the compiler reports rather than silently coercing.

        Ordinary tunnels and registers support numbers, Booleans, strings, numeric arrays, waveforms, error clusters and complex values. Input indexing on a For Loop supplies one numeric array element per iteration; the shortest indexed input and count bound execution. While indexing supplies zero after an array ends and does not determine termination. Outputs can return the last value, collect numbers, concatenate numeric arrays or collect only iterations whose named Boolean inclusion output is TRUE.

        Initialized registers reset for each invocation. Uninitialized registers retain committed state at their invocation path until runtime reset, a code-affecting edit, Undo/Redo or VI/body navigation. Stacked histories are named state, state:1, state:2, and so on. At zero For iterations, collections are empty and registers return initialization or prior state. A While Loop executes at least once; the condition can mean Stop when TRUE or Continue while TRUE. Conditional For loops combine the condition with the iteration bound.

        TRY INDEXED ACCUMULATOR.VI
        Run the array 1,2,3,4,5 through its For Loop. Final sum is 15; running sums are 1,3,6,10,15. Open the loop's connector editor, turn off Initialized and Apply. Run twice to obtain 15 then 30. Undo restores initialization and its external wire. Double-click the loop to edit the actual body shown in its thumbnail. Parent Diagram returns; TRUE/FALSE buttons select a Case branch for editing.

        5  DEBUG
        A broken Run arrow opens the compile error list. F10 steps over a top-level node; F11 steps into resumable nested work. Context Help shows the active nested frame and recent values without changing the editor navigation path. Set breakpoints inside a body, return to its caller, then Run to stop before that node. Run resumes. Highlight advances one activation transition per tick. Select a wire and Attach Probe to inspect its named output value. Abort discards the active frame; pending feedback and uninitialized-register values commit only after the root frame succeeds.

        6  FILES AND RECOVERY
        Save downloads version-3 .labspace.json containing VIs, diagrams, contracts and panel layouts. Version-1 projects migrate on load; older LabSpace 0.1 cannot read version 2. NI .vi, .ctl and .lvproj files are not parsed. Browser recovery is periodic local IndexedDB storage; desktop recovery uses the user's application-data directory. Keep explicit saves. Export Waveform CSV writes the selected waveform or first available result. Imported JSON does not evaluate script or install drivers.

        NAVIGATION
        Mouse wheel zooms at the pointer; middle drag pans; Shift+wheel pans horizontally. Fit frames content. Ctrl+E switches views, Ctrl+R runs, F6 runs continuously, F10/F11 step over/into, Ctrl+Space opens Quick Drop, Ctrl+S/O/N save/open/new VI. On a canvas, Ctrl+Z/Y undo/redo, Ctrl+C/V copy/paste, Ctrl+D duplicate, Ctrl+A select all, Delete removes and arrows nudge. Some browser/OS shortcuts take precedence; equivalent toolbar commands remain available.

        SIGNALS AND LIMITS
        Sources support sine, square and triangle signals with optional deterministic noise. Signal frequency must not exceed Nyquist. Moving Average is block-local. FFT uses a periodic Hann window with coherent-gain-corrected one-sided amplitudes. RMS avoids unnecessary overflow. Plot decimation preserves extrema. Loops are limited to 10,000 iterations; nested work shares a 100,000-node budget. An individual kernel runs to completion: cooperative UI scheduling is not preemptive or hard real time.
        """;
    public const string Compatibility = """
        INDEPENDENT EARLY IMPLEMENTATION — 0.2

        LabSpace is an MIT-licensed instrumentation workbench inspired by the classic NI LabVIEW workflow. It is not NI LabVIEW, is not endorsed by NI and contains no NI source, icons, drivers or proprietary runtime components.

        IMPLEMENTED
        Five typed value kinds; executable arithmetic, comparison, Boolean, Unicode string, numeric-array and signal functions; named typed For/While/Boolean Case/embedded SubVI contracts; numeric input/output indexing, conditional collection, array concatenation, initialized/uninitialized stacked shift registers and feedback; cooperative nested execution, nested breakpoints, step-over/step-into and named-output probes; actual-body previews, compact terminals, Quick Drop cursor placement, staged connector editing, panel controls, transactions, bounded history and version-3 JSON with version-1 migration.

        REMAINING COMPATIBILITY
        NI VI/control/project formats and external VI dependency loading; the full G language and type/coercion system, integer widths, clusters, variants, references, general and multidimensional arrays, event/sequence structures, queues/channels, polymorphic VIs and libraries; exact NI scheduling, reentrancy, compiler and plugin systems; NI-DAQmx, VISA and instrument drivers; physical acquisition, FPGA and real-time targets; every control, palette, dialog, option, shortcut, native floating window and pixel-identical styling.

        Collection indexing currently handles numeric scalars and numeric 1D arrays, not arbitrary element/rank types. Strings are managed Unicode with explicit UTF-8 byte length; this is not full NI byte-string/PCRE2 compatibility. Case selectors are Boolean. SubVI bodies are embedded, not externally linked or recursive.

        EXECUTION AND SECURITY
        Acquisition is simulated. This runtime is not validated for physical equipment, safety-critical control or hard real time. JSON does not execute script or load plugins. Imports, sample counts, nesting and execution are bounded. Pending feedback/register state commits after successful root execution; arbitrary side effects and random-generator state are not transactional. Rendering uses Skia through Uno, with host-dependent hardware acceleration or fallback. Dataflow/DSP run in managed C#, not WebGPU compute.

        COMPONENTS
        Core, Signals, Dataflow, Documents, Editing, Skia, Storage, Controls and Workbench are separate packable libraries shared by desktop and browser. Native text, picker and popup primitives remain; custom canvas content needs richer per-element accessibility. Package artifacts are not proof of NuGet-feed publication. Desktop build checks are not proof that every native integration or physical GPU has been interactively tested.
        """;
}
