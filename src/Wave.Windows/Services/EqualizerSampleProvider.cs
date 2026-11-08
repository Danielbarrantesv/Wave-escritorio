using NAudio.Wave;
using Wave.Windows.Models;

namespace Wave.Windows.Services;

/// <summary>
/// ISampleProvider que aplica un ecualizador de 5 bandas al flujo de audio.
///
/// Pipeline actualizado: reader → resampler → EqualizerSampleProvider → VolumeSampleProvider → waveOut
///
/// Implementa un filtro biquad peaking EQ por cada banda. Cada banda mantiene
/// su propio estado (xn1, xn2, yn1, yn2) para procesamiento por canal.
/// </summary>
public class EqualizerSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly EqualizerBand[] _bands;

    /// <summary>Coeficientes calculados para cada banda (b0, b1, b2, a1, a2).</summary>
    private readonly BiquadCoefficients[] _coeffs;

    /// <summary>Estado por banda por canal (2 canales: left/right).</summary>
    private readonly BiquadState[][] _states;

    /// <summary>True si el ecualizador está activo (false = bypass).</summary>
    public bool Enabled { get; set; } = true;

    public EqualizerSampleProvider(ISampleProvider source)
    {
        _source = source;
        WaveFormat = source.WaveFormat;

        // 5 bandas centradas en: 60Hz, 230Hz, 910Hz, 3600Hz, 14kHz
        // (valores del spec sección 11)
        _bands = new[]
        {
            new EqualizerBand { Frequency = 60,    Bandwidth = 0.5f, Gain = 0f },
            new EqualizerBand { Frequency = 230,   Bandwidth = 0.5f, Gain = 0f },
            new EqualizerBand { Frequency = 910,   Bandwidth = 0.5f, Gain = 0f },
            new EqualizerBand { Frequency = 3600,  Bandwidth = 0.5f, Gain = 0f },
            new EqualizerBand { Frequency = 14000, Bandwidth = 0.5f, Gain = 0f }
        };

        var channels = Math.Max(1, WaveFormat.Channels);
        _coeffs = new BiquadCoefficients[_bands.Length];
        _states = new BiquadState[_bands.Length][];
        for (int b = 0; b < _bands.Length; b++)
        {
            _states[b] = new BiquadState[channels];
            for (int c = 0; c < channels; c++)
                _states[b][c] = new BiquadState();
        }
        UpdateFilters();
    }

    public WaveFormat WaveFormat { get; }

    public EqualizerBand[] Bands => _bands;

    /// <summary>Cambia la ganancia (dB) de una banda y recalcula coeficientes.</summary>
    public void SetBandGain(int index, float gainDb)
    {
        if (index < 0 || index >= _bands.Length) return;
        _bands[index].Gain = gainDb;
        UpdateFilters();
    }

    /// <summary>Recalcula los coeficientes biquad de todas las bandas.</summary>
    private void UpdateFilters()
    {
        for (int b = 0; b < _bands.Length; b++)
        {
            _coeffs[b] = ComputePeakingCoeffs(_bands[b], WaveFormat.SampleRate);
        }
    }

    /// <summary>
    /// Calcula los coeficientes de un filtro biquad peaking EQ.
    /// Fórmula estándar del "Audio EQ Cookbook" de Robert Bristow-Johnson.
    /// </summary>
    private static BiquadCoefficients ComputePeakingCoeffs(EqualizerBand band, int sampleRate)
    {
        var A = MathF.Pow(10f, band.Gain / 40f); // amplitud
        var w0 = 2f * MathF.PI * band.Frequency / sampleRate;
        var cosW = MathF.Cos(w0);
        var sinW = MathF.Sin(w0);

        // alpha: dependiente del ancho de banda
        var alpha = sinW * MathF.Sinh(0.5f * band.Bandwidth * MathF.Log(2f) * w0 / sinW);

        var b0 = 1f + alpha * A;
        var b1 = -2f * cosW;
        var b2 = 1f - alpha * A;
        var a0 = 1f + alpha / A;
        var a1 = -2f * cosW;
        var a2 = 1f - alpha / A;

        // Normalizar por a0
        return new BiquadCoefficients
        {
            B0 = b0 / a0,
            B1 = b1 / a0,
            B2 = b2 / a0,
            A1 = a1 / a0,
            A2 = a2 / a0
        };
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var samplesRead = _source.Read(buffer, offset, count);
        if (!Enabled || samplesRead <= 0) return samplesRead;

        var channels = Math.Max(1, WaveFormat.Channels);

        // Aplicar cadena de filtros biquad en serie
        for (int i = 0; i < samplesRead; i += channels)
        {
            for (int c = 0; c < channels && i + c < samplesRead; c++)
            {
                var sample = buffer[offset + i + c];
                for (int b = 0; b < _bands.Length; b++)
                {
                    sample = ProcessBiquad(_coeffs[b], _states[b][c], sample);
                }
                // Clamp para evitar clipping si la ganancia es alta
                buffer[offset + i + c] = Math.Clamp(sample, -1f, 1f);
            }
        }
        return samplesRead;
    }

    /// <summary>Aplica un filtro biquad Direct Form I a una muestra.</summary>
    private static float ProcessBiquad(BiquadCoefficients c, BiquadState s, float x)
    {
        // Direct Form I: y[n] = b0*x[n] + b1*x[n-1] + b2*x[n-2] - a1*y[n-1] - a2*y[n-2]
        var y = c.B0 * x + c.B1 * s.X1 + c.B2 * s.X2 - c.A1 * s.Y1 - c.A2 * s.Y2;
        // Actualizar estado
        s.X2 = s.X1;
        s.X1 = x;
        s.Y2 = s.Y1;
        s.Y1 = y;
        return y;
    }

    /// <summary>Coeficientes normalizados de un filtro biquad.</summary>
    private struct BiquadCoefficients
    {
        public float B0, B1, B2, A1, A2;
    }

    /// <summary>Estado del filtro (últimas 2 muestras de entrada y salida).</summary>
    private class BiquadState
    {
        public float X1, X2, Y1, Y2;
    }
}
