using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using Muses.App.ViewModels;

namespace Muses.App.Views;

public partial class PodcastsView : UserControl
{
    private PodcastsViewModel? _viewModel;

    public PodcastsView() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null) _viewModel.PropertyChanged -= OnViewModelChanged;
        _viewModel = DataContext as PodcastsViewModel;
        if (_viewModel is not null) _viewModel.PropertyChanged += OnViewModelChanged;
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PodcastsViewModel.IsDetail)) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(sender, _viewModel) || !IsEffectivelyVisible) return;
            if (_viewModel?.IsDetail == true)
            {
                DetailScroll.Offset = default;
                BackToShowsButton.Focus();
            }
            else
                PodcastSearchBox.Focus();
        }, DispatcherPriority.Input);
    }
}
