using LabSpace.Core;
using System.Globalization;
using System.Text.Json;

namespace LabSpace.Dataflow;

public sealed record CompiledFrame(string Id, string Label, CompiledGraph Graph);

/// <summary>Validated frame dispatch. Selection is compiled once, independent of mutable UI labels.</summary>
public sealed class FrameProgram
{
    private readonly List<(int Index, double Min, double Max)> _numbers = [];
    private readonly Dictionary<string, int> _strings;
    private readonly Dictionary<bool, int> _booleans = [];
    private int _default = -1;
    public IReadOnlyList<CompiledFrame> Frames { get; }
    public bool IsSequence { get; }
    public ValueKind SelectorType { get; }
    internal FrameProgram(Node node, IReadOnlyList<CompiledFrame> frames)
    {
        Frames = frames; IsSequence = node.Kind == "sequence"; SelectorType = node.Contract!.SelectorType;
        _strings = new(node.Contract.CaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        if (IsSequence) return;
        for (var i = 0; i < node.Frames.Count; i++)
        {
            var frame = node.Frames[i];
            if (frame.IsDefault)
            {
                if (_default >= 0) throw new ArgumentException("Exactly one default Case is allowed.");
                if (SelectorType is ValueKind.Boolean or ValueKind.Error) throw new ArgumentException("Boolean and error Cases use two explicit branches, not a default.");
                _default = i;
            }
            foreach (var token in Tokens(frame.Selector))
            {
                if (SelectorType == ValueKind.String)
                {
                    var value = token.StartsWith('"') ? JsonSerializer.Deserialize<string>(token) ?? "" : token;
                    if (_strings.TryGetValue(value, out var prior) && prior != i) throw new ArgumentException("A string selector belongs to multiple Cases: " + token);
                    _strings[value] = i;
                }
                else if (SelectorType is ValueKind.Boolean or ValueKind.Error)
                {
                    var positive = SelectorType == ValueKind.Error ? "Error" : "True";
                    var negative = SelectorType == ValueKind.Error ? "No Error" : "False";
                    bool value;
                    if (token.Equals(positive, StringComparison.OrdinalIgnoreCase)) value = true;
                    else if (token.Equals(negative, StringComparison.OrdinalIgnoreCase)) value = false;
                    else throw new ArgumentException("Expected selector " + positive + " or " + negative + ".");
                    if (!_booleans.TryAdd(value, i)) throw new ArgumentException("Duplicate Boolean/error Case selector.");
                }
                else
                {
                    var range = token.Split("..", StringSplitOptions.None);
                    if (range.Length > 2 || range.All(string.IsNullOrWhiteSpace)) throw new ArgumentException("Invalid numeric Case range.");
                    var min = string.IsNullOrWhiteSpace(range[0]) ? double.NegativeInfinity : Integer(range[0]);
                    var max = range.Length == 1 ? min : string.IsNullOrWhiteSpace(range[1]) ? double.PositiveInfinity : Integer(range[1]);
                    if (min > max || _numbers.Any(r => r.Index != i && min <= r.Max && max >= r.Min)) throw new ArgumentException("Numeric Case ranges are reversed or overlap another Case.");
                    _numbers.Add((i, min, max));
                }
            }
            if (!frame.IsDefault && string.IsNullOrWhiteSpace(frame.Selector)) throw new ArgumentException("A non-default Case needs at least one selector.");
        }
        if (SelectorType is ValueKind.Boolean or ValueKind.Error)
        {
            if (node.Frames.Count != 2 || _booleans.Count != 2 || _booleans[true] == _booleans[false]) throw new ArgumentException("Boolean/error Case requires exactly two distinct branches.");
        }
        else if (_default < 0) throw new ArgumentException("Numeric and string Cases require a default branch.");
    }
    public int Select(Value selector)
    {
        if (IsSequence) return 0;
        if (selector.Kind != SelectorType) throw new ArgumentException("Case selector type mismatch.");
        if (SelectorType is ValueKind.Boolean or ValueKind.Error) return _booleans[SelectorType == ValueKind.Error ? selector.Error.Status : selector.Boolean];
        if (SelectorType == ValueKind.String) return _strings.GetValueOrDefault(selector.Text, _default);
        var number = Math.Round(selector.Number, MidpointRounding.ToEven);
        foreach (var r in _numbers) if (number >= r.Min && number <= r.Max) return r.Index;
        return _default;
    }
    private static double Integer(string text)
    {
        if (!double.TryParse(text.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) || Math.Abs(value) > 9007199254740991 || value != Math.Truncate(value))
            throw new ArgumentException("Case range endpoints must be exact integers within ±(2^53−1).");
        return value;
    }
    private static IEnumerable<string> Tokens(string text)
    {
        var start = 0; var quote = false; var escape = false;
        for (var i = 0; i <= text.Length; i++)
        {
            if (i == text.Length || (!quote && text[i] == ','))
            {
                if (quote) throw new ArgumentException("Unterminated quoted Case selector.");
                var token = text[start..i].Trim();
                if (token.Length > 0) yield return token;
                else if (text.Length > 0) throw new ArgumentException("Empty Case selector between commas.");
                start = i + 1; continue;
            }
            if (escape) { escape = false; continue; }
            if (quote && text[i] == '\\') escape = true;
            else if (text[i] == '"') quote = !quote;
        }
    }
}
