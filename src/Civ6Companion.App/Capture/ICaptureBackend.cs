namespace Civ6Companion.App.Capture;

public interface ICaptureBackend
{
    Task CaptureAsync(CivWindow window, string outputPath, CancellationToken cancellationToken);
}
