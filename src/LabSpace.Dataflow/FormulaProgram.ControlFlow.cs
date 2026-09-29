using LabSpace.Core;

namespace LabSpace.Dataflow;

public sealed partial class FormulaProgram
{
    private sealed partial class Parser
    {
        private sealed class Scope
        {
            public Dictionary<string, int?> Bindings { get; } = new(StringComparer.Ordinal);
            public HashSet<int> Declarations { get; } = [];
        }
        private sealed record Flow(bool Reachable, HashSet<int> Assigned);
        private sealed class Loop
        {
            public List<(int Jump, Flow State)> Breaks { get; } = [];
            public List<(int Jump, Flow State)> Continues { get; } = [];
        }
        private readonly Stack<Scope> _scopes = new();
        private readonly Stack<Loop> _loops = new();
        private readonly HashSet<int> _readOnly = [];
        private int _slotCount;
        private bool _reachable = true;
        private static bool ValidVariable(string name) => StructureFrames.Identifier(name) && name is not
            ("pi" or "e" or "if" or "else" or "while" or "for" or "do" or "break" or "continue" or "double" or "float64" or "switch" or "case" or "default" or "return");
        private bool IsStatementStart() => Current.Text is "{" or ";" or "if" or "while" or "for" or "do" or "break" or "continue" or "float64" or "double" or "++" or "--"
            || (_at + 1 < _tokens.Count && _tokens[_at + 1].Text is "=" or "+=" or "-=" or "*=" or "/=" or "%=" or "++" or "--");
        private Flow CaptureFlow() => new(_reachable, new(_assigned));
        private void RestoreFlow(Flow flow) { _reachable = flow.Reachable; _assigned = new(flow.Assigned); }
        private void MergeFlows(IEnumerable<Flow> flows)
        {
            var paths = flows.Where(f => f.Reachable).ToArray();
            _reachable = paths.Length > 0;
            if (!_reachable) return;
            _assigned = new(paths[0].Assigned);
            foreach (var path in paths.Skip(1)) _assigned.IntersectWith(path.Assigned);
        }
        private void EnterScope() => _scopes.Push(new());
        private void ExitScope()
        {
            foreach (var (name, old) in _scopes.Pop().Bindings)
            {
                _assigned.Remove(_variables[name]);
                if (old is int slot) _variables[name] = slot; else _variables.Remove(name);
            }
        }
        private int NewVariable(string name, bool declaration)
        {
            if (!ValidVariable(name)) throw Error("Invalid or reserved variable name");
            var scope = _scopes.Peek();
            var found = _variables.TryGetValue(name, out var old);
            // Outermost explicit output declarations bind their existing connector slots.
            if (declaration && _scopes.Count == 1 && found && _signature.Outputs.Contains(name) && scope.Declarations.Add(old)) return old;
            if (declaration && scope.Bindings.ContainsKey(name) || found && _scopes.Count == 1)
                throw Error($"Variable '{name}' is already declared in this scope");
            if (_slotCount >= 256) throw Error("Formula variable budget exceeded");
            var slot = _slotCount++;
            scope.Bindings[name] = found ? old : null; scope.Declarations.Add(slot); _variables[name] = slot;
            return slot;
        }
        private void ScopedStatement()
        {
            EnterScope();
            try { Statement(); } finally { ExitScope(); }
        }
        private void Statement()
        {
            if (++_depth > 64) throw Error("Formula nesting exceeds 64 levels");
            try
            {
                if (Take(";")) return;
                if (Take("{"))
                {
                    EnterScope();
                    try
                    {
                        while (!Take("}"))
                        {
                            if (Current.Text == "<end>") throw Error("Unclosed statement block");
                            Statement();
                        }
                    }
                    finally { ExitScope(); }
                    return;
                }
                if (Take("if"))
                {
                    Expect("("); Expression(); Expect(")");
                    var otherwise = Emit(Op.JumpFalse); var entry = CaptureFlow();
                    ScopedStatement(); var yes = CaptureFlow();
                    if (Take("else"))
                    {
                        var end = Emit(Op.Jump); Patch(otherwise); RestoreFlow(entry);
                        ScopedStatement(); var no = CaptureFlow(); Patch(end); MergeFlows([yes, no]);
                    }
                    else { Patch(otherwise); MergeFlows([yes, entry]); }
                    return;
                }
                if (Take("while")) { WhileStatement(); return; }
                if (Take("for")) { ForStatement(); return; }
                if (Take("do")) { DoStatement(); return; }
                if (Current.Text is "break" or "continue")
                {
                    if (_loops.Count == 0) throw Error("Loop control used outside a loop");
                    var breaking = Take("break"); if (!breaking) Expect("continue");
                    var jump = Emit(Op.Jump); var state = CaptureFlow();
                    if (breaking) _loops.Peek().Breaks.Add((jump, state)); else _loops.Peek().Continues.Add((jump, state));
                    _reachable = false; Expect(";"); return;
                }
                SimpleStatement();
                if (!Take(";") && Current.Text != "<end>") throw Error("Expected semicolon");
            }
            finally { _depth--; }
        }
        private void SimpleStatement()
        {
            var declaration = Take("float64") || Take("double");
            var prefix = Current.Text is "++" or "--" ? Current.Text : null;
            if (prefix is not null) _at++;
            var name = Current.Text;
            if (!ValidVariable(name)) throw Error("Expected a scalar assignment identifier");
            _at++;
            int slot;
            if (declaration) slot = NewVariable(name, true);
            else if (!_variables.TryGetValue(name, out slot)) slot = NewVariable(name, false);
            if (_readOnly.Contains(slot)) throw Error("Input variables are read-only");
            if (declaration) { _assigned.Remove(slot); Emit(Op.Unset, slot); }
            var operation = prefix ?? Current.Text;
            if (operation is "++" or "--")
            {
                if (!_assigned.Contains(slot)) throw Error($"Unassigned variable '{name}'");
                if (prefix is null) _at++;
                Emit(Op.Load, slot); EmitConstant(1); Emit(operation == "++" ? Op.Add : Op.Subtract);
            }
            else if (operation is "=" or "+=" or "-=" or "*=" or "/=" or "%=")
            {
                _at++;
                if (operation != "=")
                {
                    if (!_assigned.Contains(slot)) throw Error($"Unassigned variable '{name}'");
                    Emit(Op.Load, slot);
                }
                Expression();
                if (operation != "=") Emit(operation switch
                {
                    "+=" => Op.Add, "-=" => Op.Subtract, "*=" => Op.Multiply,
                    "/=" => Op.Divide, "%=" => Op.Modulo, _ => throw Error("Invalid assignment")
                });
            }
            else if (declaration) return;
            else throw Error("Expected assignment or increment");
            Emit(Op.Store, slot); _assigned.Add(slot);
        }
        private bool? ConstantCondition(int start) => _code.Count == start + 1 && _code[start].Code == Op.Constant ? _code[start].Number != 0 : null;
        private void WhileStatement()
        {
            var condition = _code.Count;
            Expect("("); Expression(); Expect(")"); var constant = ConstantCondition(condition);
            var end = Emit(Op.JumpFalse); var entry = CaptureFlow(); var loop = new Loop(); _loops.Push(loop);
            if (constant == false) _reachable = false;
            ScopedStatement(); Emit(Op.Jump, condition); Patch(end); _loops.Pop();
            foreach (var p in loop.Breaks) Patch(p.Jump);
            foreach (var p in loop.Continues) Patch(p.Jump, condition);
            MergeFlows(loop.Breaks.Select(p => p.State).Concat(constant == true ? [] : new[] { entry }));
        }
        private void DoStatement()
        {
            var body = _code.Count; var loop = new Loop(); _loops.Push(loop);
            ScopedStatement();
            var tail = CaptureFlow(); MergeFlows(loop.Continues.Select(p => p.State).Append(tail));
            var condition = _code.Count;
            Expect("while"); Expect("("); Expression(); Expect(")"); Expect(";");
            var constant = ConstantCondition(condition); var afterCondition = CaptureFlow();
            var end = Emit(Op.JumpFalse); Emit(Op.Jump, body); Patch(end); _loops.Pop();
            foreach (var p in loop.Breaks) Patch(p.Jump);
            foreach (var p in loop.Continues) Patch(p.Jump, condition);
            MergeFlows(loop.Breaks.Select(p => p.State).Concat(constant == true ? [] : new[] { afterCondition }));
        }
        private void ForStatement()
        {
            EnterScope();
            try
            {
                Expect("("); if (Current.Text != ";") SimpleStatement(); Expect(";");
                var condition = _code.Count;
                if (Current.Text == ";") EmitConstant(1); else Expression();
                var constant = ConstantCondition(condition); Expect(";");
                var end = Emit(Op.JumpFalse); var entry = CaptureFlow();
                // Delay increment compilation until body/continue-path assignment facts are known.
                var incrementStart = _at; var parentheses = 0;
                while (Current.Text != ")" || parentheses > 0)
                {
                    if (Current.Text == "<end>") throw Error("Unclosed for header");
                    if (Current.Text == "(") parentheses++;
                    if (Current.Text == ")") parentheses--;
                    _at++;
                }
                var incrementEnd = _at; Expect(")"); var loop = new Loop(); _loops.Push(loop);
                if (constant == false) _reachable = false;
                ScopedStatement(); var afterBody = _at; var tail = CaptureFlow();
                MergeFlows(loop.Continues.Select(p => p.State).Append(tail));
                var increment = _code.Count; _at = incrementStart;
                if (_at != incrementEnd) SimpleStatement();
                if (_at != incrementEnd) throw Error("Invalid for increment");
                _at = afterBody; Emit(Op.Jump, condition); Patch(end); _loops.Pop();
                foreach (var p in loop.Breaks) Patch(p.Jump);
                foreach (var p in loop.Continues) Patch(p.Jump, increment);
                MergeFlows(loop.Breaks.Select(p => p.State).Concat(constant == true ? [] : new[] { entry }));
            }
            finally { ExitScope(); }
        }
    }
}
