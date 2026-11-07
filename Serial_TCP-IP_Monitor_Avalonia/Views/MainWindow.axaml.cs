using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Serial_TCP_IP_Monitor_Avalonia.ViewModels;

namespace Serial_TCP_IP_Monitor_Avalonia.Views;

public partial class MainWindow : Window
{
    private MainViewModel? _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => AttachViewModel(DataContext as MainViewModel);
        SendTextBox.KeyDown += OnSendTextBoxKeyDown;
        Opened += OnOpened;
        Closed += OnClosed;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is not null)
        {
            const double safeMargin = 32;
            var availableWidth = Math.Max(640, screen.WorkingArea.Width / screen.Scaling - safeMargin);
            var availableHeight = Math.Max(420, screen.WorkingArea.Height / screen.Scaling - safeMargin);

            MinWidth = Math.Min(MinWidth, availableWidth);
            MinHeight = Math.Min(MinHeight, availableHeight);
            Width = Math.Min(Width, availableWidth);
            Height = Math.Min(Height, availableHeight);
        }

        if (_viewModel is not null)
        {
            try
            {
                await _viewModel.InitializeAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"InitializeAsync error: {ex}");
            }
        }
    }

    private void AttachViewModel(MainViewModel? viewModel)
    {
        if (_viewModel is not null)
        {
            _viewModel.VisibleLogs.CollectionChanged -= OnVisibleLogsChanged;
        }

        _viewModel = viewModel;
        if (_viewModel is not null)
        {
            _viewModel.VisibleLogs.CollectionChanged += OnVisibleLogsChanged;
        }
    }

    private void OnVisibleLogsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_viewModel is not { AutoScroll: true } || _viewModel.VisibleLogs.Count == 0)
        {
            return;
        }

        var last = _viewModel.VisibleLogs[^1];
        Dispatcher.UIThread.Post(() => LogList.ScrollIntoView(last), DispatcherPriority.Background);
    }

    private void OnSendTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
        {
            if (_viewModel.SendCommand.CanExecute(null))
            {
                _viewModel.SendCommand.Execute(null);
            }
            e.Handled = true;
        }
        else if (e.Key is Key.Up or Key.Down)
        {
            _viewModel.NavigateHistory(e.Key == Key.Up ? -1 : 1);
            Dispatcher.UIThread.Post(() => SendTextBox.CaretIndex = SendTextBox.Text?.Length ?? 0);
            e.Handled = true;
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        Opened -= OnOpened;
        SendTextBox.KeyDown -= OnSendTextBoxKeyDown;
        if (_viewModel is not null)
        {
            _viewModel.VisibleLogs.CollectionChanged -= OnVisibleLogsChanged;
            _viewModel.Dispose();
            _viewModel = null;
        }
    }
}
