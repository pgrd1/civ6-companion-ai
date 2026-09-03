namespace Civ6Companion.App.Capture;

public interface ICivWindowLocator
{
    Task<WindowLookupResult> FindAsync(CancellationToken cancellationToken);
}

public enum WindowLookupFailure
{
    NotRunning,
    NotForeground,
    Minimized,
    InvalidBounds
}

public sealed record WindowLookupResult(CivWindow? Window, WindowLookupFailure? Failure)
{
    public static WindowLookupResult Found(CivWindow window) => new(window, null);

    public static WindowLookupResult Failed(WindowLookupFailure failure) => new(null, failure);
}
