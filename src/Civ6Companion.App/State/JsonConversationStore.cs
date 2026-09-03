using System.Text.Json;
using System.IO;
using Civ6Companion.App.Advisor;
using Civ6Companion.App.Common;

namespace Civ6Companion.App.State;

public sealed class JsonConversationStore : IConversationStore, IDisposable
{
    private const int MaxMessages = 6;
    private const int MaxMessageLength = 4_000;
    private const int MaxSummaryLength = 6_000;
    private readonly string _root;
    private readonly string _currentPath;
    private readonly IClock _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonConversationStore(string root, IClock clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = Path.GetFullPath(root);
        _currentPath = Path.Combine(_root, "current.json");
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public static JsonConversationStore CreateDefault(IClock clock) => new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Civ6CodexCompanion", "State"), clock);

    public async Task<GameSession?> LoadCurrentAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_currentPath)) return null;
            try
            {
                await using var stream = new FileStream(_currentPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
                var session = await JsonSerializer.DeserializeAsync<GameSession>(stream, AdvisorJson.Options, cancellationToken).ConfigureAwait(false);
                return session is null ? null : Normalize(session);
            }
            catch (JsonException)
            {
                Directory.CreateDirectory(_root);
                File.Move(_currentPath, Path.Combine(_root, $"current.corrupt-{_clock.UtcNow:yyyyMMddHHmmssfff}.json"), overwrite: false);
                return null;
            }
        }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(GameSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await WriteAtomicAsync(Normalize(session), cancellationToken).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    public async Task<GameSession> StartNewGameAsync(string civilization, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(civilization);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_root);
            if (File.Exists(_currentPath))
            {
                try
                {
                    GameSession? old;
                    await using (var stream = new FileStream(_currentPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true))
                        old = await JsonSerializer.DeserializeAsync<GameSession>(stream, AdvisorJson.Options, cancellationToken).ConfigureAwait(false);
                    if (old is not null)
                    {
                        var archive = Path.Combine(_root, "Archive"); Directory.CreateDirectory(archive);
                        File.Move(_currentPath, Path.Combine(archive, $"{old.Id:N}.json"), overwrite: true);
                    }
                }
                catch (JsonException) { File.Move(_currentPath, Path.Combine(_root, $"current.corrupt-{_clock.UtcNow:yyyyMMddHHmmssfff}.json"), false); }
            }
            var session = new GameSession(Guid.NewGuid(), _clock.UtcNow, civilization.Trim(), "확인 불가", "확인 불가", "미정", "", null, Array.Empty<ConversationMessage>());
            await WriteAtomicAsync(session, cancellationToken).ConfigureAwait(false);
            return session;
        }
        finally { _gate.Release(); }
    }

    public async Task<GameSession> AppendAsync(GameSession session, ConversationMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session); ArgumentNullException.ThrowIfNull(message);
        var capped = message with { Text = Limit(message.Text, MaxMessageLength) };
        var messages = session.RecentMessages.Append(capped).TakeLast(MaxMessages).ToArray();
        var updated = Normalize(session with { RecentMessages = messages });
        await SaveAsync(updated, cancellationToken).ConfigureAwait(false);
        return updated;
    }

    private async Task WriteAtomicAsync(GameSession session, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_root);
        var temp = Path.Combine(_root, $"current.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true))
            {
                await JsonSerializer.SerializeAsync(stream, session, AdvisorJson.Options, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(temp, _currentPath, overwrite: true);
        }
        finally { try { File.Delete(temp); } catch (IOException) { } }
    }

    private static GameSession Normalize(GameSession session) => session with
    {
        Civilization = Limit(session.Civilization, 600), Difficulty = Limit(session.Difficulty, 600),
        Speed = Limit(session.Speed, 600), VictoryGoal = Limit(session.VictoryGoal, 600),
        CompressedSummary = Limit(session.CompressedSummary, MaxSummaryLength),
        RecentMessages = (session.RecentMessages ?? Array.Empty<ConversationMessage>()).TakeLast(MaxMessages).Select(m => m with { Text = Limit(m.Text, MaxMessageLength) }).ToArray()
    };

    private static string Limit(string? value, int length) { value ??= ""; return value[..Math.Min(value.Length, length)]; }
    public void Dispose() => _gate.Dispose();
}
