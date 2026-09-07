using CommunityToolkit.Mvvm.ComponentModel;

namespace Muses.App.ViewModels;

public sealed partial class EQBandViewModel : ObservableObject
{
    private readonly Action<int, float>? _onGainChanged;

    public int Index { get; }
    public double Frequency { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GainReadout))]
    private float _gain;

    public string Label => Frequency >= 1000 ? $"{Frequency / 1000:0.#}k" : $"{Frequency:0}";

    public string GainReadout => Gain > 0 ? $"+{Gain:0}" : $"{Gain:0}";

    public EQBandViewModel(int index, double frequency, float initialGain, Action<int, float>? onGainChanged = null)
    {
        Index = index;
        Frequency = frequency;
        _gain = initialGain;
        _onGainChanged = onGainChanged;
    }

    partial void OnGainChanged(float value)
    {
        _onGainChanged?.Invoke(Index, value);
    }
}
