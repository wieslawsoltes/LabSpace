using System.Globalization;
using LabSpace.Core;

namespace LabSpace.Dataflow;

/// <summary>Bounded, host-independent scalar Formula Node bytecode. No reflection, dynamic compilation or host-code evaluation.</summary>
public sealed class FormulaProgram
{
    private enum Op { Constant, Load, Store, Pop, Unary, Binary, Call, Jump, JumpFalse }
    private readonly record struct Instruction(Op Op, double Number = 0, int Target = 0, string Text = "");
    private readonly Instruction[] _code;
    private readonly string[] _variables, _inputs, _outputs;
    private readonly int[] _inputSlots, _outputSlots;
    public int InstructionCount => _code.Length;
    private FormulaProgram(Parser parser, StructureContract contract)
    {
        _code = parser.Code.ToArray(); _variables = parser.Variables.ToArray();
        _inputs = contract.Inputs.Select(t => t.Name).ToArray(); _outputs = contract.Outputs.Select(t => t.Name).ToArray();
        _inputSlots = _inputs.Select(n => Array.IndexOf(_variables, n)).ToArray(); _outputSlots = _outputs.Select(n => Array.IndexOf(_variables, n)).ToArray();
    }
    public static FormulaProgram Compile(string source, StructureContract contract)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(contract);
        if (source.Length > 32768) throw new ArgumentException("Formula source exceeds 32,768 characters.");
        if (contract.Inputs.IsDefault || contract.Outputs.IsDefault || contract.Inputs.Length > 32 || contract.Outputs.Length is < 1 or > 32 || contract.Inputs.Any(t => t.Type != ValueKind.Number) || contract.Outputs.Any(t => t.Type != ValueKind.Number)) throw new ArgumentException("Formula requires numeric terminals and 1–32 outputs.");
        var parser = new Parser(source, contract); parser.Parse(); return new(parser, contract);
    }
    public Dictionary<string, Value> Evaluate(Func<string, Value> input, ExecutionBudget? budget = null, int maximumInstructions = 65536)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (maximumInstructions is < 1 or > 1000000) throw new ArgumentOutOfRangeException(nameof(maximumInstructions));
        var slots = new double[_variables.Length]; var assigned = new bool[_variables.Length]; var stack = new double[256]; var sp = 0; var pc = 0; var fuel = 0;
        for (var i = 0; i < _inputs.Length; i++)
        {
            var value = input(_inputs[i]); if (value.Kind != ValueKind.Number) throw new ArgumentException("Formula input must be numeric.");
            slots[_inputSlots[i]] = value.Number; assigned[_inputSlots[i]] = true;
        }
        double Pop() => sp > 0 ? stack[--sp] : throw new InvalidOperationException("Invalid formula stack.");
        void Push(double value)
        {
            if (!double.IsFinite(value)) throw new ArithmeticException("Formula produced a non-finite value.");
            if (sp >= stack.Length) throw new ExecutionLimitException("Formula expression stack exceeded.");
            stack[sp++] = value;
        }
        while (pc < _code.Length)
        {
            if (++fuel > maximumInstructions) throw new ExecutionLimitException("Formula instruction budget exceeded.");
            if ((fuel & 63) == 1) budget?.CheckCancellation();
            var op = _code[pc++];
            switch (op.Op)
            {
                case Op.Constant: Push(op.Number); break;
                case Op.Load:
                    if (!assigned[op.Target]) throw new ArithmeticException("Formula variable read before assignment: " + _variables[op.Target]);
                    Push(slots[op.Target]); break;
                case Op.Store: slots[op.Target] = Pop(); assigned[op.Target] = true; break;
                case Op.Pop: Pop(); break;
                case Op.Unary: var x = Pop(); Push(op.Text switch { "-" => -x, "!" => x == 0 ? 1 : 0, _ => x }); break;
                case Op.Binary:
                    var b = Pop(); var a = Pop();
                    Push(op.Text switch
                    {
                        "+" => a + b, "-" => a - b, "*" => a * b, "/" => b == 0 ? throw new DivideByZeroException() : a / b,
                        "%" => b == 0 ? throw new DivideByZeroException() : a % b, "**" => Math.Pow(a, b),
                        "<" => a < b ? 1 : 0, "<=" => a <= b ? 1 : 0, ">" => a > b ? 1 : 0, ">=" => a >= b ? 1 : 0,
                        "==" => a == b ? 1 : 0, "!=" => a != b ? 1 : 0, _ => throw new InvalidOperationException("Invalid formula operator.")
                    }); break;
                case Op.Call:
                    var second = op.Target == 2 ? Pop() : 0; Push(Call(op.Text, Pop(), second)); break;
                case Op.Jump: pc = op.Target; break;
                case Op.JumpFalse: if (Pop() == 0) pc = op.Target; break;
            }
        }
        var result = new Dictionary<string, Value>(StringComparer.Ordinal);
        for (var i = 0; i < _outputs.Length; i++)
        {
            if (!assigned[_outputSlots[i]]) throw new ArithmeticException("Formula output was not assigned: " + _outputs[i]);
            result[_outputs[i]] = Value.Numeric(slots[_outputSlots[i]]);
        }
        return result;
    }
    private static int Arity(string name) => name switch
    {
        "sin" or "cos" or "tan" or "asin" or "acos" or "atan" or "sinh" or "cosh" or "tanh" or "sqrt" or "abs" or "exp" or "ln" or "log" or "log2" or "floor" or "ceil" or "round" or "sign" => 1,
        "pow" or "min" or "max" or "atan2" or "hypot" => 2, _ => 0
    };
    private static double Call(string name, double a, double b) => name switch
    {
        "sin" => Math.Sin(a), "cos" => Math.Cos(a), "tan" => Math.Tan(a), "asin" => Math.Asin(a), "acos" => Math.Acos(a), "atan" => Math.Atan(a),
        "sinh" => Math.Sinh(a), "cosh" => Math.Cosh(a), "tanh" => Math.Tanh(a), "sqrt" => Math.Sqrt(a), "abs" => Math.Abs(a), "exp" => Math.Exp(a),
        "ln" => Math.Log(a), "log" => Math.Log10(a), "log2" => Math.Log2(a), "floor" => Math.Floor(a), "ceil" => Math.Ceiling(a), "round" => Math.Round(a), "sign" => Math.Sign(a),
        "pow" => Math.Pow(a, b), "min" => Math.Min(a, b), "max" => Math.Max(a, b), "atan2" => Math.Atan2(a, b), "hypot" => System.Numerics.Complex.Abs(new(a, b)),
        _ => throw new InvalidOperationException("Unsupported formula function.")
    };
    private readonly record struct Token(string Text, int Position);
    private sealed class Parser
    {
        private readonly List<Token> _tokens = [];
        private readonly StructureContract _contract;
        private readonly HashSet<string> _written = new(StringComparer.Ordinal);
        private readonly Stack<(List<int> Breaks, List<int> Continues)> _loops = new();
        private int _next, _depth;
        public List<Instruction> Code { get; } = [];
        public List<string> Variables { get; } = [];
        private string Current => _tokens[_next].Text;
        public Parser(string source, StructureContract contract)
        {
            _contract = contract; Lex(source);
            foreach (var t in contract.Inputs) Register(t.Name);
            foreach (var t in contract.Outputs) if (!Variables.Contains(t.Name)) Register(t.Name);
        }
        private ArgumentException Error(string message) => new(message + " At character " + _tokens[_next].Position + ".");
        public void Parse()
        {
            while (Current != "<end>") Statement();
            foreach (var t in _contract.Outputs) if (!_written.Contains(t.Name)) throw Error("Output is never assigned: " + t.Name);
        }
        private int Register(string name)
        {
            if (!Identifier(name) || Variables.Count >= 256 || Variables.Contains(name) || name is "pi" or "e" || Arity(name) > 0) throw Error("Invalid, duplicate or excessive formula variable: " + name);
            Variables.Add(name); return Variables.Count - 1;
        }
        private int Emit(Instruction instruction)
        {
            if (Code.Count >= 8192) throw Error("Formula exceeds 8,192 instructions."); Code.Add(instruction); return Code.Count - 1;
        }
        private void Patch(int index, int target) => Code[index] = Code[index] with { Target = target };
        private bool Take(string text) { if (Current != text) return false; _next++; return true; }
        private void Need(string text) { if (!Take(text)) throw Error("Expected '" + text + "', found '" + Current + "'"); }
        private static bool Identifier(string name) => name.Length > 0 && (char.IsAsciiLetter(name[0]) || name[0] == '_') && name.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '_');
        private void Statement()
        {
            if (++_depth > 64) throw Error("Formula nesting exceeds 64.");
            try
            {
                if (Take(";")) return;
                if (Take("{")) { while (!Take("}")) { if (Current == "<end>") throw Error("Unclosed block."); Statement(); } return; }
                if (Take("if"))
                {
                    Need("("); Expression(); Need(")"); var no = Emit(new(Op.JumpFalse)); Statement();
                    if (Take("else")) { var end = Emit(new(Op.Jump)); Patch(no, Code.Count); Statement(); Patch(end, Code.Count); } else Patch(no, Code.Count);
                    return;
                }
                if (Take("while"))
                {
                    var condition = Code.Count; Need("("); Expression(); Need(")"); var end = Emit(new(Op.JumpFalse)); _loops.Push(([], []));
                    Statement(); Emit(new(Op.Jump, Target: condition)); Patch(end, Code.Count); FinishLoop(condition); return;
                }
                if (Take("for"))
                {
                    Need("("); if (Current != ";") Simple(); Need(";"); var condition = Code.Count;
                    if (Current == ";") Emit(new(Op.Constant, 1)); else Expression(); Need(";"); var end = Emit(new(Op.JumpFalse));
                    // Place the increment before the body in bytecode, with a first-iteration jump over it.
                    var bodyJump = Emit(new(Op.Jump)); var increment = Code.Count;
                    if (Current != ")") Simple(); Need(")"); Emit(new(Op.Jump, Target: condition)); Patch(bodyJump, Code.Count);
                    _loops.Push(([], [])); Statement(); Emit(new(Op.Jump, Target: increment)); Patch(end, Code.Count); FinishLoop(increment); return;
                }
                if (Take("do"))
                {
                    var body = Code.Count; _loops.Push(([], [])); Statement(); var condition = Code.Count;
                    Need("while"); Need("("); Expression(); Need(")"); Need(";"); var end = Emit(new(Op.JumpFalse)); Emit(new(Op.Jump, Target: body)); Patch(end, Code.Count); FinishLoop(condition); return;
                }
                if (Current is "break" or "continue")
                {
                    if (_loops.Count == 0) throw Error("Loop control used outside a loop.");
                    var isBreak = Take("break"); if (!isBreak) Need("continue"); var jump = Emit(new(Op.Jump));
                    if (isBreak) _loops.Peek().Breaks.Add(jump); else _loops.Peek().Continues.Add(jump); Need(";"); return;
                }
                Simple(); Need(";");
            }
            finally { _depth--; }
        }
        private void FinishLoop(int continueTarget)
        {
            var loop = _loops.Pop(); foreach (var p in loop.Breaks) Patch(p, Code.Count); foreach (var p in loop.Continues) Patch(p, continueTarget);
        }
        private void Simple()
        {
            var declaration = Take("float64") || Take("double");
            var name = Current; if (!Identifier(name)) throw Error("Expected a scalar assignment."); _next++;
            var slot = Variables.IndexOf(name);
            if (declaration) slot = Register(name);
            else if (slot < 0) throw Error("Undeclared formula variable: " + name);
            var op = Current;
            if (op is "++" or "--") { _next++; Emit(new(Op.Load, Target: slot)); Emit(new(Op.Constant, 1)); Emit(new(Op.Binary, Text: op == "++" ? "+" : "-")); }
            else if (op is "=" or "+=" or "-=" or "*=" or "/=" or "%=")
            {
                _next++; if (op != "=") Emit(new(Op.Load, Target: slot)); Expression(); if (op != "=") Emit(new(Op.Binary, Text: op[..1]));
            }
            else if (declaration) return;
            else throw Error("Expected assignment or increment.");
            Emit(new(Op.Store, Target: slot)); _written.Add(name);
        }
        private static int Precedence(string op) => op switch { "||" => 1, "&&" => 2, "==" or "!=" => 3, "<" or "<=" or ">" or ">=" => 4, "+" or "-" => 5, "*" or "/" or "%" => 6, "**" => 7, _ => 0 };
        private void Expression(int minimum = 1)
        {
            if (++_depth > 64) throw Error("Expression nesting exceeds 64.");
            try
            {
                if (Current is "+" or "-" or "!") { var op = Current; _next++; Expression(8); Emit(new(Op.Unary, Text: op)); }
                else if (Take("(")) { Expression(); Need(")"); }
                else
                {
                    var text = Current; _next++;
                    if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number)) Emit(new(Op.Constant, number));
                    else if (text is "pi" or "e") Emit(new(Op.Constant, text == "pi" ? Math.PI : Math.E));
                    else if (Identifier(text))
                    {
                        if (Take("("))
                        {
                            var arity = Arity(text); if (arity == 0) throw Error("Unknown scalar function: " + text);
                            Expression(); if (arity == 2) { Need(","); Expression(); } Need(")"); Emit(new(Op.Call, Target: arity, Text: text));
                        }
                        else { var slot = Variables.IndexOf(text); if (slot < 0) throw Error("Undeclared variable: " + text); Emit(new(Op.Load, Target: slot)); }
                    }
                    else { _next--; throw Error("Expected numeric expression."); }
                }
                while (Precedence(Current) >= minimum)
                {
                    var op = Current; var precedence = Precedence(op); _next++;
                    if (op is "&&" or "||")
                    {
                        if (op == "||") Emit(new(Op.Unary, Text: "!"));
                        var shortCircuit = Emit(new(Op.JumpFalse)); Expression(precedence + 1); Emit(new(Op.Unary, Text: "!")); Emit(new(Op.Unary, Text: "!"));
                        var end = Emit(new(Op.Jump)); Patch(shortCircuit, Code.Count); Emit(new(Op.Constant, op == "&&" ? 0 : 1)); Patch(end, Code.Count);
                    }
                    else { Expression(op == "**" ? precedence : precedence + 1); Emit(new(Op.Binary, Text: op)); }
                }
                if (minimum == 1 && Take("?"))
                {
                    var otherwise = Emit(new(Op.JumpFalse)); Expression(); Need(":"); var end = Emit(new(Op.Jump)); Patch(otherwise, Code.Count); Expression(); Patch(end, Code.Count);
                }
            }
            finally { _depth--; }
        }
        private void Lex(string source)
        {
            var i = 0;
            while (i < source.Length)
            {
                if (_tokens.Count >= 16384) throw new ArgumentException("Formula token budget exceeded.");
                if (char.IsWhiteSpace(source[i])) { i++; continue; }
                if (i + 1 < source.Length && source[i] == '/' && source[i + 1] == '/') { while (i < source.Length && source[i] != '\n') i++; continue; }
                if (i + 1 < source.Length && source[i] == '/' && source[i + 1] == '*') { var end = source.IndexOf("*/", i + 2, StringComparison.Ordinal); if (end < 0) throw new ArgumentException("Unterminated formula comment."); i = end + 2; continue; }
                var start = i;
                if (char.IsAsciiLetter(source[i]) || source[i] == '_') { while (i < source.Length && (char.IsAsciiLetterOrDigit(source[i]) || source[i] == '_')) i++; }
                else if (char.IsAsciiDigit(source[i]) || source[i] == '.')
                {
                    i++; while (i < source.Length && (char.IsAsciiDigit(source[i]) || source[i] == '.')) i++;
                    if (i < source.Length && source[i] is 'e' or 'E') { i++; if (i < source.Length && source[i] is '+' or '-') i++; while (i < source.Length && char.IsAsciiDigit(source[i])) i++; }
                }
                else if (i + 1 < source.Length && source.Substring(i, 2) is "++" or "--" or "+=" or "-=" or "*=" or "/=" or "%=" or "**" or "<=" or ">=" or "==" or "!=" or "&&" or "||") i += 2;
                else i++;
                _tokens.Add(new(source[start..i], start));
            }
            _tokens.Add(new("<end>", source.Length));
        }
    }
}
