namespace Civ6Companion.Tests.TestSupport;

public sealed class TempDirectory : IDisposable
{
    private readonly string _resolvedPath;

    public TempDirectory()
    {
        var temporaryRoot = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath());
        _resolvedPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(temporaryRoot, $"Civ6Companion.Tests.{Guid.NewGuid():N}"));

        if (!IsChildOf(_resolvedPath, temporaryRoot))
        {
            throw new InvalidOperationException("The temporary directory path is invalid.");
        }

        Directory.CreateDirectory(_resolvedPath);
    }

    public string Path => _resolvedPath;

    public void Dispose()
    {
        if (Directory.Exists(_resolvedPath))
        {
            Directory.Delete(_resolvedPath, recursive: true);
        }
    }

    private static bool IsChildOf(string candidate, string parent)
    {
        var normalizedParent = parent.EndsWith(System.IO.Path.DirectorySeparatorChar)
            ? parent
            : parent + System.IO.Path.DirectorySeparatorChar;

        return candidate.StartsWith(normalizedParent, StringComparison.OrdinalIgnoreCase);
    }
}
