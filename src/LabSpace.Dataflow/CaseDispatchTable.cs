using System.Globalization;
using System.Text.Json;
using LabSpace.Core;

namespace LabSpace.Dataflow;

/// <summary>Validated immutable case selectors. Numeric ranges are inclusive; string ranges are half-open.</summary>
public sealed class CaseDispatchTable
{
    private sealed record Pattern(int Frame, long Low, long High, string? Start, string? End, bool Exact, bool? Status);
    private readonly Pattern[] _patterns;
    private readonly ValueKind _type;
    private readonly StringComparer _strings;
    private readonly int _fallback;
    private CaseDispatchTable(ValueKind type, Pattern[] patterns, int fallback, bool ignoreCase)
    {
        _type = type; _patterns = patterns; _fallback = fallback;
        _strings = ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    }
    public static CaseDispatchTable Compile(Node node)
    {
        if (node.DataType is not (ValueKind.Number or ValueKind.String or ValueKind.Boolean or ValueKind.Error))
            throw new ArgumentException("A case selector must be numeric, Boolean, string or error cluster.");
        if (node.Frames.Count is < 1 or > 64) throw new ArgumentException("A Case Structure requires 1–64 cases.");
        var patterns = new List<Pattern>(); var fallback = -1;
        for (var frame = 0; frame < node.Frames.Count; frame++)
        {
            var item = node.Frames[frame];
            if (item.IsDefault)
            {
                if (fallback >= 0) throw new ArgumentException("Only one case can be the default.");
                fallback = frame;
            }
            foreach (var label in Split(item.Label))
            {
                if (label.Length == 0) continue;
                if (node.DataType == ValueKind.Boolean)
                {
                    if (!bool.TryParse(label, out var boolean)) throw new ArgumentException("Boolean cases use True or False labels.");
                    patterns.Add(new(frame, boolean ? 1 : 0, boolean ? 1 : 0, null, null, false, null));
                }
                else if (node.DataType == ValueKind.String)
                {
                    var range = RangeAt(label);
                    patterns.Add(range < 0
                        ? new(frame, 0, 0, Unquote(label), null, true, null)
                        : new(frame, 0, 0, label[..range].Trim().Length == 0 ? null : Unquote(label[..range]), label[(range + 2)..].Trim().Length == 0 ? null : Unquote(label[(range + 2)..]), false, null));
                }
                else
                {
                    var numeric = label; bool? status = null;
                    if (node.DataType == ValueKind.Error)
                    {
                        if (label.Equals("No Error", StringComparison.OrdinalIgnoreCase))
                        { patterns.Add(new(frame, int.MinValue, int.MaxValue, null, null, false, false)); continue; }
                        if (numeric.StartsWith("Error", StringComparison.OrdinalIgnoreCase)) numeric = numeric[5..].Trim();
                        status = true;
                        if (numeric.Length == 0) numeric = "..";
                    }
                    var range = numeric.IndexOf("..", StringComparison.Ordinal);
                    var low = range < 0 ? Integer(numeric) : Bound(numeric[..range], int.MinValue);
                    var high = range < 0 ? low : Bound(numeric[(range + 2)..], int.MaxValue);
                    if (low > high) throw new ArgumentException("A numeric case range must be ascending.");
                    patterns.Add(new(frame, low, high, null, null, false, status));
                }
                if (patterns.Count > 256) throw new ArgumentException("At most 256 case selector terms are supported.");
            }
            if (!item.IsDefault && string.IsNullOrWhiteSpace(item.Label)) throw new ArgumentException("A non-default case requires a selector label.");
        }
        var table = new CaseDispatchTable(node.DataType, patterns.ToArray(), fallback, node.Parameter("caseInsensitive", 0) != 0);
        foreach (var p in patterns)
            if (node.DataType == ValueKind.String && !p.Exact && p.Start is not null && p.End is not null && table._strings.Compare(p.Start, p.End) >= 0)
                throw new ArgumentException("A string range must be ascending and excludes its upper bound.");
        for (var a = 0; a < patterns.Count; a++)
            for (var b = a + 1; b < patterns.Count; b++)
                if (patterns[a].Frame != patterns[b].Frame && table.Overlaps(patterns[a], patterns[b]))
                    throw new ArgumentException($"Case labels overlap between frames {patterns[a].Frame} and {patterns[b].Frame}.");
        if (fallback < 0)
        {
            if (node.DataType == ValueKind.Boolean && new[] { 0L, 1L }.All(v => patterns.Any(p => p.Low == v))) return table;
            if (node.DataType == ValueKind.Error && patterns.Any(p => p.Status == false) && Covers(patterns.Where(p => p.Status == true))) return table;
            if (node.DataType == ValueKind.Number && Covers(patterns)) return table;
            throw new ArgumentException("Cases must cover every selector value or include one default case.");
        }
        return table;
    }
    private static bool Covers(IEnumerable<Pattern> patterns)
    {
        long next = int.MinValue;
        foreach (var p in patterns.OrderBy(p => p.Low))
        {
            if (p.Low > next) return false;
            next = Math.Max(next, p.High + 1);
        }
        return next > int.MaxValue;
    }
    public int Select(Value value)
    {
        if (value.Kind != _type) throw new ArgumentException("Case selector type does not match its compiled contract.");
        var numeric = _type switch
        {
            ValueKind.Boolean => value.Boolean ? 1d : 0d,
            ValueKind.Error => value.Error.Code,
            ValueKind.Number => Math.Round(value.Number, MidpointRounding.ToEven),
            _ => 0d
        };
        if (_type == ValueKind.Number && (numeric < int.MinValue || numeric > int.MaxValue))
            throw new ArgumentOutOfRangeException(nameof(value), "Numeric case selectors must round into the signed 32-bit range.");
        foreach (var p in _patterns)
            if (_type == ValueKind.String ? Matches(p, value.Text) : (p.Status is null || p.Status == value.Error.Status) && numeric >= p.Low && numeric <= p.High)
                return p.Frame;
        if (_fallback >= 0) return _fallback;
        throw new InvalidOperationException("The compiled Case Structure has no matching frame.");
    }
    private bool Matches(Pattern p, string value) => p.Exact ? _strings.Equals(p.Start, value)
        : (p.Start is null || _strings.Compare(value, p.Start) >= 0) && (p.End is null || _strings.Compare(value, p.End) < 0);
    private bool Overlaps(Pattern a, Pattern b)
    {
        if (_type != ValueKind.String) return a.Status == b.Status && a.Low <= b.High && b.Low <= a.High;
        if (a.Exact) return Matches(b, a.Start!);
        if (b.Exact) return Matches(a, b.Start!);
        return (a.End is null || b.Start is null || _strings.Compare(b.Start, a.End) < 0)
            && (b.End is null || a.Start is null || _strings.Compare(a.Start, b.End) < 0);
    }
    private static long Bound(string text, long fallback) => string.IsNullOrWhiteSpace(text) ? fallback : Integer(text);
    private static long Integer(string text) => int.TryParse(text.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n)
        ? n : throw new ArgumentException("Case labels require signed 32-bit integers, not floating-point literals.");
    private static string Unquote(string text)
    {
        text = text.Trim();
        if (!text.StartsWith('"')) return text;
        return JsonSerializer.Deserialize<string>(text) ?? "";
    }
    private static IEnumerable<string> Split(string label)
    {
        if (label.Length > 4096) throw new ArgumentException("Case selector label exceeds 4,096 characters.");
        var start = 0; var quoted = false; var escaped = false;
        for (var i = 0; i < label.Length; i++)
        {
            var c = label[i];
            if (escaped) { escaped = false; continue; }
            if (c == '\\' && quoted) { escaped = true; continue; }
            if (c == '"') quoted = !quoted;
            if (c != ',' || quoted) continue;
            yield return label[start..i].Trim(); start = i + 1;
        }
        if (quoted || escaped) throw new ArgumentException("Unterminated quoted case selector.");
        yield return label[start..].Trim();
    }
    private static int RangeAt(string text)
    {
        var quoted = false; var escaped = false;
        for (var i = 0; i < text.Length - 1; i++)
        {
            if (escaped) { escaped = false; continue; }
            if (text[i] == '\\' && quoted) { escaped = true; continue; }
            if (text[i] == '"') quoted = !quoted;
            if (!quoted && text[i] == '.' && text[i + 1] == '.') return i;
        }
        return -1;
    }
}
