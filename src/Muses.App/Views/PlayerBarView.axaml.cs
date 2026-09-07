using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Muses.App.ViewModels;

namespace Muses.App.Views;

public partial class PlayerBarView : UserControl
{
    private bool _isDraggingScrubber;
    private ShellViewModel? _vm;

    public PlayerBarView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _vm = DataContext as ShellViewModel;

        if (_vm is not null)
        {
            _vm.PropertyChanged += OnViewModelPropertyChanged;
            UpdateProgress();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShellViewModel.CurrentPositionSeconds) or nameof(ShellViewModel.CurrentDurationSeconds) or nameof(ShellViewModel.ProgressFraction))
        {
            if (!_isDraggingScrubber)
            {
                Dispatcher.UIThread.Post(UpdateProgress);
            }
        }
    }

    private void UpdateProgress()
    {
        if (_vm is null || ProgressTrackContainer is null) return;

        var totalWidth = ProgressTrackContainer.Bounds.Width;
        if (totalWidth <= 0) totalWidth = 612; // default fallback width

        var fraction = _vm.ProgressFraction;
        var fillWidth = Math.Clamp(fraction * totalWidth, 0, totalWidth);

        ProgressTrackFill.Width = fillWidth;
        ProgressThumb.Margin = new Thickness(Math.Max(0, fillWidth - 4), -2.5, 0, 0);
    }

    private void OnProgressPointerEntered(object? sender, PointerEventArgs e)
    {
        ProgressTrackBg.Height = 4.5;
        ProgressTrackFill.Height = 4.5;
        ProgressThumb.IsVisible = true;
    }

    private void OnProgressPointerExited(object? sender, PointerEventArgs e)
    {
        if (!_isDraggingScrubber)
        {
            ProgressTrackBg.Height = 3;
            ProgressTrackFill.Height = 3;
            ProgressThumb.IsVisible = false;
        }
    }

    private void OnProgressPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(ProgressTrackContainer).Properties.IsLeftButtonPressed)
        {
            _isDraggingScrubber = true;
            e.Pointer.Capture(ProgressTrackContainer);
            ProgressTrackBg.Height = 4.5;
            ProgressTrackFill.Height = 4.5;
            ProgressThumb.IsVisible = true;
            SeekFromPointer(e.GetPosition(ProgressTrackContainer).X);
        }
    }

    private void OnProgressPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isDraggingScrubber)
        {
            SeekFromPointer(e.GetPosition(ProgressTrackContainer).X);
        }
    }

    private void OnProgressPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isDraggingScrubber)
        {
            SeekFromPointer(e.GetPosition(ProgressTrackContainer).X, commit: true);
            _isDraggingScrubber = false;
            e.Pointer.Capture(null);

            var pos = e.GetPosition(ProgressTrackContainer);
            if (pos.X < 0 || pos.X > ProgressTrackContainer.Bounds.Width || pos.Y < 0 || pos.Y > ProgressTrackContainer.Bounds.Height)
            {
                ProgressTrackBg.Height = 3;
                ProgressTrackFill.Height = 3;
                ProgressThumb.IsVisible = false;
            }
        }
    }

    private void OnProgressPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _isDraggingScrubber = false;
        ProgressTrackBg.Height = 3;
        ProgressTrackFill.Height = 3;
        ProgressThumb.IsVisible = false;
        UpdateProgress();
    }

    private void SeekFromPointer(double x, bool commit = false)
    {
        if (_vm is null || ProgressTrackContainer is null) return;

        var totalWidth = ProgressTrackContainer.Bounds.Width;
        if (totalWidth <= 0) return;

        var fraction = Math.Clamp(x / totalWidth, 0, 1);
        var fillWidth = fraction * totalWidth;

        ProgressTrackFill.Width = fillWidth;
        ProgressThumb.Margin = new Thickness(Math.Max(0, fillWidth - 4), -2.5, 0, 0);

        if (commit && _vm.CurrentDurationSeconds > 0)
        {
            _vm.SeekToSeconds(fraction * _vm.CurrentDurationSeconds);
        }
    }
}
