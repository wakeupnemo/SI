using SIQuester.ViewModel;

namespace SIQuester.ViewModel.Tests;

[TestFixture]
internal sealed class PointSelectionControllerTests
{
    [Test]
    public void SelectFromViewport_UsesUniformImageBoundsAndCanonicalInvariantFormat()
    {
        var controller = new PointSelectionController("", 0.1);

        var selected = controller.SelectFromViewport(
            pointerX: 100,
            pointerY: 100,
            viewportWidth: 200,
            viewportHeight: 200,
            pixelWidth: 400,
            pixelHeight: 200);

        Assert.Multiple(() =>
        {
            Assert.That(selected, Is.True);
            Assert.That(controller.CurrentAnswer, Is.EqualTo("0.5,0.5,2"));
        });
    }

    [Test]
    public void SelectFromViewport_ClampsPointerOutsideLetterboxedImage()
    {
        var controller = new PointSelectionController("", 0.1);

        controller.SelectFromViewport(
            pointerX: -20,
            pointerY: 240,
            viewportWidth: 200,
            viewportHeight: 200,
            pixelWidth: 400,
            pixelHeight: 200);

        Assert.That(controller.CurrentAnswer, Is.EqualTo("0,1,2"));
    }

    [Test]
    public void TryGetVisualLayout_MapsSelectionAndDeviationToDisplayedImage()
    {
        var controller = new PointSelectionController("0.25,0.75,2", 0.1);

        var hasLayout = controller.TryGetVisualLayout(
            viewportWidth: 200,
            viewportHeight: 200,
            pixelWidth: 400,
            pixelHeight: 200,
            out var layout);

        Assert.Multiple(() =>
        {
            Assert.That(hasLayout, Is.True);
            Assert.That(layout.MarkerX, Is.EqualTo(50));
            Assert.That(layout.MarkerY, Is.EqualTo(125));
            Assert.That(layout.Radius, Is.EqualTo(10));
        });
    }

    [TestCase(double.NaN, 0.0)]
    [TestCase(double.NegativeInfinity, 0.0)]
    [TestCase(double.PositiveInfinity, 0.5)]
    [TestCase(-0.1, 0.0)]
    [TestCase(0.7, 0.5)]
    public void Deviation_IsClampedToSupportedRange(double value, double expected)
    {
        var controller = new PointSelectionController("not-a-point", value);

        Assert.Multiple(() =>
        {
            Assert.That(controller.CurrentDeviation, Is.EqualTo(expected));
            Assert.That(controller.TryGetVisualLayout(200, 200, 400, 200, out _), Is.False);
        });
    }

    [Test]
    public void SelectFromViewport_RejectsInvalidDimensionsWithoutChangingAnswer()
    {
        var controller = new PointSelectionController("0.1,0.2,1", 0.1);

        var selected = controller.SelectFromViewport(1, 1, 0, 200, 400, 200);

        Assert.Multiple(() =>
        {
            Assert.That(selected, Is.False);
            Assert.That(controller.CurrentAnswer, Is.EqualTo("0.1,0.2,1"));
        });
    }

    [Test]
    public void SelectFromViewport_RejectsNonFinitePointerWithoutChangingAnswer()
    {
        var controller = new PointSelectionController("0.1,0.2,1", 0.1);

        var selected = controller.SelectFromViewport(double.NaN, 1, 200, 200, 400, 200);

        Assert.Multiple(() =>
        {
            Assert.That(selected, Is.False);
            Assert.That(controller.CurrentAnswer, Is.EqualTo("0.1,0.2,1"));
        });
    }

    [Test]
    public void NudgeSelection_UsesCenterForMissingPointAndClampsAtImageEdges()
    {
        var controller = new PointSelectionController("", 0.1);

        Assert.That(controller.NudgeSelection(0.01, -0.01, 400, 300), Is.True);
        Assert.That(controller.CurrentAnswer, Is.EqualTo("0.51,0.49,1.33"));

        Assert.That(controller.NudgeSelection(1, -1, 400, 300), Is.True);
        Assert.That(controller.CurrentAnswer, Is.EqualTo("1,0,1.33"));
    }
}
