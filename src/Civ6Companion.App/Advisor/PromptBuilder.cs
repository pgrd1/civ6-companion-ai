using System.Text;
using Civ6Companion.App.State;

namespace Civ6Companion.App.Advisor;

public sealed class PromptBuilder
{
    private const string Rules = """
        문명 6 초보자에게 한국어로 짧고 구체적으로 답하세요. 화면에서 확인할 수 없는 내용은 반드시 '확인 불가'라고 쓰세요.
        보이지 않는 타일 좌표나 수치를 지어내지 마세요. 사용자를 대신해 자동으로 게임을 조작하거나 입력하지 마세요.
        먼저 지금 바로 할 행동을 최대 3개 제시하고 각각 이유를 붙이세요. 생산, 기술, 사회 제도, 정부 정책, 위인, 도시 국가 등 화면 종류에 맞춰 답하세요.
        """;

    public string BuildAnalysisPrompt(GameSession session, int imageCount = 1)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (imageCount < 1) throw new ArgumentOutOfRangeException(nameof(imageCount));
        return Rules + $"""
            화면 유형은 다음 12개 중 정확히 하나입니다: {string.Join(", ", ScreenTypeJsonConverter.WireValues)}.
            현재 게임 상태: {Safe(session.CompressedSummary)}
            문명: {Safe(session.Civilization)}, 난이도: {Safe(session.Difficulty)}, 속도: {Safe(session.Speed)}, 목표: {Safe(session.VictoryGoal)}
            첨부한 문명 6 화면은 시간순으로 총 {imageCount}장입니다. 모든 화면의 정보를 합쳐 지금 해야 할 일을 판단하고 제공된 JSON 스키마만 출력하세요.
            """;
    }

    public string BuildChatPrompt(GameSession session, string message)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        var builder = new StringBuilder(Rules).AppendLine().Append("현재 게임 상태: ")
            .AppendLine(Safe(session.CompressedSummary)).AppendLine("최근 대화:");
        foreach (var item in session.RecentMessages.TakeLast(6))
            builder.Append(item.Role == MessageRole.User ? "사용자: " : "도우미: ").AppendLine(Safe(item.Text));
        builder.Append("새 질문: ").AppendLine(Safe(message)).Append("제공된 JSON 스키마만 출력하세요.");
        return builder.ToString();
    }

    private static string Safe(string? value) => string.IsNullOrWhiteSpace(value)
        ? "없음"
        : value.Trim()[..Math.Min(value.Trim().Length, 6_000)];
}
