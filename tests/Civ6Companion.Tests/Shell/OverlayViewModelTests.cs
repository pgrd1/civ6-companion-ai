using Civ6Companion.App.Advisor;
using Civ6Companion.App.Shell;
using FluentAssertions;

namespace Civ6Companion.Tests.Shell;

public sealed class OverlayViewModelTests
{
    [Fact]
    public void ReadyGovernmentState_MapsBadgeAndLimitsImmediateActionsToThree()
    {
        var advisor = new FakeAdvisor();
        using var viewModel = new OverlayViewModel(advisor);
        var actions = Enumerable.Range(1, 4).Select(index => new RecommendedAction($"행동 {index}", $"이유 {index}")).ToArray();

        advisor.Publish(new(AdvisorStatus.Ready, Analysis: new(
            ScreenType.Government, .95, "정책 추천", actions, ["다음"], ["주의"], ["목표"], null, "상태")));

        viewModel.ScreenBadge.Should().Be("정부·정책");
        viewModel.Title.Should().Be("정책 추천");
        viewModel.ImmediateActions.Should().HaveCount(3);
        viewModel.IsBusy.Should().BeFalse();
    }

    [Theory]
    [InlineData(ScreenType.Map, "지도")]
    [InlineData(ScreenType.CityProduction, "생산")]
    [InlineData(ScreenType.Technology, "기술")]
    [InlineData(ScreenType.Civic, "사회제도")]
    [InlineData(ScreenType.GreatPerson, "위인")]
    [InlineData(ScreenType.CityState, "도시국가")]
    [InlineData(ScreenType.Diplomacy, "외교")]
    [InlineData(ScreenType.Trade, "교역")]
    [InlineData(ScreenType.CitizenManagement, "시민 관리")]
    [InlineData(ScreenType.Religion, "종교")]
    [InlineData(ScreenType.Other, "기타")]
    public void ReadyState_MapsScreenBadge(ScreenType screenType, string badge)
    {
        var advisor = new FakeAdvisor();
        using var viewModel = new OverlayViewModel(advisor);

        advisor.Publish(new(AdvisorStatus.Ready, Analysis: EmptyAnalysis(screenType)));

        viewModel.ScreenBadge.Should().Be(badge);
    }

    [Fact]
    public void AnalyzingState_DisablesAnalyzeAndSendCommands()
    {
        var advisor = new FakeAdvisor();
        using var viewModel = new OverlayViewModel(advisor) { ChatInput = "질문" };

        advisor.Publish(new(AdvisorStatus.Analyzing, Message: "분석 중"));

        viewModel.IsBusy.Should().BeTrue();
        viewModel.StatusMessage.Should().Be("분석 중");
        viewModel.AnalyzeCommand.CanExecute(null).Should().BeFalse();
        viewModel.SendChatCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void AnalyzingState_DisablesBothNewGameCommands()
    {
        var advisor = new FakeAdvisor();
        using var viewModel = new OverlayViewModel(advisor);

        advisor.Publish(new(AdvisorStatus.Analyzing, Message: "분석 중"));

        viewModel.RequestNewGameCommand.CanExecute(null).Should().BeFalse();
        viewModel.NewGameCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task NewGameCommand_OnSuccessfulCompletion_ClearsStaleSessionUi()
    {
        var advisor = new FakeAdvisor();
        using var viewModel = new OverlayViewModel(advisor) { ChatInput = "다음 행동은?" };
        advisor.Publish(new(AdvisorStatus.Ready, Analysis: new(
            ScreenType.Map, 1, "기존 추천", [new("정찰", "정보")], ["다음"], ["주의"], ["목표"], "도시 화면", "상태")));
        advisor.Publish(new(AdvisorStatus.Error, Message: "원래 오류", RawFallback: "원문"));
        advisor.Publish(new(AdvisorStatus.Ready, Chat: new("기존 채팅", "외교 화면", "상태")));

        await viewModel.NewGameCommand.ExecuteAsync();

        advisor.NewGameCalls.Should().Be(1);
        viewModel.ScreenBadge.Should().Be("대기");
        viewModel.Title.Should().Be("문명 6 도우미");
        viewModel.StatusMessage.Should().Be("새 게임을 시작했습니다. 화면을 저장하려면 F7을 누르세요.");
        viewModel.ImmediateActions.Should().BeEmpty();
        viewModel.NextSteps.Should().BeEmpty();
        viewModel.Warnings.Should().BeEmpty();
        viewModel.FiveTurnGoals.Should().BeEmpty();
        viewModel.NeedsAnotherScreen.Should().BeNull();
        viewModel.RawFallback.Should().BeNull();
        viewModel.ChatInput.Should().BeEmpty();
        viewModel.ChatTranscript.Should().BeEmpty();
    }

    [Fact]
    public async Task NewGameCommand_WhenAdvisorPublishesFailure_PreservesTheExistingSessionUi()
    {
        var advisor = new FakeAdvisor { NewGameResult = false };
        using var viewModel = new OverlayViewModel(advisor);
        advisor.Publish(new(AdvisorStatus.Ready, Analysis: EmptyAnalysis(ScreenType.Map) with { Title = "기존 추천" }));

        await viewModel.NewGameCommand.ExecuteAsync();

        viewModel.StatusMessage.Should().Be("새 게임을 시작하지 못했습니다.");
        viewModel.Title.Should().Be("기존 추천");
    }

    [Fact]
    public async Task SendChat_TrimsInputAndAddsTranscript()
    {
        var advisor = new FakeAdvisor();
        using var viewModel = new OverlayViewModel(advisor) { ChatInput = "  다음은?  " };

        await viewModel.SendChatCommand.ExecuteAsync();
        advisor.LastChat.Should().Be("다음은?");
        viewModel.ChatInput.Should().BeEmpty();
        viewModel.ChatTranscript.Should().ContainSingle().Which.Should().Be("나: 다음은?");

        advisor.Publish(new(AdvisorStatus.Ready, Chat: new("캠퍼스를 지으세요.", null, "")));
        viewModel.ChatTranscript.Should().Contain("도우미: 캠퍼스를 지으세요.");
    }

    [Fact]
    public async Task QueueCaptureCommand_QueuesWithoutStartingAnalysis()
    {
        var advisor = new FakeAdvisor();
        using var viewModel = new OverlayViewModel(advisor);

        await viewModel.QueueCaptureCommand.ExecuteAsync();

        advisor.QueueCalls.Should().Be(1);
        advisor.AnalyzeCalls.Should().Be(0);
    }

    [Fact]
    public void ErrorState_ShowsRawFallbackAndAnotherScreenRequest()
    {
        var advisor = new FakeAdvisor();
        using var viewModel = new OverlayViewModel(advisor);

        advisor.Publish(new(AdvisorStatus.Error, Message: "분석 실패", RawFallback: "원문 응답"));
        viewModel.StatusMessage.Should().Be("분석 실패");
        viewModel.RawFallback.Should().Be("원문 응답");

        advisor.Publish(new(AdvisorStatus.Ready, Analysis: EmptyAnalysis(ScreenType.Map) with { NeedsAnotherScreen = "생산 창" }));
        viewModel.NeedsAnotherScreen.Should().Be("생산 창");
    }

    private static AnalysisResponse EmptyAnalysis(ScreenType screenType) =>
        new(screenType, 1, "추천", [], [], [], [], null, "");

    private sealed class FakeAdvisor : IAdvisorOrchestrator
    {
        public event EventHandler<AdvisorState>? StateChanged;
        public string? LastChat { get; private set; }
        public int QueueCalls { get; private set; }
        public int AnalyzeCalls { get; private set; }
        public int NewGameCalls { get; private set; }
        public bool NewGameResult { get; init; } = true;
        public Task QueueCurrentScreenAsync(CancellationToken cancellationToken)
        {
            QueueCalls++;
            return Task.CompletedTask;
        }
        public Task AnalyzeCurrentScreenAsync(CancellationToken cancellationToken)
        {
            AnalyzeCalls++;
            return Task.CompletedTask;
        }
        public Task<bool> StartNewGameAsync(CancellationToken cancellationToken)
        {
            NewGameCalls++;
            if (!NewGameResult)
            {
                Publish(new(AdvisorStatus.Error, Message: "새 게임을 시작하지 못했습니다."));
                return Task.FromResult(false);
            }

            return Task.FromResult(true);
        }
        public Task SendChatAsync(string message, CancellationToken cancellationToken)
        {
            LastChat = message;
            return Task.CompletedTask;
        }
        public void Cancel() { }
        public void Publish(AdvisorState state) => StateChanged?.Invoke(this, state);
    }
}
