namespace Civ6Companion.App.Advisor;

public interface IExecutableLocator
{
    string? Find(string? configuredPath);
}
