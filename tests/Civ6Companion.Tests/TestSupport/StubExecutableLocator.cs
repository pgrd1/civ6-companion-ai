using Civ6Companion.App.Advisor;

namespace Civ6Companion.Tests.TestSupport;

public sealed class StubExecutableLocator : IExecutableLocator
{
    private readonly string? _path;

    public StubExecutableLocator(string? path)
    {
        _path = path;
    }

    public string? Find(string? configuredPath) => _path;
}
