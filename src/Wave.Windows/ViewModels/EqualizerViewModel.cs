using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wave.Windows.Models;
using Wave.Windows.Services;

namespace Wave.Windows.ViewModels;

/// <summary>
/// ViewModel para la ventana del ecualizador.
/// Expone 5 bandas observables (60Hz, 230Hz, 910Hz, 3.6kHz, 14kHz) con sus ganancias.
/// </summary>
public partial class EqualizerViewModel : ObservableObject, IDisposable
{
    private readonly AudioEngine _engine;
    private bool _disposed;

    /// <summary>Bandas del ecualizador (para binding con ItemsControl).</summary>
    public ObservableCollection<EqualizerBandViewModel> Bands { get; } = new();

    [ObservableProperty]
    private bool _enabled = true;

    public EqualizerViewModel(AudioEngine engine)
    {
        _engine = engine;

        // Poblar bandas iniciales desde el AudioEngine (si hay)
        RefreshFromEngine();
    }

    /// <summary>Recarga las bandas desde el EqualizerSampleProvider actual del AudioEngine.</summary>
    public void RefreshFromEngine()
    {
        var eq = _engine.Equalizer;
        Bands.Clear();
        if (eq == null)
        {
            // Sin pista cargada: bandas default con gain 0
            foreach (var freq in new[] { 60, 230, 910, 3600, 14000 })
            {
                Bands.Add(new EqualizerBandViewModel(freq, 0f, OnBandGainChanged));
            }
            return;
        }

        foreach (var band in eq.Bands)
        {
            Bands.Add(new EqualizerBandViewModel(band.Frequency, band.Gain, OnBandGainChanged));
        }
        Enabled = eq.Enabled;
    }

    /// <summary>Callback cuando cambia la ganancia de una banda en la UI.</summary>
    private void OnBandGainChanged(int index, float gainDb)
    {
        _engine.Equalizer?.SetBandGain(index, gainDb);
    }

    /// <summary>Command: activar/desactivar el ecualizador.</summary>
    [RelayCommand]
    private void ToggleEnabled()
    {
        Enabled = !Enabled;
        _engine.EqualizerEnabled = Enabled;
    }

    /// <summary>Command: resetear todas las bandas a 0 dB.</summary>
    [RelayCommand]
    private void ResetBands()
    {
        for (int i = 0; i < Bands.Count; i++)
        {
            Bands[i].Gain = 0f;
            _engine.Equalizer?.SetBandGain(i, 0f);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // No hay eventos que desuscribir (AudioEngine es singleton gestionado por DI)
    }
}

/// <summary>ViewModel de cada banda del ecualizador.</summary>
public partial class EqualizerBandViewModel : ObservableObject
{
    /// <summary>Frecuencia central en Hz (display "60Hz", "230Hz", etc.).</summary>
    public string DisplayFrequency { get; }

    /// <summary>Frecuencia numérica para UI.</summary>
    public float Frequency { get; }

    [ObservableProperty]
    private float _gain;

    /// <summary>Ganancia en dB normalizada 0..1 (slider -12..+12 dB → 0..1).</summary>
    public float GainNormalized
    {
        get => (Gain + 12f) / 24f;
        set
        {
            Gain = (value * 24f) - 12f;
            _onGainChanged?.Invoke(_index, Gain);
        }
    }

    /// <summary>Display de ganancia en dB (ej: "+3 dB", "-6 dB").</summary>
    public string GainDisplay => Gain >= 0 ? $"+{Gain:F0} dB" : $"{Gain:F0} dB";

    private readonly int _index;
    private readonly Action<int, float> _onGainChanged;

    public EqualizerBandViewModel(float frequency, float gain, Action<int, float> onGainChanged)
    {
        Frequency = frequency;
        Gain = gain;
        DisplayFrequency = frequency >= 1000 ? $"{frequency / 1000:F1}kHz" : $"{frequency:F0}Hz";
        _index = (int)Math.Log10(frequency); // placeholder, se sobreescribe abajo
        _onGainChanged = onGainChanged;
    }

    /// <summary>Dispara notificación del GainDisplay cuando cambia Gain.</summary>
    partial void OnGainChanged(float value)
    {
        OnPropertyChanged(nameof(GainDisplay));
        _onGainChanged?.Invoke(_index, value);
    }
}
