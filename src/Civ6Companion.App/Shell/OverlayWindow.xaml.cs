using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace Civ6Companion.App.Shell;

public partial class OverlayWindow : Window
{
    private readonly OverlayViewModel _viewModel;
    private bool _exitRequested;

    public OverlayWindow(OverlayViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = viewModel;
        viewModel.HideRequested += OnHideRequested;
        viewModel.NewGameRequested += OnNewGameRequested;
        viewModel.ExitRequested += OnExitRequested;
    }

    protected override void OnClosing(CancelEventArgs eventArgs)
    {
        if (!_exitRequested)
        {
            eventArgs.Cancel = true;
            Hide();
        }

        base.OnClosing(eventArgs);
    }

    protected override void OnClosed(EventArgs eventArgs)
    {
        _viewModel.HideRequested -= OnHideRequested;
        _viewModel.NewGameRequested -= OnNewGameRequested;
        _viewModel.ExitRequested -= OnExitRequested;
        base.OnClosed(eventArgs);
    }

    private void DragArea_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (eventArgs.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void ChatBox_OnPreviewKeyDown(object sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;
        if (_viewModel.SendChatCommand.CanExecute(null)) _viewModel.SendChatCommand.Execute(null);
        eventArgs.Handled = true;
    }

    private void ChatBox_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (!IsActive) Activate();
        Keyboard.Focus(ChatBox);
    }

    private void OnHideRequested(object? sender, EventArgs eventArgs) => Hide();

    private void OnNewGameRequested(object? sender, EventArgs eventArgs)
    {
        var result = MessageBox.Show(this, "현재 대화와 게임 진행 요약을 새로 시작할까요?", "새 게임",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result == MessageBoxResult.Yes) _viewModel.NewGameCommand.Execute(null);
    }

    private void OnExitRequested(object? sender, EventArgs eventArgs)
    {
        _exitRequested = true;
        Close();
        Application.Current?.Shutdown();
    }
}
