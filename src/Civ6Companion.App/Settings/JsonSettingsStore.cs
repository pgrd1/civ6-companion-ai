using System.IO;
using System.Text.Json;

namespace Civ6Companion.App.Settings;

public sealed class JsonSettingsStore : ISettingsStore
{
    private const string SettingsFileName = "settings.json";
    private const string TemporaryFileName = "settings.json.tmp";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private static readonly SemaphoreSlim SettingsIoLock = new(1, 1);

    private readonly string _settingsDirectory;
    private readonly string _settingsFilePath;

    public JsonSettingsStore(string settingsDirectory)
    {
        if (string.IsNullOrWhiteSpace(settingsDirectory))
        {
            throw new ArgumentException("The settings directory is invalid.", nameof(settingsDirectory));
        }

        _settingsDirectory = Path.GetFullPath(settingsDirectory);
        _settingsFilePath = Path.Combine(_settingsDirectory, SettingsFileName);
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await SettingsIoLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            try
            {
                var json = await File.ReadAllTextAsync(_settingsFilePath, cancellationToken).ConfigureAwait(false);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions);

                if (settings is null || !AreValid(settings)) throw new InvalidDataException();

                cancellationToken.ThrowIfCancellationRequested();
                return settings;
            }
            catch (JsonException)
            {
                PreserveInvalidSettings(cancellationToken);
                return new AppSettings();
            }
            catch (InvalidDataException)
            {
                PreserveInvalidSettings(cancellationToken);
                return new AppSettings();
            }
            catch (FileNotFoundException)
            {
                return new AppSettings();
            }
            catch (DirectoryNotFoundException)
            {
                return new AppSettings();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (IOException exception)
            {
                throw new IOException("Settings could not be loaded.", exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                throw new IOException("Settings could not be loaded.", exception);
            }
        }
        finally
        {
            SettingsIoLock.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();

        if (!AreValid(settings))
        {
            throw new ArgumentException("Settings contain invalid values.");
        }

        await SettingsIoLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            Directory.CreateDirectory(_settingsDirectory);
            var temporaryFilePath = CreateTemporaryFilePath();

            try
            {
                await using (var temporaryFile = new FileStream(
                    temporaryFilePath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 4096,
                    useAsync: true))
                {
                    await JsonSerializer.SerializeAsync(temporaryFile, settings, SerializerOptions, cancellationToken)
                        .ConfigureAwait(false);
                    await temporaryFile.FlushAsync(cancellationToken).ConfigureAwait(false);
                    temporaryFile.Flush(flushToDisk: true);
                }

                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temporaryFilePath, _settingsFilePath, overwrite: true);
            }
            finally
            {
                TryDeleteTemporaryFile(temporaryFilePath);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IOException exception)
        {
            throw new IOException("Settings could not be saved.", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new IOException("Settings could not be saved.", exception);
        }
        finally
        {
            SettingsIoLock.Release();
        }
    }

    private void PreserveInvalidSettings(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            if (!File.Exists(_settingsFilePath)) return;

            var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfffffff");
            var invalidFilePath = Path.Combine(_settingsDirectory, $"settings.json.invalid-{timestamp}");
            File.Move(_settingsFilePath, invalidFilePath);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (FileNotFoundException)
        {
            // An external process removed the invalid file before it could be preserved.
        }
        catch (IOException exception)
        {
            throw new IOException("Invalid settings could not be preserved.", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new IOException("Invalid settings could not be preserved.", exception);
        }
    }

    private static bool AreValid(AppSettings settings) =>
        !string.IsNullOrWhiteSpace(settings.Hotkey) &&
        double.IsFinite(settings.OverlayLeft) &&
        double.IsFinite(settings.OverlayTop) &&
        double.IsFinite(settings.OverlayWidth) &&
        settings.OverlayWidth > 0;

    private string CreateTemporaryFilePath() =>
        Path.Combine(_settingsDirectory, $"{TemporaryFileName}.{Guid.NewGuid():N}");

    private static void TryDeleteTemporaryFile(string temporaryFilePath)
    {
        try
        {
            if (File.Exists(temporaryFilePath))
            {
                File.Delete(temporaryFilePath);
            }
        }
        catch (IOException)
        {
            // The main save error is more actionable than cleanup failure.
        }
        catch (UnauthorizedAccessException)
        {
            // The main save error is more actionable than cleanup failure.
        }
    }
}
