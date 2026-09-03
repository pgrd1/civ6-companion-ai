using Civ6Companion.App.Advisor;

namespace Civ6Companion.Tests.TestSupport;

public sealed class StubProcessRunner : IProcessRunner
{
    private readonly Func<ProcessRequest, ProcessResult> _resultFactory;

    public StubProcessRunner(ProcessResult result)
        : this(_ => result)
    {
    }

    public StubProcessRunner(Func<ProcessRequest, ProcessResult> resultFactory)
    {
        _resultFactory = resultFactory ?? throw new ArgumentNullException(nameof(resultFactory));
    }

    public ProcessRequest? LastRequest { get; private set; }

    public IReadOnlyList<ProcessRequest> Requests => _requests;

    private readonly List<ProcessRequest> _requests = [];

    public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastRequest = request;
        _requests.Add(request);
        return Task.FromResult(_resultFactory(request));
    }
}
