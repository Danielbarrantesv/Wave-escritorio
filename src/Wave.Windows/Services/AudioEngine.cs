using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Wave.Windows.Audio;

namespace Wave.Windows.Services;

/// <summary>
/// Wrapper sobre NAudio que gestiona el pipeline de audio:
///   reader (MediaFoundationReader) → resampler → visualizer → equalizer → volume → waveOut
///
/// Expone eventos para que PlaybackController sincronice UI.
/// </summary>
public sealed class AudioEngine : IDisposable
{
    private WaveOutEvent? _waveOut;
    private MediaFoundationReader? _reader;
    private ISampleProvider? _sampleProvider;
    private VisualizerEngine? _visualizer;
    private EqualizerSampleProvider? _equalizer;
    private VolumeSampleProvider? _volume;
    private float _volumeLevel = 1.0f;
    private bool _disposed;

    /// <summary>Se dispara cuando el estado de reproducción cambia (Play/Pause/Stop).</summary>
    public event EventHandler<PlaybackState>? PlaybackStateChanged;

    /// <summary>Se dispara cuando NAudio detiene el playback (natural o por Stop()).</summary>
    public event EventHandler<StoppedEventArgs>? PlaybackStopped;

    /// <summary>Se dispara cuando la posición cambia (seek o playback normal).</summary>
    public event EventHandler? PositionChanged;

    /// <summary>True si actualmente está reproduciendo audio.</summary>
    public bool IsPlaying => _waveOut?.PlaybackState == PlaybackState.Playing;

    /// <summary>Posición actual en milisegundos.</summary>
    public long PositionMs => _reader != null
        ? (long)_reader.CurrentTime.TotalMilliseconds
        : 0;

    /// <summary>Duración total de la pista actual en milisegundos.</summary>
    public long LengthMs => _reader != null
        ? (long)_reader.TotalTime.TotalMilliseconds
        : 0;

    /// <summary>Instancia del ecualizador (5 bandas). Null si no hay pista cargada.</summary>
    public EqualizerSampleProvider? Equalizer => _equalizer;

    /// <summary>Instancia del visualizador FFT. Null si no hay pista cargada.</summary>
    public VisualizerEngine? Visualizer => _visualizer;

    /// <summary>True si el ecualizador está activo (false = bypass).</summary>
    public bool EqualizerEnabled
    {
        get => _equalizer?.Enabled ?? false;
        set { if (_equalizer != null) _equalizer.Enabled = value; }
    }

    /// <summary>Volumen de 0.0 a 1.0.</summary>
    public float Volume
    {
        get => _volumeLevel;
        set
        {
            _volumeLevel = Math.Clamp(value, 0f, 1f);
            if (_volume != null) _volume.Volume = _volumeLevel;
        }
    }

    /// <summary>
    /// Carga un archivo de audio y prepara el pipeline.
    /// Descarta cualquier reproducción previa.
    /// </summary>
    /// <param name="filePath">Ruta absoluta al archivo de audio (mp3, flac, m4a, ogg, wav).</param>
    /// <exception cref="FileNotFoundException">Si el archivo no existe.</exception>
    /// <exception cref="InvalidOperationException">Si NAudio no puede leer el formato.</exception>
    public void Load(string filePath)
    {
        System.Diagnostics.Debug.WriteLine($"[AudioEngine] Loading: {filePath}");
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Archivo de audio no encontrado: {filePath}", filePath);

        // Descartar pipeline anterior
        DisposeInternal();

        try
        {
            _reader = new MediaFoundationReader(filePath);
            System.Diagnostics.Debug.WriteLine($"[AudioEngine] Reader created: {_reader.WaveFormat} duration={_reader.TotalTime}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AudioEngine] MediaFoundationReader failed: {ex.Message}");
            throw;
        }

        // Convertir a ISampleProvider (float 32-bit, 44.1kHz, stereo)
        _sampleProvider = ConvertToSampleProvider(_reader);

        // Pipeline: source → visualizer → equalizer → volume → waveOut
        _visualizer = new VisualizerEngine(_sampleProvider, fftLength: 1024);
        _equalizer = new EqualizerSampleProvider(_visualizer);
        _volume = new VolumeSampleProvider(_equalizer) { Volume = _volumeLevel };

        _waveOut = new WaveOutEvent
        {
            DesiredLatency = 200,
            NumberOfBuffers = 3
        };
        _waveOut.Init(_volume);
        _waveOut.PlaybackStopped += (s, e) => PlaybackStopped?.Invoke(s, e);
        System.Diagnostics.Debug.WriteLine("[AudioEngine] Pipeline initialized OK");
    }

    /// <summary>
    /// Convierte el IWaveProvider del reader a ISampleProvider normalizado a 44.1kHz stereo float.
    /// Esto garantiza que todos los formatos (mp3, flac, m4a) pasen por el mismo pipeline.
    /// </summary>
    private static ISampleProvider ConvertToSampleProvider(IWaveProvider waveProvider)
    {
        var waveFormat = waveProvider.WaveFormat;

        // Si ya es float IEEE, solo envolver con WaveToSampleProvider
        if (waveFormat.Encoding == WaveFormatEncoding.IeeeFloat && waveFormat.Channels == 2)
        {
            return new WaveToSampleProvider(waveProvider);
        }

        // Si no, resamplear a 44.1kHz stereo float
        var targetFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
        var resampler = new MediaFoundationResampler(waveProvider, targetFormat);
        return resampler.ToSampleProvider();
    }

    /// <summary>Inicia la reproducción. Si ya está reproduciendo, no hace nada.</summary>
    public void Play()
    {
        if (_waveOut == null) return;
        if (_waveOut.PlaybackState != PlaybackState.Playing)
        {
            _waveOut.Play();
        }
        PlaybackStateChanged?.Invoke(this, PlaybackState.Playing);
    }

    /// <summary>Pausa la reproducción. Mantiene la posición.</summary>
    public void Pause()
    {
        if (_waveOut == null) return;
        _waveOut.Pause();
        PlaybackStateChanged?.Invoke(this, PlaybackState.Paused);
    }

    /// <summary>Detiene la reproducción y resetea la posición a 0.</summary>
    public void Stop()
    {
        if (_waveOut == null) return;
        _waveOut.Stop();
        if (_reader != null) _reader.CurrentTime = TimeSpan.Zero;
        PlaybackStateChanged?.Invoke(this, PlaybackState.Stopped);
    }

    /// <summary>Salta a una posición específica en milisegundos.</summary>
    public void Seek(long positionMs)
    {
        if (_reader == null) return;
        var clamped = Math.Clamp(positionMs, 0, LengthMs);
        _reader.CurrentTime = TimeSpan.FromMilliseconds(clamped);
        PositionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Notifica a los suscriptores que la posición cambió.
    /// PlaybackController lo invoca desde un DispatcherTimer para refrescar la UI.
    /// </summary>
    public void RaisePositionChanged()
    {
        PositionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Libera los recursos de NAudio. Idempotente.</summary>
    public void Dispose()
    {
        DisposeInternal();
        _disposed = true;
    }

    private void DisposeInternal()
    {
        _waveOut?.Dispose();
        _reader?.Dispose();
        _waveOut = null;
        _reader = null;
        _sampleProvider = null;
        _volume = null;
    }
}
