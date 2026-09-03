namespace Civ6Companion.App.State;

public interface IConversationStore
{
    Task<GameSession?> LoadCurrentAsync(CancellationToken cancellationToken);
    Task SaveAsync(GameSession session, CancellationToken cancellationToken);
    Task<GameSession> StartNewGameAsync(string civilization, CancellationToken cancellationToken);
    Task<GameSession> AppendAsync(GameSession session, ConversationMessage message, CancellationToken cancellationToken);
}
