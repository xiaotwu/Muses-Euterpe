using Muses.Core.Advanced;

namespace Muses.Infrastructure.Advanced;

public sealed class EQService
{
    private readonly IEQRepository _repository;
    private List<EQBand> _currentBands;
    private string _activePresetName = "Flat";

    public event Action<IReadOnlyList<EQBand>>? BandsChanged;
    public event Action<string>? PresetChanged;

    public EQService(IEQRepository repository)
    {
        _repository = repository;
        _currentBands = BuiltinEQPresets.Flat.Select(b => new EQBand(b.Frequency, b.Gain, b.Q)).ToList();
    }

    public string ActivePresetName => _activePresetName;
    public IReadOnlyList<EQBand> ActiveBands => _currentBands;

    public IReadOnlyList<EQPresetEntity> GetCustomPresets() => _repository.GetPresets();

    public void SelectPreset(string nameOrId)
    {
        _activePresetName = nameOrId;
        switch (nameOrId)
        {
            case "Flat":
                _currentBands = BuiltinEQPresets.Flat.Select(b => new EQBand(b.Frequency, b.Gain, b.Q)).ToList();
                break;
            case "HiFi":
                _currentBands = BuiltinEQPresets.HiFi.Select(b => new EQBand(b.Frequency, b.Gain, b.Q)).ToList();
                break;
            case "BassBoost":
                _currentBands = BuiltinEQPresets.BassBoost.Select(b => new EQBand(b.Frequency, b.Gain, b.Q)).ToList();
                break;
            case "Vocal":
                _currentBands = BuiltinEQPresets.Vocal.Select(b => new EQBand(b.Frequency, b.Gain, b.Q)).ToList();
                break;
            case "Electronic":
                _currentBands = BuiltinEQPresets.Electronic.Select(b => new EQBand(b.Frequency, b.Gain, b.Q)).ToList();
                break;
            default:
                var custom = _repository.GetPresets().FirstOrDefault(p => p.Id == nameOrId || p.Name == nameOrId);
                if (custom != null)
                {
                    _currentBands = custom.GetBands().Select(b => new EQBand(b.Frequency, b.Gain, b.Q)).ToList();
                }
                break;
        }

        PresetChanged?.Invoke(_activePresetName);
        BandsChanged?.Invoke(_currentBands);
    }

    public void SetBandGain(int index, float gain)
    {
        if (index < 0 || index >= _currentBands.Count) return;
        var existing = _currentBands[index];
        _currentBands[index] = new EQBand(existing.Frequency, Math.Clamp(gain, -24f, 24f), existing.Q);
        _activePresetName = "Custom";
        PresetChanged?.Invoke(_activePresetName);
        BandsChanged?.Invoke(_currentBands);
    }

    public void Reset()
    {
        SelectPreset("Flat");
    }

    public EQPresetEntity SaveCustomPreset(string name)
    {
        var preset = EQPresetEntity.Create(name, _currentBands);
        _repository.SavePreset(preset);
        _activePresetName = preset.Name;
        PresetChanged?.Invoke(_activePresetName);
        return preset;
    }

    public void DeleteCustomPreset(string id)
    {
        var custom = _repository.GetPresets().FirstOrDefault(p => p.Id == id || p.Name == id);
        _repository.DeletePreset(id);
        if (custom != null && (_activePresetName == custom.Id || _activePresetName == custom.Name))
        {
            Reset();
        }
    }
}
