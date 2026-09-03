using System.Windows.Input;

namespace Civ6Companion.App.Common;

public sealed class AsyncCommand : ICommand
{
    private readonly Func<CancellationToken, Task> _execute;
    private readonly Func<bool>? _canExecute;
    private bool _isExecuting;

    public AsyncCommand(Func<CancellationToken, Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;
    public event EventHandler<Exception>? ExecutionFailed;

    public bool CanExecute(object? parameter) => !_isExecuting && (_canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        try { await ExecuteAsync().ConfigureAwait(true); }
        catch (Exception exception) { ExecutionFailed?.Invoke(this, exception); }
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!CanExecute(null)) return;
        _isExecuting = true;
        RaiseCanExecuteChanged();
        try { await _execute(cancellationToken).ConfigureAwait(true); }
        finally
        {
            _isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
