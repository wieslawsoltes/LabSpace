using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using LabSpace.Dataflow;
using LabSpace.Documents;
using LabSpace.Signals;

var count = args.Length > 0 && int.TryParse(args[0], out var requested) ? Math.Clamp(requested, 10, 100000) : 1000;
var vi = Examples.SignalAnalysis(); var plan = GraphCompiler.Compile(vi.Diagram); var runtime = new DataflowRuntime();
for (var i = 0; i < 30; i++) runtime.Run(plan);
var allocations = GC.GetAllocatedBytesForCurrentThread(); var watch = Stopwatch.StartNew();
for (var i = 0; i < count; i++) runtime.Run(plan);
watch.Stop(); var execution = new { frames = count, totalMilliseconds = watch.Elapsed.TotalMilliseconds, millisecondsPerFrame = watch.Elapsed.TotalMilliseconds / count, bytesPerFrame = (GC.GetAllocatedBytesForCurrentThread() - allocations) / count };
var input = SignalMath.Generate(4096, 48000, 1000, 1, 0); for (var i = 0; i < 20; i++) SignalMath.Spectrum(input);
allocations = GC.GetAllocatedBytesForCurrentThread(); watch.Restart(); for (var i = 0; i < count; i++) SignalMath.Spectrum(input); watch.Stop();
var fft = new { samples = 4096, iterations = count, millisecondsPerTransform = watch.Elapsed.TotalMilliseconds / count, bytesPerTransform = (GC.GetAllocatedBytesForCurrentThread() - allocations) / count };
Console.WriteLine(JsonSerializer.Serialize(new { runtime = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(), logicalProcessors = Environment.ProcessorCount, scope = "Managed engine and FFT only; not GPU or UI frame time", execution, fft }, new JsonSerializerOptions { WriteIndented = true }));
