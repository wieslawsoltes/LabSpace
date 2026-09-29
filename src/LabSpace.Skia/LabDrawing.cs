using LabSpace.Core;
using SkiaSharp;

namespace LabSpace.Skia;

public sealed class LabFonts : IDisposable
{
    public SKTypeface Regular { get; private set; } = SKTypeface.Default;
    private bool _owns;
    public void Load(Stream stream) { var typeface = SKTypeface.FromStream(stream) ?? throw new InvalidDataException("The application font could not be loaded."); if (_owns) Regular.Dispose(); Regular = typeface; _owns = true; }
    public void Dispose() { if (_owns) Regular.Dispose(); _owns = false; }
}

public sealed class LabDrawing(LabFonts fonts) : IDisposable
{
    private readonly SKPaint _fill = new() { IsAntialias = true };
    private readonly SKPaint _stroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
    private readonly Dictionary<float, SKFont> _fonts = [];
    public static SKColor Color(string hex) => SKColor.Parse(hex);
    public static SKColor WireColor(ValueKind kind) => kind switch { ValueKind.Number => Color("#DE741B"), ValueKind.Boolean => Color("#288624"), ValueKind.String => Color("#CA257E"), ValueKind.Array => Color("#C9680B"), ValueKind.Error => Color("#9C8D21"), ValueKind.Complex => Color("#D66C20"), _ => Color("#875332") };
    public SKFont Font(float size) { if (!_fonts.TryGetValue(size, out var font)) _fonts[size] = font = new(fonts.Regular, size) { Edging = SKFontEdging.SubpixelAntialias, Subpixel = true }; return font; }
    public void Rect(SKCanvas c, SKRect rect, SKColor color) { _fill.Color = color; c.DrawRect(rect, _fill); }
    public void Rect(SKCanvas c, float x, float y, float width, float height, string color) => Rect(c, new(x, y, x + width, y + height), Color(color));
    public void Border(SKCanvas c, SKRect rect, SKColor color, float width = 1) { _stroke.Color = color; _stroke.StrokeWidth = width; c.DrawRect(rect, _stroke); }
    public void Line(SKCanvas c, float x, float y, float x2, float y2, SKColor color, float width = 1) { _stroke.Color = color; _stroke.StrokeWidth = width; c.DrawLine(x, y, x2, y2, _stroke); }
    public void Path(SKCanvas c, SKPath path, SKColor color, float width = 1, bool fill = false)
    {
        if (fill) { _fill.Color = color; c.DrawPath(path, _fill); } else { _stroke.Color = color; _stroke.StrokeWidth = width; c.DrawPath(path, _stroke); }
    }
    public void Circle(SKCanvas c, float x, float y, float radius, SKColor color, bool fill = true)
    {
        if (fill) { _fill.Color = color; c.DrawCircle(x, y, radius, _fill); } else { _stroke.Color = color; _stroke.StrokeWidth = 1; c.DrawCircle(x, y, radius, _stroke); }
    }
    public void Text(SKCanvas c, string text, float x, float y, float size = 13, string color = "#202020", bool center = false)
    {
        if (text.Length > 512) text = text[..509] + "…";
        var f = Font(size); _fill.Color = Color(color);
        c.DrawText(text, x, y, center ? SKTextAlign.Center : SKTextAlign.Left, f, _fill);
    }
    public void Bevel(SKCanvas c, SKRect r, string background = "#C6C6C6", bool inset = false)
    {
        Rect(c, r, Color(background)); Border(c, r, Color("#777777"));
        var a = Color(inset ? "#777777" : "#F5F5F5"); var b = Color(inset ? "#F1F1F1" : "#777777");
        Line(c, r.Left + 1, r.Top + 1, r.Right - 1, r.Top + 1, a); Line(c, r.Left + 1, r.Top + 1, r.Left + 1, r.Bottom - 1, a);
        Line(c, r.Left + 1, r.Bottom - 1, r.Right - 1, r.Bottom - 1, b); Line(c, r.Right - 1, r.Top + 1, r.Right - 1, r.Bottom - 1, b);
    }
    public void Selection(SKCanvas c, SKRect r)
    {
        r.Inflate(4, 4); Border(c, r, Color("#226EC5"));
        foreach (var p in new[] { new SKPoint(r.Left, r.Top), new SKPoint(r.Right, r.Top), new SKPoint(r.Left, r.Bottom), new SKPoint(r.Right, r.Bottom) })
        { var handle = new SKRect(p.X - 3, p.Y - 3, p.X + 3, p.Y + 3); Rect(c, handle, SKColors.White); Border(c, handle, Color("#226EC5")); }
    }
    public void Dispose() { _fill.Dispose(); _stroke.Dispose(); foreach (var f in _fonts.Values) f.Dispose(); _fonts.Clear(); }
}
