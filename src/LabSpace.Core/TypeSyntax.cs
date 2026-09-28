using System.Text.Json;

namespace LabSpace.Core;

/// <summary>Round-trippable compact type syntax for connector, cluster and array editors.</summary>
public static class TypeSyntax
{
    public static string Format(LabType type) => type.Kind switch
    {
        ValueKind.Array => Format(type.Element!) + "[" + new string(',', type.Rank - 1) + "]",
        ValueKind.Cluster => "{" + string.Join(", ", type.Fields.Select(f => JsonSerializer.Serialize(f.Name) + ": " + Format(f.Type))) + "}",
        ValueKind.Enum => "enum(" + string.Join(", ", type.Labels.Select(l => JsonSerializer.Serialize(l))) + ")",
        ValueKind.Number => "DBL", ValueKind.Single => "SGL", ValueKind.Complex => "CDB",
        ValueKind.Boolean => "Boolean", ValueKind.String => "String", ValueKind.Error => "Error", ValueKind.Variant => "Variant",
        ValueKind.Waveform => "Waveform", _ => (type.IsUnsigned ? "U" : "I") + type.BitWidth
    };
    public static LabType Parse(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Length > 16384) throw new ArgumentException("Type expression exceeds 16 KiB.");
        var parser = new Parser(source); var result = parser.Type(0); parser.White();
        if (!parser.End) throw parser.Error("Unexpected trailing input.");
        result.Validate(); return result;
    }
    private sealed class Parser(string text)
    {
        private int _position;
        public bool End => _position >= text.Length;
        public void White() { while (!End && char.IsWhiteSpace(text[_position])) _position++; }
        public ArgumentException Error(string message) => new($"{message} At character {_position + 1}.");
        private bool Take(char value) { White(); if (End || text[_position] != value) return false; _position++; return true; }
        private void Require(char value) { if (!Take(value)) throw Error($"Expected '{value}'."); }
        private string Name()
        {
            White(); var start = _position;
            if (Take('"'))
            {
                var escaped = false;
                while (!End)
                {
                    var ch = text[_position++];
                    if (ch == '"' && !escaped) return JsonSerializer.Deserialize<string>(text[start.._position])!;
                    escaped = ch == '\\' && !escaped;
                }
                throw Error("Unterminated name.");
            }
            while (!End && (char.IsLetterOrDigit(text[_position]) || text[_position] is '_' or '-')) _position++;
            if (_position == start) throw Error("Expected a type or field name.");
            return text[start.._position];
        }
        public LabType Type(int depth)
        {
            if (depth > 8) throw Error("Type nesting exceeds eight levels.");
            LabType type;
            if (Take('{'))
            {
                var fields = new List<TypeField>();
                if (!Take('}')) do
                {
                    var name = Name(); Require(':'); fields.Add(new(name, Type(depth + 1)));
                    if (fields.Count > 64) throw Error("Cluster has more than 64 fields.");
                    if (Take('}')) break; Require(',');
                } while (true);
                type = LabType.Cluster(fields.ToArray());
            }
            else
            {
                var name = Name().ToUpperInvariant();
                if (name == "ENUM")
                {
                    Require('('); var labels = new List<string>();
                    do { labels.Add(Name()); if (labels.Count > 1024) throw Error("Too many enum labels."); if (Take(')')) break; Require(','); } while (true);
                    type = LabType.Enumeration(labels.ToArray());
                }
                else type = LabType.Scalar(name switch
                {
                    "DBL" or "DOUBLE" or "NUMBER" => ValueKind.Number, "SGL" or "SINGLE" => ValueKind.Single,
                    "CDB" or "COMPLEX" => ValueKind.Complex, "BOOLEAN" or "BOOL" => ValueKind.Boolean,
                    "STRING" => ValueKind.String, "ERROR" => ValueKind.Error, "VARIANT" => ValueKind.Variant,
                    "WAVEFORM" => ValueKind.Waveform,
                    "I8" or "INT8" => ValueKind.Int8, "U8" or "UINT8" => ValueKind.UInt8,
                    "I16" or "INT16" => ValueKind.Int16, "U16" or "UINT16" => ValueKind.UInt16,
                    "I32" or "INT32" => ValueKind.Int32, "U32" or "UINT32" => ValueKind.UInt32,
                    "I64" or "INT64" => ValueKind.Int64, "U64" or "UINT64" => ValueKind.UInt64,
                    _ => throw Error("Unknown type '" + name + "'.")
                });
            }
            if (Take('[')) { var rank = 1; while (Take(',')) rank++; Require(']'); type = LabType.Array(type, rank); }
            return type;
        }
    }
}
