using System.Globalization;
using LabSpace.Core;

namespace LabSpace.Dataflow;

public sealed class FormulaException(string message, int position) : Exception($"{message} (character {position + 1}).")
{
    public int Position { get; } = position;
}

/// <summary>Bounded scalar Formula Node bytecode. Control flow cannot access host code, files or networks.</summary>
public sealed partial class FormulaProgram
{
    private enum Op { Constant, Load, Store, Unset, Negate, Not, Boolean, Add, Subtract, Multiply, Divide, Modulo, Power, Equal, NotEqual, Less, LessEqual, Greater, GreaterEqual, Call, Jump, JumpFalse, JumpTrue }
    private readonly record struct Instruction(Op Code, double Number = 0, int Argument = 0);
    private readonly record struct Function(string Name, int Arity, Func<double[], int, double> Apply);
    private static readonly Function[] Functions =
    [
        new("sin", 1, (s,i) => Math.Sin(s[i])), new("cos", 1, (s,i) => Math.Cos(s[i])), new("tan", 1, (s,i) => Math.Tan(s[i])),
        new("asin", 1, (s,i) => Math.Asin(s[i])), new("acos", 1, (s,i) => Math.Acos(s[i])), new("atan", 1, (s,i) => Math.Atan(s[i])),
        new("atan2", 2, (s,i) => Math.Atan2(s[i], s[i+1])), new("sqrt", 1, (s,i) => Math.Sqrt(s[i])), new("abs", 1, (s,i) => Math.Abs(s[i])),
        new("exp", 1, (s,i) => Math.Exp(s[i])), new("ln", 1, (s,i) => Math.Log(s[i])), new("log", 1, (s,i) => Math.Log10(s[i])), new("log2", 1, (s,i) => Math.Log2(s[i])), new("log10", 1, (s,i) => Math.Log10(s[i])),
        new("floor", 1, (s,i) => Math.Floor(s[i])), new("ceil", 1, (s,i) => Math.Ceiling(s[i])), new("round", 1, (s,i) => Math.Round(s[i])),
        new("trunc", 1, (s,i) => Math.Truncate(s[i])), new("min", 2, (s,i) => Math.Min(s[i], s[i+1])), new("max", 2, (s,i) => Math.Max(s[i], s[i+1])),
        new("pow", 2, (s,i) => Math.Pow(s[i], s[i+1])), new("clamp", 3, (s,i) => Math.Clamp(s[i], s[i+1], s[i+2])),
        new("sinh", 1, (s,i) => Math.Sinh(s[i])), new("cosh", 1, (s,i) => Math.Cosh(s[i])), new("tanh", 1, (s,i) => Math.Tanh(s[i]))
    ];
    private readonly Instruction[] _code;
    private readonly (string Name, int Slot)[] _inputs, _outputs;
    private readonly int _slots;
    private FormulaProgram(Instruction[] code, (string, int)[] inputs, (string, int)[] outputs, int slots)
    { _code = code; _inputs = inputs; _outputs = outputs; _slots = slots; }
    public int InstructionCount => _code.Length;
    public static FormulaProgram Compile(string source, FormulaSignature signature) => new Parser(source, signature).Compile();
    public IReadOnlyDictionary<string, double> Evaluate(Func<string, double> input, ExecutionBudget? budget = null, int maximumInstructions = 65536, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (maximumInstructions is < 1 or > 1000000) throw new ArgumentOutOfRangeException(nameof(maximumInstructions));
        cancellation.ThrowIfCancellationRequested(); budget?.CheckCancellation();
        var values = new double[_slots]; var initialized = new bool[_slots];
        var stack = new double[Math.Max(1, _code.Length)]; var sp = 0; var executed = 0;
        foreach (var (name, slot) in _inputs) { values[slot] = Finite(input(name)); initialized[slot] = true; }
        for (var pc = 0; pc < _code.Length; pc++)
        {
            if (++executed > maximumInstructions) throw new ExecutionLimitException("Formula instruction budget exceeded.");
            if ((executed & 63) == 1) { cancellation.ThrowIfCancellationRequested(); budget?.CheckCancellation(); }
            var instruction = _code[pc];
            switch (instruction.Code)
            {
                case Op.Constant: stack[sp++] = instruction.Number; break;
                case Op.Load:
                    if (!initialized[instruction.Argument]) throw new InvalidOperationException("Formula variable is uninitialized.");
                    stack[sp++] = values[instruction.Argument]; break;
                case Op.Store: values[instruction.Argument] = Finite(stack[--sp]); initialized[instruction.Argument] = true; break;
                case Op.Unset: initialized[instruction.Argument] = false; break;
                case Op.Negate: stack[sp-1] = -stack[sp-1]; break;
                case Op.Not: stack[sp-1] = stack[sp-1] == 0 ? 1 : 0; break;
                case Op.Boolean: stack[sp-1] = stack[sp-1] != 0 ? 1 : 0; break;
                case Op.Jump: pc = instruction.Argument - 1; break;
                case Op.JumpFalse: if (stack[--sp] == 0) pc = instruction.Argument - 1; break;
                case Op.JumpTrue: if (stack[--sp] != 0) pc = instruction.Argument - 1; break;
                case Op.Call:
                    var function = Functions[instruction.Argument]; sp -= function.Arity;
                    var result = Finite(function.Apply(stack, sp)); stack[sp++] = result; break;
                default:
                    var b = stack[--sp]; var a = stack[sp-1];
                    stack[sp-1] = Finite(instruction.Code switch
                    {
                        Op.Add => a+b, Op.Subtract => a-b, Op.Multiply => a*b,
                        Op.Divide => b == 0 ? throw new DivideByZeroException() : a/b,
                        Op.Modulo => b == 0 ? throw new DivideByZeroException() : a%b,
                        Op.Power => Math.Pow(a,b), Op.Equal => a==b ? 1 : 0, Op.NotEqual => a!=b ? 1 : 0,
                        Op.Less => a<b ? 1 : 0, Op.LessEqual => a<=b ? 1 : 0, Op.Greater => a>b ? 1 : 0, Op.GreaterEqual => a>=b ? 1 : 0,
                        _ => throw new InvalidOperationException("Invalid formula opcode.")
                    }); break;
            }
        }
        foreach (var (name, slot) in _outputs)
            if (!initialized[slot]) throw new InvalidOperationException($"Formula output '{name}' is uninitialized.");
        return _outputs.ToDictionary(p => p.Name, p => values[p.Slot], StringComparer.Ordinal);
    }
    private static double Finite(double value) => double.IsFinite(value) ? value : throw new ArithmeticException("Formula produced a non-finite result.");
    private sealed record Token(string Text, int Position, double? Number = null);
    private sealed partial class Parser
    {
        private readonly List<Token> _tokens;
        private readonly List<Instruction> _code = [];
        private readonly Dictionary<string, int> _variables = new(StringComparer.Ordinal);
        private HashSet<int> _assigned = [];
        private readonly FormulaSignature _signature;
        private int _at, _depth;
        private Token Current => _tokens[Math.Min(_at, _tokens.Count - 1)];
        public Parser(string source, FormulaSignature signature)
        {
            ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(signature);
            if (source.Length > 16384) throw new FormulaException("Formula exceeds 16,384 characters", 0);
            if (signature.Inputs.IsDefault || signature.Outputs.IsDefault || signature.Inputs.Length > 32 || signature.Outputs.Length is < 1 or > 32)
                throw new FormulaException("Formula requires at most 32 inputs and 1–32 outputs", 0);
            _signature = signature; _tokens = Lex(source);
            foreach (var name in signature.Inputs.Concat(signature.Outputs))
            {
                if (!ValidVariable(name) || !_variables.TryAdd(name, _variables.Count))
                    throw new FormulaException("Formula terminal names must be distinct identifiers other than pi and e", 0);
            }
            _assigned.UnionWith(signature.Inputs.Select(name => _variables[name]));
            _slotCount = _variables.Count; _readOnly.UnionWith(_assigned); _scopes.Push(new());
        }
        public FormulaProgram Compile()
        {
            if (Current.Text == "<end>") throw Error("Enter an expression or output assignments");
            if (IsStatementStart())
            {
                while (Current.Text != "<end>") Statement();
            }
            else
            {
                if (_signature.Outputs.Length != 1) throw Error("Multiple outputs require assignment statements");
                Expression(); Emit(Op.Store, _variables[_signature.Outputs[0]]); _assigned.Add(_variables[_signature.Outputs[0]]); Take(";");
                if (Current.Text != "<end>") throw Error("Unexpected token after expression");
            }
            foreach (var name in _signature.Outputs)
                if (_reachable && !_assigned.Contains(_variables[name])) throw Error($"Output '{name}' is not assigned on every returning path");
            return new(_code.ToArray(), _signature.Inputs.Select(n => (n, _variables[n])).ToArray(), _signature.Outputs.Select(n => (n, _variables[n])).ToArray(), _slotCount);
        }
        private void Expression(int minimum = 0)
        {
            if (++_depth > 64) throw Error("Formula nesting exceeds 64 levels");
            try
            {
                Prefix();
                while (Precedence(Current.Text) is var precedence && precedence >= minimum && precedence > 0)
                {
                    var operation = Current.Text; _at++;
                    if (operation is "&&" or "||")
                    {
                        var branch = Emit(operation == "&&" ? Op.JumpFalse : Op.JumpTrue);
                        Expression(precedence+1); Emit(Op.Boolean); var end = Emit(Op.Jump);
                        Patch(branch); EmitConstant(operation == "&&" ? 0 : 1); Patch(end);
                    }
                    else
                    {
                        Expression(precedence + (operation == "**" ? 0 : 1));
                        Emit(operation switch
                        {
                            "+" => Op.Add, "-" => Op.Subtract, "*" => Op.Multiply, "/" => Op.Divide, "%" => Op.Modulo, "**" => Op.Power,
                            "==" => Op.Equal, "!=" => Op.NotEqual, "<" => Op.Less, "<=" => Op.LessEqual, ">" => Op.Greater, ">=" => Op.GreaterEqual,
                            _ => throw Error("Unsupported operator")
                        });
                    }
                }
                if (minimum == 0 && Take("?"))
                {
                    var alternate = Emit(Op.JumpFalse); Expression(); Expect(":"); var end = Emit(Op.Jump);
                    Patch(alternate); Expression(); Patch(end);
                }
            }
            finally { _depth--; }
        }
        private void Prefix()
        {
            if (Take("-")) { Expression(8); Emit(Op.Negate); return; }
            if (Take("+")) { Expression(8); return; }
            if (Take("!")) { Expression(8); Emit(Op.Not); return; }
            if (Take("(")) { Expression(); Expect(")"); return; }
            if (Current.Number is double number) { _at++; EmitConstant(number); return; }
            var name = Current.Text; _at++;
            if (Take("("))
            {
                var index = Array.FindIndex(Functions, f => f.Name == name);
                if (index < 0) throw Error($"Unknown function '{name}'");
                var count = 0;
                if (!Take(")")) { do { Expression(); count++; } while (Take(",")); Expect(")"); }
                if (count != Functions[index].Arity) throw Error($"{name} requires {Functions[index].Arity} arguments");
                Emit(Op.Call, index); return;
            }
            if (name is "pi" or "e") { EmitConstant(name == "pi" ? Math.PI : Math.E); return; }
            if (!_variables.TryGetValue(name, out var slot) || !_assigned.Contains(slot)) throw Error($"Unknown or unassigned variable '{name}'");
            Emit(Op.Load, slot);
        }
        private static int Precedence(string op) => op switch { "||" => 1, "&&" => 2, "==" or "!=" => 3, "<" or "<=" or ">" or ">=" => 4, "+" or "-" => 5, "*" or "/" or "%" => 6, "**" => 7, _ => 0 };
        private int Emit(Op op, int argument = 0)
        {
            if (_code.Count >= 2048) throw Error("Formula instruction budget exceeded");
            _code.Add(new(op, Argument: argument)); return _code.Count - 1;
        }
        private void EmitConstant(double value) { var at = Emit(Op.Constant); _code[at] = new(Op.Constant, value); }
        private void Patch(int index, int? target = null) => _code[index] = _code[index] with { Argument = target ?? _code.Count };
        private bool Take(string token) { if (Current.Text != token) return false; _at++; return true; }
        private void Expect(string token) { if (!Take(token)) throw Error($"Expected '{token}'"); }
        private FormulaException Error(string message) => new(message, _tokens[Math.Min(_at, _tokens.Count-1)].Position);
        private static List<Token> Lex(string source)
        {
            var tokens = new List<Token>(); var at = 0;
            while (at < source.Length)
            {
                var start = at; var c = source[at];
                if (char.IsWhiteSpace(c)) { at++; continue; }
                if (c == '/' && at+1 < source.Length && source[at+1] == '/') { while (at < source.Length && source[at] != '\n') at++; continue; }
                if (c == '/' && at+1 < source.Length && source[at+1] == '*')
                {
                    var end = source.IndexOf("*/", at+2, StringComparison.Ordinal);
                    if (end < 0) throw new FormulaException("Unterminated comment", at);
                    at = end+2; continue;
                }
                if (char.IsLetter(c) || c == '_')
                {
                    while (at < source.Length && (char.IsLetterOrDigit(source[at]) || source[at] == '_')) at++;
                    tokens.Add(new(source[start..at], start));
                }
                else if (char.IsDigit(c) || c == '.')
                {
                    while (at < source.Length && (char.IsDigit(source[at]) || source[at] == '.')) at++;
                    if (at < source.Length && source[at] is 'e' or 'E')
                    {
                        at++; if (at < source.Length && source[at] is '+' or '-') at++;
                        while (at < source.Length && char.IsDigit(source[at])) at++;
                    }
                    var text = source[start..at];
                    if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number)) throw new FormulaException("Invalid finite number", start);
                    tokens.Add(new(text, start, number));
                }
                else
                {
                    var text = at+1 < source.Length ? source.Substring(at,2) : "";
                    if (text is "&&" or "||" or "==" or "!=" or "<=" or ">=" or "**" or "++" or "--" or "+=" or "-=" or "*=" or "/=" or "%=") at += 2;
                    else { text = c.ToString(); at++; if (!"+-*/%!=<>()?,:;{}".Contains(c)) throw new FormulaException("Unsupported character", start); }
                    tokens.Add(new(text,start));
                }
                if (tokens.Count > 4096) throw new FormulaException("Formula token budget exceeded", at);
            }
            tokens.Add(new("<end>", source.Length)); return tokens;
        }
    }
}
