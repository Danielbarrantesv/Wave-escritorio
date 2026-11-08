using NAudio.Dsp;
using NAudio.Wave;

namespace Wave.Windows.Audio;

/// <summary>
/// Proveedor de muestras que calcula el espectro de frecuencias (FFT) en tiempo real.
/// Se inserta en el pipeline ANTES del equalizer para ver el espectro sin procesar.
///
/// Pipeline actualizado: reader → VisualizerEngine → equalizer → volume → waveOut
///
/// Acumula muestras hasta llenar el buffer FFT (1024), aplica Hamming window,
/// ejecuta FFT, calcula magnitudes y dispara el evento SpectrumUpdated.
/// </summary>
public class VisualizerEngine : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _fftLength;
    private readonly Complex[] _fftBuffer;
    private readonly float[] _window;
    private int _bufferFill;
    private bool _enabled = true;

    /// <summary>
    /// Se dispara cuando se completa una FFT. El argumento es un array de
    /// magnitudes (float) con longitud _fftLength/2, ya en escala logarítmica.
    /// </summary>
    public event EventHandler<float[]>? SpectrumUpdated;

    /// <summary>Número de bandas de frecuencia que produce (fftLength / 2).</summary>
    public int BandCount => _fftLength / 2;

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    /// <param name="source">Fuente de muestras (debe ser mono o stereo).</param>
    /// <param name="fftLength">Tamaño del FFT (debe ser potencia de 2). Default: 1024.</param>
    public VisualizerEngine(ISampleProvider source, int fftLength = 1024)
    {
        _source = source;
        WaveFormat = source.WaveFormat;

        // Validar potencia de 2
        if ((fftLength & (fftLength - 1)) != 0)
            throw new ArgumentException("fftLength debe ser potencia de 2", nameof(fftLength));

        _fftLength = fftLength;
        _fftBuffer = new Complex[_fftLength];
        _window = FastFourierTransform.HammingWindow(_fftLength);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);

        if (!_enabled || read <= 0) return read;

        var channels = Math.Max(1, WaveFormat.Channels);

        // Acumular muestras mono (promedio de canales si es stereo)
        for (int i = 0; i < read; i += channels)
        {
            if (_bufferFill >= _fftLength) break;

            float sample = buffer[offset + i];
            if (channels == 2 && i + 1 < read)
                sample = (sample + buffer[offset + i + 1]) * 0.5f;

            // Aplicar ventana Hamming
            _fftBuffer[_bufferFill].X = sample * _window[_bufferFill];
            _fftBuffer[_bufferFill].Y = 0f;
            _bufferFill++;
        }

        // Si el buffer se llenó, ejecutar FFT y disparar evento
        if (_bufferFill >= _fftLength)
        {
            // Copiar el buffer porque FFT lo modifica in-place
            var fftCopy = new Complex[_fftLength];
            Array.Copy(_fftBuffer, fftCopy, _fftLength);

            FastFourierTransform.FFT(true, (int)Math.Log2(_fftLength), fftCopy);

            // Calcular magnitudes (solo primera mitad, simétrico)
            var magnitudes = new float[_fftLength / 2];
            for (int i = 0; i < _fftLength / 2; i++)
            {
                var re = fftCopy[i].X;
                var im = fftCopy[i].Y;
                magnitudes[i] = (float)Math.Sqrt(re * re + im * im);
            }

            SpectrumUpdated?.Invoke(this, magnitudes);
            _bufferFill = 0;
        }

        return read;
    }
}
