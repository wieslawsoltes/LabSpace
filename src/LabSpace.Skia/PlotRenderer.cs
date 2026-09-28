using LabSpace.Core;
using SkiaSharp;

namespace LabSpace.Skia;

public sealed class PlotRenderer(LabDrawing drawing)
{
    /// <summary>Min/max buckets preserve peaks while limiting a trace to roughly two vertices per pixel.</summary>
    public void Draw(SKCanvas c, SKRect bounds, Value? value, double[]? history, bool spectrum, double? cursor = null)
    {
        var d = drawing; d.Bevel(c, bounds, "#C6C6C6");
        var plot = new SKRect(bounds.Left + 43, bounds.Top + 25, bounds.Right - 14, bounds.Bottom - 32);
        d.Rect(c, plot, LabDrawing.Color("#06110A")); d.Border(c, plot, SKColors.Black);
        for (var i = 0; i <= 10; i++) { var x = plot.Left + i * plot.Width / 10; d.Line(c, x, plot.Top, x, plot.Bottom, LabDrawing.Color("#27432C")); }
        for (var i = 0; i <= 8; i++) { var y = plot.Top + i * plot.Height / 8; d.Line(c, plot.Left, y, plot.Right, y, LabDrawing.Color("#27432C")); }
        IReadOnlyList<double> samples = history is not null ? history : value is not null ? value.Samples : Array.Empty<double>();
        var min = samples.Count > 0 ? Math.Min(0, samples.Min()) : -1;
        var max = samples.Count > 0 ? Math.Max(.001, samples.Max()) : 1;
        if (!spectrum) { var extent = Math.Max(Math.Abs(min), Math.Abs(max)) * 1.12; min = -Math.Max(1, extent); max = Math.Max(1, extent); } else max *= 1.15;
        var range = Math.Max(1e-20, max - min);
        for (var i = 0; i <= 4; i++) d.Text(c, (max - range * i / 4).ToString("G3", System.Globalization.CultureInfo.InvariantCulture), bounds.Left + 5, plot.Top + i * plot.Height / 4 + 4, 10);
        var rate = value?.SampleRate ?? 1; var duration = samples.Count > 0 ? (samples.Count - 1) / rate : 1;
        for (var i = 0; i <= 4; i++) d.Text(c, (duration * i / 4).ToString("G3", System.Globalization.CultureInfo.InvariantCulture), plot.Left + i * plot.Width / 4, plot.Bottom + 15, 10, center: true);
        d.Text(c, spectrum ? "Frequency (Hz)" : "Time (s)", plot.MidX, bounds.Bottom - 4, 10, center: true);
        d.Text(c, spectrum ? "Amplitude" : "Plot 0", bounds.Right - 72, bounds.Top + 16, 10);
        var color = LabDrawing.Color(spectrum ? "#F3D75C" : "#55F25B"); d.Line(c, bounds.Right - 30, bounds.Top + 12, bounds.Right - 11, bounds.Top + 12, color, 2);
        if (samples.Count > 1 && plot.Width > 1 && plot.Height > 1)
        {
            c.Save(); c.ClipRect(plot); using var path = new SKPath(); var buckets = Math.Max(1, Math.Min(samples.Count, (int)plot.Width)); var first = true;
            for (var bucket = 0; bucket < buckets; bucket++)
            {
                var from = bucket * samples.Count / buckets; var to = Math.Max(from + 1, (bucket + 1) * samples.Count / buckets); var lo = samples[from]; var hi = lo;
                for (var j = from + 1; j < to; j++) { lo = Math.Min(lo, samples[j]); hi = Math.Max(hi, samples[j]); }
                var x = plot.Left + (float)bucket / Math.Max(1, buckets - 1) * plot.Width;
                var a = plot.Bottom - (float)((lo - min) / range * plot.Height); var b = plot.Bottom - (float)((hi - min) / range * plot.Height);
                if (first) { path.MoveTo(x, a); first = false; } else path.LineTo(x, a); if (Math.Abs(b - a) > .1) path.LineTo(x, b);
            }
            d.Path(c, path, color, 1.3f); c.Restore();
            if (cursor.HasValue)
            {
                var t = Math.Clamp(cursor.Value, 0, 1); var index = (int)Math.Round(t * (samples.Count - 1)); var x = plot.Left + (float)t * plot.Width;
                d.Line(c, x, plot.Top, x, plot.Bottom, LabDrawing.Color("#E2E2E2"));
                d.Text(c, $"X: {index / rate:G4}   Y: {samples[index]:G5}", plot.Left + 7, plot.Top + 15, 11, "#FFFFFF");
            }
        }
        else d.Text(c, "Run VI to acquire a simulated waveform", plot.MidX, plot.MidY, 12, "#87A88B", true);
    }
}
