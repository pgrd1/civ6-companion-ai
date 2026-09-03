using Civ6Companion.App.Advisor;
using Civ6Companion.App.State;
using FluentAssertions;

namespace Civ6Companion.Tests.Advisor;

public sealed class PromptBuilderTests
{
    [Fact]
    public void AnalysisPrompt_ContainsSafetyTaxonomyAndState()
    {
        var prompt = new PromptBuilder().BuildAnalysisPrompt(Session("호조 도키무네, 5도시"));
        prompt.Should().Contain("한국어").And.Contain("확인 불가").And.Contain("지어내지").And.Contain("자동으로 게임을 조작");
        foreach (var type in ScreenTypeJsonConverter.WireValues) prompt.Should().Contain(type);
        prompt.Should().Contain("호조 도키무네, 5도시");
    }

    [Fact]
    public void ChatPrompt_UsesOnlyLatestSixMessages()
    {
        var messages = Enumerable.Range(1, 8).Select(i => new ConversationMessage(MessageRole.User, $"m{i}", DateTimeOffset.UnixEpoch)).ToArray();
        var prompt = new PromptBuilder().BuildChatPrompt(Session("상태") with { RecentMessages = messages }, "다음은?");
        prompt.Should().NotContain("m1").And.NotContain("m2").And.Contain("m3").And.Contain("m8").And.Contain("다음은?");
    }

    private static GameSession Session(string summary) => new(Guid.NewGuid(), DateTimeOffset.UnixEpoch, "일본", "왕자", "보통", "과학", summary, null, Array.Empty<ConversationMessage>());
}
