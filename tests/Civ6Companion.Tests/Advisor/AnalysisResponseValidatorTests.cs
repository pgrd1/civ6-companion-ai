using Civ6Companion.App.Advisor;
using FluentAssertions;

namespace Civ6Companion.Tests.Advisor;

public sealed class AnalysisResponseValidatorTests
{
    [Fact]
    public void Parse_AcceptsDirectAndFencedJson()
    {
        var json = ValidJson("map");
        AnalysisResponseValidator.Parse(json).IsValid.Should().BeTrue();
        AnalysisResponseValidator.Parse($"```json\n{json}\n```").Value!.ScreenType.Should().Be(ScreenType.Map);
    }

    [Fact]
    public void Parse_RejectsUnknownTypeRangeExtraPropertyAndTooManyActions()
    {
        const string json = """{"screenType":"invented","confidence":2,"title":"x","immediateActions":[{"action":"1","reason":"r"},{"action":"2","reason":"r"},{"action":"3","reason":"r"},{"action":"4","reason":"r"}],"nextSteps":[],"warnings":[],"fiveTurnGoals":[],"needsAnotherScreen":null,"stateUpdate":"","extra":1}""";
        var result = AnalysisResponseValidator.Parse(json);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("screenType", StringComparison.Ordinal));
        result.Errors.Should().Contain(e => e.Contains("confidence", StringComparison.Ordinal));
        result.Errors.Should().Contain(e => e.Contains("immediateActions", StringComparison.Ordinal));
        result.Errors.Should().Contain(e => e.Contains("extra", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_InvalidInputRetainsOnlySanitizedEightKib()
    {
        var result = AnalysisResponseValidator.Parse(new string('x', 10_000) + "\0secret");
        result.IsValid.Should().BeFalse();
        result.RawText.Should().HaveLength(8 * 1024).And.NotContain("\0");
    }

    [Fact]
    public void ParseChat_ValidatesShapeAndLength()
    {
        AnalysisResponseValidator.ParseChat("""{"message":"좋아요","needsAnotherScreen":null,"stateUpdate":"요약"}""").IsValid.Should().BeTrue();
        AnalysisResponseValidator.ParseChat("""{"message":"좋아요","needsAnotherScreen":null,"stateUpdate":"요약","extra":true}""").IsValid.Should().BeFalse();
    }

    private static string ValidJson(string type) => $$"""{"screenType":"{{type}}","confidence":0.8,"title":"현재 상황","immediateActions":[{"action":"정찰","reason":"시야 확보"}],"nextSteps":["도시 성장"],"warnings":[],"fiveTurnGoals":["개척자"],"needsAnotherScreen":null,"stateUpdate":"4도시"}""";
}
