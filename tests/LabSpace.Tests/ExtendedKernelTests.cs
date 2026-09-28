using LabSpace.Core;
using LabSpace.Dataflow;
using LabSpace.Documents;
using Xunit;

namespace LabSpace.Tests;

public sealed class ExtendedKernelTests
{
    private static Value Run(string kind, params (string Port, string Kind, double Number, string Text)[] args)
    {
        var graph = new Diagram(); var operation = Examples.NewNode(kind, 0, 0); graph.Nodes.Add(operation);
        foreach (var arg in args) { var n = Examples.NewNode(arg.Kind, 0, 0); n.Value = arg.Number; n.Text = arg.Text; graph.Nodes.Add(n); Examples.Connect(graph, n, operation, arg.Port); }
        return new DataflowRuntime().Run(GraphCompiler.Compile(graph)).Values[operation.Id];
    }
    [Theory]
    [InlineData("increment", 8, 9)] [InlineData("decrement", 8, 7)] [InlineData("negate", 8, -8)] [InlineData("floor", -1.2, -2)]
    [InlineData("ceiling", -1.2, -1)] [InlineData("truncate", -1.2, -1)] [InlineData("log", 1, 0)] [InlineData("log10", 100, 2)]
    [InlineData("exp", 0, 1)] [InlineData("tan", 0, 0)] [InlineData("sign", -10, -1)] [InlineData("reciprocal", 2, .5)]
    public void ExtendedNumericFunctions(string kind, double x, double expected) => Assert.Equal(expected, Run(kind, ("x", "constant", x, "")).Number, 10);
    [Theory] [InlineData("upper", "Hello", "HELLO")] [InlineData("lower", "Hello", "hello")] [InlineData("trim", "  hi  ", "hi")] [InlineData("reverse-string", "A🙂B", "B🙂A")]
    public void StringTransforms(string kind, string x, string expected) => Assert.Equal(expected, Run(kind, ("x", "string", 0, x)).Text);
    [Fact] public void Utf8LengthIsNotUtf16Length() => Assert.Equal(4, Run("utf8-length", ("x", "string", 0, "🙂")).Number);
    [Fact] public void ArraysSortAndReverse() { Assert.Equal(new double[] { 1, 2, 3 }, Run("sort-array", ("x", "array", 0, "3,1,2")).Samples); Assert.Equal(new double[] { 3, 2, 1 }, Run("reverse-array", ("x", "array", 0, "1,2,3")).Samples); }
    [Fact] public void ArraySearchReturnsMinusOneForMissingElement() => Assert.Equal(-1, Run("search-array", ("x", "array", 0, "1,2,3"), ("value", "constant", 9, "")).Number);
    [Fact] public void StringReplaceAndParseAreRealKernels() { Assert.Equal("a--c", Run("replace-string", ("x", "string", 0, "abc"), ("search", "string", 0, "b"), ("replacement", "string", 0, "--")).Text); Assert.Equal(125, Run("parse-number", ("x", "string", 0, "1.25e2")).Number); }
    [Fact] public void NonFiniteParseFails() => Assert.Throws<NodeExecutionException>(() => Run("parse-number", ("x", "string", 0, "NaN")));
    [Fact] public void WaveformConstructionPreservesMetadata() { var value = Run("build-waveform", ("x", "array", 0, "1,2,3"), ("rate", "constant", 250, ""), ("start", "constant", 12, "")); Assert.Equal(250, value.SampleRate); Assert.Equal(12, value.StartTime); Assert.Equal(3, value.Samples.Length); }
}
