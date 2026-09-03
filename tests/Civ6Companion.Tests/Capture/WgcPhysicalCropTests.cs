using System.Runtime.InteropServices;
using FluentAssertions;

namespace Civ6Companion.Tests.Capture;

public sealed class WgcPhysicalCropTests
{
    [Theory]
    [InlineData(100, 100, 1200, 900, 108, 132, 1184, 760, 1200, 900, 8, 32)] // 100% DPI
    [InlineData(1250, 300, 1500, 1125, 1260, 340, 1480, 1045, 1500, 1125, 10, 40)] // 125% DPI
    [InlineData(3000, 450, 1920, 1080, 3012, 498, 1896, 1008, 1920, 1080, 12, 48)] // 150% DPI with invisible frame border
    public void ComputeWgcClientCropForTest_UsesPhysicalFrameAndClientCoordinates(
        int frameLeft,
        int frameTop,
        int frameWidth,
        int frameHeight,
        int clientLeft,
        int clientTop,
        int clientWidth,
        int clientHeight,
        int contentWidth,
        int contentHeight,
        int expectedX,
        int expectedY)
    {
        var result = ComputeWgcClientCropForTest(
            frameLeft, frameTop, frameWidth, frameHeight,
            clientLeft, clientTop, clientWidth, clientHeight,
            contentWidth, contentHeight,
            out var cropX, out var cropY);

        result.Should().Be(0);
        cropX.Should().Be(expectedX);
        cropY.Should().Be(expectedY);
    }

    [Fact]
    public void ComputeWgcClientCropForTest_RejectsContentThatDoesNotMatchThePhysicalFrame()
    {
        var result = ComputeWgcClientCropForTest(
            1250, 300, 1500, 1125,
            1260, 340, 1480, 1045,
            1499, 1125,
            out _, out _);

        result.Should().BeLessThan(0);
    }

    [Fact]
    public void ComputeWgcClientCropForTest_RejectsAClientRectangleOutsideTheCapturedContent()
    {
        var result = ComputeWgcClientCropForTest(
            3000, 450, 1920, 1080,
            3012, 498, 1910, 1008,
            1920, 1080,
            out _, out _);

        result.Should().BeLessThan(0);
    }

    [DllImport("Civ6Companion.WgcNative.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int ComputeWgcClientCropForTest(
        int frameLeft,
        int frameTop,
        int frameWidth,
        int frameHeight,
        int clientLeft,
        int clientTop,
        int clientWidth,
        int clientHeight,
        int contentWidth,
        int contentHeight,
        out int cropX,
        out int cropY);
}
