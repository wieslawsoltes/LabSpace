using LabSpace.Core;
using LabSpace.Documents;
using LabSpace.Editing;
using Xunit;

namespace LabSpace.Tests;

public sealed class EditorSemanticsTests
{
    [Theory]
    [InlineData("DBL")][InlineData("U64")][InlineData("I32[]")][InlineData("DBL[,,]")]
    [InlineData("{serial: U64, data: {x: SGL, y: Boolean}[]}")]
    [InlineData("enum(Off, On, Auto)")][InlineData("{\"quoted name\": String}")]
    public void CompactTypesRoundTrip(string text)
    {
        var type = TypeSyntax.Parse(text); Assert.Equal(type, TypeSyntax.Parse(TypeSyntax.Format(type)));
    }
    [Theory]
    [InlineData("DBL junk")][InlineData("X64")][InlineData("DBL[][]")][InlineData("{x: DBL,x: DBL}")]
    [InlineData("enum(A,A)")][InlineData("{x DBL}")][InlineData("enum()")][InlineData("I32[,,,,,,,,]")]
    public void InvalidTypesAreRejected(string text) => Assert.ThrowsAny<Exception>(() => TypeSyntax.Parse(text));

    [Fact]
    public void RouteAvoidsObstaclesAndIsDeterministic()
    {
        var obstacles = new[] { new RectD(80, -40, 60, 80), new RectD(170, 0, 40, 70) };
        var route = OrthogonalRouter.Route(new(0, 0), new(300, 0), obstacles);
        Assert.True(route.ObstacleFree); Assert.Equal(new PointD(0, 0), route.Points[0]); Assert.Equal(new PointD(300, 0), route.Points[^1]);
        for (var i = 1; i < route.Points.Count; i++)
        {
            Assert.True(route.Points[i - 1].X == route.Points[i].X || route.Points[i - 1].Y == route.Points[i].Y);
            Assert.All(obstacles, r => Assert.False(OrthogonalRouter.IntersectsInterior(route.Points[i - 1], route.Points[i], r)));
        }
        Assert.Equal(route.Points, OrthogonalRouter.Route(new(0, 0), new(300, 0), obstacles).Points);
    }
    [Fact]
    public void CanceledRouteAndInvalidGeometryFailBeforeWork()
    {
        Assert.Throws<OperationCanceledException>(() => OrthogonalRouter.Route(new(0, 0), new(100, 0), [], cancellationToken: new(true)));
        Assert.Throws<ArgumentException>(() => OrthogonalRouter.Route(new(0, 0), new(100, 0), [new(double.NaN, 0, 20, 20)]));
    }
    [Fact]
    public void RouteReportsObstructionWhenEndpointIsInsideAnObstacle()
    {
        var route = OrthogonalRouter.Route(new(0, 0), new(100, 0), [new RectD(-20, -20, 50, 40)], searchLimit: 1);
        Assert.False(route.ObstacleFree); Assert.InRange(route.ExploredStates, 0, 1);
    }
    [Fact]
    public void TypeEditsPreserveExactValuesAndUndoBothFields()
    {
        var project = Examples.Create(); var session = new InstrumentSession(project);
        var n = session.Add("typed-control", 40, 40);
        session.SetTypedLiteral(n.Id, LabType.Scalar(ValueKind.UInt64), "18446744073709551615");
        Assert.Equal(ulong.MaxValue, session.DisplayValue(session.Find(n.Id)!).Unsigned);
        session.Undo(); Assert.Equal(LabType.Int32, session.Find(n.Id)!.Type);
    }
    [Fact]
    public void InvalidNewLiteralCannotChangeTheDocument()
    {
        var session = new InstrumentSession(Examples.Create()); var n = session.Add("typed-control", 40, 40);
        var before = ProjectSerializer.Save(session.Project);
        Assert.ThrowsAny<Exception>(() => session.SetTypedLiteral(n.Id, LabType.Scalar(ValueKind.UInt8), "999"));
        Assert.Equal(before, ProjectSerializer.Save(session.Project));
    }
}
