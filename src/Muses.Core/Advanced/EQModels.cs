using System.Text.Json;

namespace Muses.Core.Advanced;

public sealed record EQBand(double Frequency, float Gain, float Q = 1.0f);

public sealed record EQPresetEntity(string Id, string Name, string BandsJson)
{
    public IReadOnlyList<EQBand> GetBands()
    {
        try
        {
            return JsonSerializer.Deserialize<List<EQBand>>(BandsJson) ?? BuiltinEQPresets.Flat;
        }
        catch
        {
            return BuiltinEQPresets.Flat;
        }
    }

    public static EQPresetEntity Create(string name, IReadOnlyList<EQBand> bands)
    {
        var json = JsonSerializer.Serialize(bands);
        return new EQPresetEntity(Guid.NewGuid().ToString(), name, json);
    }
}

public static class BuiltinEQPresets
{
    public static readonly double[] ISO32Frequencies =
    [
        16, 20, 25, 31.5, 40, 50, 63, 80, 100, 125, 160, 200, 250, 315, 400, 500, 630,
        800, 1000, 1250, 1600, 2000, 2500, 3150, 4000, 5000, 6300, 8000, 10000,
        12500, 16000, 20000
    ];

    public static IReadOnlyList<EQBand> Flat =>
        ISO32Frequencies.Select(f => new EQBand(f, 0f, 1.0f)).ToList();

    public static IReadOnlyList<EQBand> BassBoost
    {
        get
        {
            return ISO32Frequencies.Select(f =>
            {
                var gain = f switch
                {
                    <= 63 => 6.0f,
                    <= 125 => 4.5f,
                    <= 250 => 2.5f,
                    _ => 0f
                };
                return new EQBand(f, gain, 1.0f);
            }).ToList();
        }
    }

    public static IReadOnlyList<EQBand> Vocal
    {
        get
        {
            return ISO32Frequencies.Select(f =>
            {
                var gain = f switch
                {
                    < 200 => -2.0f,
                    >= 1000 and <= 4000 => 3.5f,
                    > 8000 => -1.0f,
                    _ => 0f
                };
                return new EQBand(f, gain, 1.0f);
            }).ToList();
        }
    }

    public static IReadOnlyList<EQBand> HiFi
    {
        get
        {
            return ISO32Frequencies.Select(f =>
            {
                var gain = f switch
                {
                    <= 50 => 3.0f,
                    <= 100 => 2.0f,
                    >= 10000 => 3.0f,
                    >= 6300 => 1.5f,
                    _ => 0f
                };
                return new EQBand(f, gain, 1.0f);
            }).ToList();
        }
    }

    public static IReadOnlyList<EQBand> Electronic
    {
        get
        {
            return ISO32Frequencies.Select(f =>
            {
                var gain = f switch
                {
                    <= 80 => 5.0f,
                    >= 250 and <= 500 => -1.5f,
                    >= 4000 => 3.0f,
                    _ => 0f
                };
                return new EQBand(f, gain, 1.0f);
            }).ToList();
        }
    }
}
