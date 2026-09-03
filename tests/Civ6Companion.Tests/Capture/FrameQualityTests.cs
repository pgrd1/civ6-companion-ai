using Civ6Companion.App.Capture;
using Civ6Companion.Tests.TestSupport;
using FluentAssertions;

namespace Civ6Companion.Tests.Capture;

public sealed class FrameQualityTests
{
    [Theory]
    [InlineData("black-frame.png", true)]
    [InlineData("civ-map-sample.png", false)]
    public void IsUnusable_DetectsFlatOrBlackFrames(string fixture, bool expected)
    {
        FrameQuality.IsUnusable(FixtureFiles.Path(fixture)).Should().Be(expected);
    }

    [Theory]
    [InlineData(564, false)]
    [InlineData(565, true)]
    public void IsUnusable_UsesExactNinetyEightPercentBlackSampleCutoff(int blackSamples, bool expected)
    {
        var pixels = GridWithBlackSamples(blackSamples, blackLuminance: 0);
        var path = FixtureFiles.CreatePng(32, 18, pixels);

        FrameQuality.IsUnusable(path).Should().Be(expected);
    }

    [Theory]
    [InlineData(7, true)]
    [InlineData(8, false)]
    public void IsUnusable_UsesStrictLuminanceThreshold(int baseLuminance, bool expected)
    {
        var pixels = GridWithBlackSamples(565, baseLuminance);
        var path = FixtureFiles.CreatePng(32, 18, pixels);

        FrameQuality.IsUnusable(path).Should().Be(expected);
    }

    [Theory]
    [InlineData(3, true)]
    [InlineData(4, false)]
    public void IsUnusable_UsesStrictStandardDeviationThreshold(int range, bool expected)
    {
        var pixels = new byte[32 * 18 * 4];
        for (var index = 0; index < 32 * 18; index++)
        {
            var luminance = (byte)(128 + ((index % 2) * range));
            pixels[index * 4] = luminance;
            pixels[(index * 4) + 1] = luminance;
            pixels[(index * 4) + 2] = luminance;
            pixels[(index * 4) + 3] = 255;
        }

        var path = FixtureFiles.CreatePng(32, 18, pixels);

        FrameQuality.IsUnusable(path).Should().Be(expected);
    }

    private static byte[] GridWithBlackSamples(int blackSamples, int blackLuminance)
    {
        var pixels = new byte[32 * 18 * 4];
        for (var index = 0; index < 32 * 18; index++)
        {
            var luminance = (byte)(index < blackSamples ? blackLuminance : 255);
            pixels[index * 4] = luminance;
            pixels[(index * 4) + 1] = luminance;
            pixels[(index * 4) + 2] = luminance;
            pixels[(index * 4) + 3] = 255;
        }

        return pixels;
    }
}
