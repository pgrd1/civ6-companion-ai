using Civ6Companion.App.Advisor;

namespace Civ6Companion.App.State;

public enum MessageRole { User, Assistant }

public sealed record ConversationMessage(MessageRole Role, string Text, DateTimeOffset Timestamp);

public sealed record GameSession(
    Guid Id,
    DateTimeOffset StartedAt,
    string Civilization,
    string Difficulty,
    string Speed,
    string VictoryGoal,
    string CompressedSummary,
    AnalysisResponse? LastAnalysis,
    IReadOnlyList<ConversationMessage> RecentMessages);
