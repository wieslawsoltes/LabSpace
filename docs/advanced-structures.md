# Advanced structures and typed instrumentation

This guide describes **LabSpace 0.3.0-alpha.1** behavior. It does not claim complete NI Formula Node syntax, G runtime, binary VI or hardware compatibility. The [compatibility ledger](compatibility.md) records remaining limits.

## Case Structure

Create **Case Structure** from Quick Drop or the Structures palette. The selector can be Boolean, numeric, string or error. A structure stores 1–64 frames with stable IDs and one visible frame; only the runtime-selected frame executes. Its current visual frame is independent of runtime selection.

Click the center of its selector caption or **Cases and sequence frames**. The staged editor edits labels/defaults, adds/removes/duplicates and reorders deep-copied frames. Apply validates labels before committing. Cancel leaves the model and undo stack untouched. Each case uses the same named connector contract. Required new output connectors remain broken until wired; they are never silently assigned invented data.

Numeric cases use signed-32-bit values (finite doubles rounded to even at the selector). `0, 2, 5..9`, `..-1` and `10..` are examples; endpoints are inclusive. String cases use ordinal matching and optional ordinal case-insensitivity. Quote labels with commas or range punctuation: `"run", "start"` and `"a,b..c"`. A string range `"a".."d"` includes `"a"` but excludes `"d"`. Boolean labels are True/False. Error selectors support `No Error`, `Error`, numeric codes or `Error 10..20`; warnings follow No Error because status is false. At most 256 selector terms are accepted. Cross-frame overlap and insufficient coverage without a default are rejected.

**Case Dispatch.vi** maps `run`/`start` to 42, `idle` to zero and other strings to -1. Changing the visible case does not change that computation.

## Stacked Sequence

Sequence frames execute in order. Ordinary input tunnels are available in every frame; each output has one producer frame and becomes available outside the sequence only after the entire sequence completes. **Sequence local write** names a typed value; **Sequence local read** accesses it only in later frames of that same sequence. A local has one writer. Reads in the same/earlier frame, mismatched types, duplicate writers and locals outside sequence frames produce diagnostics.

Double-click the structure body to enter its visible frame. **Previous frame / Next frame** navigate siblings. The frame editor preserves each body when reordering. Moving a reader before its writer intentionally reports an invalid dependency; Undo restores the original ordering. Running from inside a frame executes the whole root VI with its caller context, not a detached fragment.

**Sequence Pipeline.vi** writes sample=21 in Acquire, then reads that local in Process, evaluates `result = x * 4;` and outputs 84. Changing the formula to multiply by 5 produces 105.

## Formula Node

Double-click a Formula Node or choose **Edit formula**. Inputs and outputs are comma-separated, distinct identifiers. Inputs are read-only. A single-output node accepts a scalar expression; explicit assignments support multiple outputs and intermediate locals:

```text
positive = max(x, 0);
square = positive * positive;
result = square > 100 ? sqrt(square) : square;
```

Every output must be definitely assigned. Reading an undefined/unassigned variable, duplicate terminal names, invalid arity, malformed source or exceeded budgets reports a character position without committing the draft. Renaming/removing a terminal removes affected wires; Undo restores source, signatures and wires together. The legacy `result` source alias continues to identify the first output.

Supported operators: `+ - * / % **`, comparisons, `! && ||`, and lazy `?:`. Parentheses control grouping; power is right-associative. `&&` and `||` short-circuit. Both line and block comments are supported. Numeric literals allow exponent notation.

Supported functions: `sin`, `cos`, `tan`, `asin`, `acos`, `atan`, `atan2`, `sqrt`, `abs`, `exp`, `ln`, `log`, `log10`, `log2`, `floor`, `ceil`, `round`, `trunc`, `min`, `max`, `pow`, `clamp`, `sinh`, `cosh`, `tanh`. Constants: `pi`, `e`. `ln` is natural logarithm; `log`/`log10` use base 10; `log2` uses base 2.

Control flow supports `if`/`else`, `for`, `while`, `do`/`while`, `break`, `continue`, lexical blocks, `double`/`float64` declarations, compound assignments and prefix/postfix increment/decrement statements. Inputs remain read-only. Definite-assignment analysis merges reachable branches and accounts for loop exits; local slots are reset on re-entry. Declarations are scalar doubles, not the full C type system. Side effects inside arbitrary expressions and switch/return statements are not supported.

**Formula Control Flow.vi** supplies count=10 and gain=2 to:

```c
// Skip one sample; no hardware or external code is evaluated.
double total = 0;
double used = 0;
for (double i = 0; i < count; i++) {
    if (i == 3) continue;
    total += i * gain;
    used++;
}
result = total;
iterations = used;
```

The independent outputs are 84 and 9. A probe on `result` demonstrates the modeless Debug window and retained values after switching to another VI.

Limits: 16,384 source characters, 4,096 tokens, 2,048 emitted instructions, 64 nesting levels, 256 scalar variable slots, 32 inputs and 32 outputs. Compiled bytecode is retained in the graph plan. Each evaluation has instruction fuel (65,536 by default, at most 1,000,000) and checks cancellation before execution and periodically during interpretation. Budget exhaustion, division/remainder by zero and non-finite results fail the frame without committing its pending register state. Individual formula evaluations do not yield to the browser event loop. Arrays, general C declarations/types, units, native calls and external code/I/O remain unsupported.

## Error clusters and complex values

An error cluster is immutable `(Boolean status, Int32 code, String source)`. Nonzero code with false status denotes a warning. Bundle/Unbundle have independently typed terminals. Merge Errors selects the first error in x/y order, otherwise the first nonzero warning, otherwise no error. Clear Errors consumes its dependency and returns no error. The editor validates signed-32-bit codes; no driver calls are performed.

Complex values are finite double-precision real/imaginary pairs. Construct/decompose, addition, subtraction, multiplication, division, conjugate, magnitude and phase are executable functions. Division by zero and non-finite results are rejected. The front panel has custom Error and Complex controls and indicators; controls open staged native-text-input editors and indicators reflect real values. General clusters, complex arrays and automatic coercion are outside this release.

## Editing and debugging

Right-click input/output terminals for direction- and type-appropriate **Create Constant**, **Create Control**, **Create Indicator**, **Branch Wire** and **Disconnect Terminal** commands. Creation inserts the terminal, relevant panel widget and wire as one transaction. Output fan-out retains existing destinations. An input still has exactly one driver. Disconnect resolves named/primary output aliases.

Select a structure or formula and drag its lower-right resize handle. Release commits one history transaction; Escape/capture cancellation restores geometry. Frame captions and filmstrip edges are independently drawn vectors; formula bodies preview their actual source. Physical-GPU performance is not inferred from headless validation.

F10 steps over, F11 steps into nested execution, and Ctrl+F11 steps out of the active child to its caller. Nested breakpoints can interrupt step-out. Cooperative scheduling does not preempt individual kernels and is not hard-real-time execution. Read-only browser diagnostics expose model/hit-target snapshots only when `?test=1`; acceptance tests perform real pointer and keyboard input.

## Reuse

```csharp
using LabSpace.Core;
using LabSpace.Dataflow;
using LabSpace.Documents;

var vi = AdvancedExamples.Sequence();
var sequence = vi.Diagram.Nodes.Single(n => n.Kind == "sequence");
var frame = new DataflowRuntime().Run(GraphCompiler.Compile(vi.Diagram));
Console.WriteLine(frame.GetOutput(sequence.Id).Number); // 84

var formula = FormulaProgram.Compile("result = sqrt(x*x + 16);", new FormulaSignature());
Console.WriteLine(formula.Evaluate(_ => 3)["result"]); // 5
```

The nine package boundaries remain unchanged. Frames, signatures and new values are represented by the Core/Dataflow/Documents packages; staged edits belong to Editing; canvas geometry/rendering to Skia; editors to Controls; command composition to Workbench. Document format 3 migrates versions 1 and 2.
