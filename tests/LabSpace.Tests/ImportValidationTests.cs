using LabSpace.Documents;
using Xunit;

namespace LabSpace.Tests;

public sealed class ImportValidationTests
{
    [Fact]
    public void UnknownFunctionsAreRejectedBeforeReachingTheUi()
    {
        var json = ProjectSerializer.Save(Examples.Create()).Replace("\"kind\": \"control\"", "\"kind\": \"unavailable-driver\"");
        Assert.Throws<InvalidDataException>(() => ProjectSerializer.Load(json));
    }
    [Fact]
    public void UnknownPanelWidgetsAreRejected()
    {
        var json = ProjectSerializer.Save(Examples.Create()).Replace("\"widget\": \"Knob\"", "\"widget\": \"unavailable-widget\"");
        Assert.Throws<InvalidDataException>(() => ProjectSerializer.Load(json));
    }
    [Fact]
    public void ExcessivePanelCoordinatesAreRejected()
    {
        var project = Examples.Create(); project.Instruments[0].Panel[0].Bounds = project.Instruments[0].Panel[0].Bounds with { X = 1e20 };
        Assert.Throws<InvalidDataException>(() => ProjectSerializer.Save(project));
    }
}
