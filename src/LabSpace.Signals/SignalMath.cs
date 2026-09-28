using System.Numerics;
using LabSpace.Core;

namespace LabSpace.Signals;

public static class SignalMath
{
    public static Value Generate(int count, double rate, double frequency, double amplitude, double start, string shape = "Sine", double noise = 0, int seed = 42)
    {
        if (count is < 2 or > 65536 || !double.IsFinite(rate) || rate <= 0 || !double.IsFinite(frequency) || frequency < 0 || frequency > rate / 2 || !double.IsFinite(amplitude) || !double.IsFinite(start) || !double.IsFinite(noise) || noise < 0)
            throw new ArgumentException("Signal requires 2–65,536 samples, a positive rate, and frequency between zero and Nyquist.");
        var samples = new double[count]; var random = new Random(seed);
        for (var i = 0; i < count; i++)
        {
            var phase = 2 * Math.PI * frequency * (start + i / rate);
            var wave = shape.ToLowerInvariant() switch { "square" => Math.Sin(phase) >= 0 ? 1 : -1, "triangle" => 2 / Math.PI * Math.Asin(Math.Sin(phase)), _ => Math.Sin(phase) };
            samples[i] = amplitude * wave + noise * (2 * random.NextDouble() - 1);
        }
        return Value.Series(samples, rate, start);
    }
    public static Value Transform(Value input, Func<double, double> transform) => Value.Series(input.Samples.Select(transform), input.SampleRate, input.StartTime, input.Kind == ValueKind.Waveform);
    public static Value MovingAverage(Value input, int window)
    {
        if (window is < 1 or > 1024) throw new ArgumentOutOfRangeException(nameof(window));
        var output = new double[input.Samples.Length]; double sum = 0;
        for (var i = 0; i < output.Length; i++) { sum += input.Samples[i]; if (i >= window) sum -= input.Samples[i - window]; output[i] = sum / Math.Min(i + 1, window); }
        return Value.Series(output, input.SampleRate, input.StartTime);
    }
    public static double Rms(IEnumerable<double> samples)
    {
        double scale = 0, sum = 1; var count = 0;
        foreach (var item in samples)
        {
            if (!double.IsFinite(item)) throw new ArgumentException("RMS requires finite samples.");
            count++; var x = Math.Abs(item); if (x == 0) continue;
            if (scale < x) { var r = scale / x; sum = 1 + sum * r * r; scale = x; } else { var r = x / scale; sum += r * r; }
        }
        if (count == 0) throw new ArgumentException("RMS requires at least one sample.");
        return scale == 0 ? 0 : scale * Math.Sqrt(sum / count);
    }
    public static Value Spectrum(Value input)
    {
        var n = input.Samples.Length;
        if (n < 4 || (n & (n - 1)) != 0) throw new ArgumentException("FFT requires a power-of-two sample count of at least four.");
        var a = new Complex[n]; double windowSum = 0;
        for (var i = 0; i < n; i++) { var window = .5 - .5 * Math.Cos(2 * Math.PI * i / n); windowSum += window; a[i] = input.Samples[i] * window; }
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1; for (; (j & bit) != 0; bit >>= 1) j ^= bit; j ^= bit;
            if (i < j) (a[i], a[j]) = (a[j], a[i]);
        }
        for (var length = 2; length <= n; length <<= 1)
        {
            var root = Complex.FromPolarCoordinates(1, -2 * Math.PI / length);
            for (var start = 0; start < n; start += length)
            {
                var factor = Complex.One;
                for (var j = 0; j < length / 2; j++) { var u = a[start + j]; var v = a[start + j + length / 2] * factor; a[start + j] = u + v; a[start + j + length / 2] = u - v; factor *= root; }
            }
        }
        var result = new double[n / 2 + 1];
        for (var i = 0; i < result.Length; i++) result[i] = a[i].Magnitude / windowSum * (i == 0 || i == n / 2 ? 1 : 2);
        // A waveform is used as an evenly spaced series: dt is frequency-bin width here.
        return Value.Series(result, n / input.SampleRate);
    }
}
