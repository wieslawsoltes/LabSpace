using System.Globalization;
using System.Text.Json;
using LabSpace.Core;

namespace LabSpace.Dataflow;

public sealed partial class DataflowRuntime
{
    private Value EvaluateAdvanced(CompiledNode node, Func<string, Value> input, ExecutionBudget budget,
        IReadOnlyDictionary<string, Value> arguments, string path, Dictionary<string, Value> pending, Action<string, Value> publish)
    {
        var model = node.Model; var type = node.Definition.DataType;
        switch (model.Kind)
        {
            case "typed-constant": case "typed-control": return node.Literal(type);
            case "typed-indicator": case "tunnel-out": case "shift-write": return input("x");
            case "tunnel-in": return arguments.TryGetValue(model.Text, out var value) ? value : Value.Default(type);
            case "shift-read": return arguments.TryGetValue("$shift:" + model.Text + ":" + (int)model.Parameter("element", 0), out var previous) ? previous : Value.Default(type);
            case "iteration": return arguments.GetValueOrDefault("$i") ?? Value.Signed(0, ValueKind.Int32);
            case "loop-count": return arguments.GetValueOrDefault("$N") ?? Value.Signed(0, ValueKind.Int32);
            case "convert": return ValueConversion.Convert(input("x"), type);
            case "bundle": return Value.Cluster(type, type.Fields.Select(f => KeyValuePair.Create(f.Name, input(f.Name))));
            case "unbundle":
                var cluster = input("x"); foreach (var field in cluster.Type.Fields) publish(field.Name, cluster.Fields[field.Name]);
                return cluster.Fields[cluster.Type.Fields[0].Name];
            case "to-variant": return Value.Variant(input("x"));
            case "from-variant": return ValueConversion.Convert(input("x").Contained ?? throw new ArgumentException("Variant is empty."), type);
            case "error": return Value.Error(input("status").Boolean, (int)input("code").Integer, input("source").Text);
            case "error-fields": var error = input("x"); foreach (var field in error.Fields) publish(field.Key, field.Value); return error.Fields["status"];
            case "clear-error": return Value.Error();
            case "merge-errors": var x = input("x"); var y = input("y"); return x.Boolean ? x : y.Boolean ? y : x.Integer != 0 ? x : y;
            case "array-build": return Value.Collect(node.Definition.Inputs[0].DataType, new[] { input("x"), input("y") });
            case "array-index": return input("x").IndexFirst((int)input("index").Integer);
            case "array-replace":
                var original = input("x"); var replacement = input("value"); var index = (int)input("index").Integer;
                if (index < 0 || index >= original.Shape[0]) return original;
                var stride = original.Type.Rank == 1 ? 1 : Value.ShapeCount(original.Shape.Skip(1));
                if (original.Type.Rank > 1 && !replacement.Shape.SequenceEqual(original.Shape.Skip(1))) throw new ArgumentException("Replacement row shape does not match.");
                return Value.Array(original.Type.Element!, original.Shape, Enumerable.Range(0, original.Count).Select(i => i / stride == index ? original.Type.Rank == 1 ? replacement : replacement.ElementAt(i % stride) : original.ElementAt(i)));
            case "array-concat":
                var left = input("x"); var right = input("y"); if (!left.Shape.Skip(1).SequenceEqual(right.Shape.Skip(1))) throw new ArgumentException("Concatenated array row shapes do not match.");
                return Value.Array(type.Element!, new[] { checked(left.Shape[0] + right.Shape[0]) }.Concat(left.Shape.Skip(1)), left.EnumerateElements().Concat(right.EnumerateElements()));
            case "array-reverse":
                var reverse = input("x"); var width = reverse.Type.Rank == 1 ? 1 : Value.ShapeCount(reverse.Shape.Skip(1));
                return Value.Array(type.Element!, reverse.Shape, Enumerable.Range(0, reverse.Count).Select(i => reverse.ElementAt((reverse.Shape[0] - 1 - i / width) * width + i % width)));
            case "array-transpose":
                var matrix = input("x"); if (type.Rank != 2) throw new ArgumentException("Transpose requires a rank-two array.");
                return Value.Array(type.Element!, new[] { matrix.Shape[1], matrix.Shape[0] }, Enumerable.Range(0, matrix.Count).Select(i => matrix.ElementAt(i % matrix.Shape[0] * matrix.Shape[1] + i / matrix.Shape[0])));
            case "array-reshape":
                var shape = JsonSerializer.Deserialize<int[]>(string.IsNullOrWhiteSpace(model.Text) ? "[0,0]" : model.Text) ?? throw new ArgumentException("Shape is missing.");
                if (shape.Length != type.Rank) throw new ArgumentException("Shape rank does not match the output wire type.");
                var source = input("x"); return Value.Array(type.Element!, shape, Enumerable.Range(0, Value.ShapeCount(shape)).Select(source.ElementAt));
            case "array-sort":
                if (type.Rank != 1 || !type.Element!.IsNumeric) throw new ArgumentException("Sort requires a rank-one numeric array.");
                var data = input("x"); var sorted = data.EnumerateElements().OrderBy(v => v, Comparer<Value>.Create((a, b) => type.Element.IsInteger ? ValueConversion.ExactInteger(a).CompareTo(ValueConversion.ExactInteger(b)) : a.Number.CompareTo(b.Number)));
                return Value.Array(type.Element, data.Shape, sorted);
            case "for-loop": case "while-loop": case "case-typed": case "subvi-typed": return ExecuteStructure(node, input, budget, path, pending, publish);
            default: throw new NotSupportedException("No typed kernel for " + model.Kind);
        }
    }

    private Value ExecuteStructure(CompiledNode compiled, Func<string, Value> input, ExecutionBudget budget,
        string parentPath, Dictionary<string, Value> pending, Action<string, Value> publish)
    {
        var node = compiled.Model; var contract = node.Contract!;
        var isFor = node.Kind == "for-loop"; var isWhile = node.Kind == "while-loop";
        var path = parentPath + "/" + node.Id;
        var supplied = contract.Inputs.ToDictionary(t => t.Name, t => input(t.Name), StringComparer.Ordinal);
        var count = isWhile ? 10000 : 1;
        if (isFor)
        {
            count = compiled.Sources.ContainsKey("count") || node.Parameters.ContainsKey("count") ? Integer(Math.Max(0, input("count").Integer), 0, 1000000, "count") : int.MaxValue;
            foreach (var tunnel in contract.Inputs.Where(t => t.Mode == TunnelMode.Indexing)) count = Math.Min(count, supplied[tunnel.Name].Shape[0]);
            if (count == int.MaxValue) throw new ArgumentException("For Loop has no iteration bound.");
        }
        var registers = new Dictionary<string, Value[]>(StringComparer.Ordinal);
        foreach (var register in contract.Registers)
        {
            var initial = register.Initialized && compiled.Sources.ContainsKey("init:" + register.Name) ? input("init:" + register.Name) : ValueLiteral.Parse(register.Type, register.InitialValue);
            var stack = new Value[register.Depth];
            for (var slot = 0; slot < stack.Length; slot++)
            {
                var key = path + "/$register:" + register.Name + ":" + slot;
                stack[slot] = register.Initialized ? initial : pending.GetValueOrDefault(key) ?? _feedback.GetValueOrDefault(key) ?? Value.Default(register.Type);
            }
            registers[register.Name] = stack;
        }
        var results = contract.Outputs.ToDictionary(t => t.Name, t => Value.Default(t.OutsideType), StringComparer.Ordinal);
        var collected = contract.Outputs.Where(t => t.Mode != TunnelMode.LastValue).ToDictionary(t => t.Name, _ => new List<Value>(), StringComparer.Ordinal);
        var elementCounts = collected.Keys.ToDictionary(k => k, _ => 0, StringComparer.Ordinal);
        var body = node.Kind == "case-typed" && !input("selector").Boolean ? compiled.Alternative! : compiled.Body!;
        var branchPath = node.Kind == "case-typed" ? path + (input("selector").Boolean ? "/true" : "/false") : path;
        var stopped = false;
        for (var iteration = 0; iteration < count; iteration++)
        {
            budget.Consume(); // Bounds empty loops and observes cancellation even before their first node.
            var args = new Dictionary<string, Value>(StringComparer.Ordinal) { ["$i"] = Value.Signed(iteration, ValueKind.Int32), ["$N"] = Value.Signed(count, ValueKind.Int32) };
            foreach (var tunnel in contract.Inputs) args[tunnel.Name] = tunnel.Mode == TunnelMode.Indexing ? supplied[tunnel.Name].IndexFirst(iteration) : supplied[tunnel.Name];
            foreach (var (name, stack) in registers) for (var slot = 0; slot < stack.Length; slot++) args["$shift:" + name + ":" + slot] = stack[slot];
            var frame = new ExecutionFrame(this, body, budget, args, branchPath, pending);
            while (!frame.Completed) frame.Step();
            foreach (var tunnel in contract.Outputs)
            {
                var hasTerminal = body.TunnelOutputs.TryGetValue(tunnel.Name, out var terminal);
                var value = hasTerminal ? frame.Values[terminal!.Model.Id] : Value.Default(tunnel.Type);
                var include = !tunnel.Conditional || (hasTerminal && terminal!.Sources.TryGetValue("include", out var condition) && frame.Read(condition).Boolean);
                if (!include) continue;
                if (tunnel.Mode == TunnelMode.LastValue) results[tunnel.Name] = value;
                else
                {
                    var size = value.Kind == ValueKind.Array ? value.Count : 1;
                    elementCounts[tunnel.Name] += size;
                    if (elementCounts[tunnel.Name] > 65536 || collected[tunnel.Name].Count == 65536) throw new ExecutionLimitException("Collected tunnel exceeds 65,536 elements.");
                    collected[tunnel.Name].Add(value);
                }
            }
            // Read all outputs before advancing any history. A source can safely depend on another register.
            var next = contract.Registers.Select(r => frame.Values[body.RegisterOutputs[r.Name].Model.Id]).ToArray();
            for (var r = 0; r < contract.Registers.Count; r++)
            {
                var stack = registers[contract.Registers[r].Name];
                for (var slot = stack.Length - 1; slot > 0; slot--) stack[slot] = stack[slot - 1]; stack[0] = next[r];
            }
            if (isWhile && frame.Values[body.Stop!].Boolean == contract.StopWhenTrue) { stopped = true; break; }
        }
        if (isWhile && !stopped) throw new ExecutionLimitException("While Loop did not terminate within 10,000 iterations.");
        foreach (var tunnel in contract.Outputs)
        {
            if (tunnel.Mode == TunnelMode.Indexing) results[tunnel.Name] = Value.Collect(tunnel.Type, collected[tunnel.Name]);
            else if (tunnel.Mode == TunnelMode.Concatenating) results[tunnel.Name] = Value.Array(tunnel.Type.Element!, new[] { elementCounts[tunnel.Name] }, collected[tunnel.Name].SelectMany(v => v.EnumerateElements()));
            publish(tunnel.Name, results[tunnel.Name]);
        }
        foreach (var register in contract.Registers)
        {
            var stack = registers[register.Name]; var name = "shift:" + register.Name; results[name] = stack[0]; publish(name, stack[0]);
            if (!register.Initialized) for (var slot = 0; slot < stack.Length; slot++) pending[path + "/$register:" + register.Name + ":" + slot] = stack[slot];
        }
        return results[compiled.Definition.OutputPorts[0].Name];
    }
}
