using Avalonia.Controls;
using Avalonia.Interactivity;
using Muses.App.ViewModels;

namespace Muses.App.Controls;

public partial class VolumeSliderControl : UserControl
{
    private float _preMuteVolume = 0.8f;

    public VolumeSliderControl()
    {
        InitializeComponent();
    }

    private void OnMuteClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ShellViewModel vm)
        {
            if (vm.CurrentVolume > 0.001f)
            {
                _preMuteVolume = vm.CurrentVolume;
                vm.ChangeVolume(0);
            }
            else
            {
                vm.ChangeVolume(_preMuteVolume <= 0.001f ? 0.8f : _preMuteVolume);
            }
        }
    }

    private void OnSliderValueChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (DataContext is ShellViewModel vm)
        {
            vm.ChangeVolume(e.NewValue);
        }
    }
}
