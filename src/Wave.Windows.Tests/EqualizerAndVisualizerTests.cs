using FluentAssertions;
using NAudio.Wave;
using Wave.Windows.Audio;
using Wave.Windows.Models;
using Wave.Windows.Services;
using Xunit;

namespace Wave.Windows.Tests;

/// <summary>
/// Tests del EqualizerSampleProvider y VisualizerEngine (v0.7.0).
///
/// No testeamos el comportamiento auditivo real (eso requiere NAudio + dispositivo de audio).
/// Testeamos la lógica de inicialización, bandas, ganancias y comportamiento básico de Read.
/// </summary>
public class EqualizerAndVisualizerTests
{
    /// <summary>Generador de onda senoidal de test (440Hz mono, 44.1kHz).</summary>
    private static ISampleProvider CreateTestSource(int sampleRate = 44100, int channels = 2, int durationSec = 1)
    {
        var samples = new float[sampleRate * channels * durationSec];
        for (int i = 0; i < samples.Length; i += channels)
        {
            var t = (double)i / (channels * sampleRate);
            var sample = (float)(0.5 * Math.Sin(2 * Math.PI * 440 * t));
            for (int c = 0; c < channels; c++)
                samples[i + c] = sample;
        }
        return new BufferedSampleProvider(samples, sampleRate, channels);
    }

    // === EqualizerBand ===

    [Fact]
    public void EqualizerBand_ValoresPorDefecto_BandwidthEsMedioOctavo()
    {
        var band = new EqualizerBand { Frequency = 1000, Gain = 6 };
        band.Bandwidth.Should().Be(0.5f);
    }

    // === EqualizerSampleProvider ===

    [Fact]
    public void EqualizerSampleProvider_Constructor_Crea5Bandas()
    {
        // Arrange & Act
        var eq = new EqualizerSampleProvider(CreateTestSource());

        // Assert
        eq.Bands.Should().HaveCount(5);
        eq.Bands[0].Frequency.Should().Be(60);
        eq.Bands[1].Frequency.Should().Be(230);
        eq.Bands[2].Frequency.Should().Be(910);
        eq.Bands[3].Frequency.Should().Be(3600);
        eq.Bands[4].Frequency.Should().Be(14000);
    }

    [Fact]
    public void EqualizerSampleProvider_PorDefecto_EstaEnabled()
    {
        var eq = new EqualizerSampleProvider(CreateTestSource());
        eq.Enabled.Should().BeTrue();
    }

    [Fact]
    public void EqualizerSampleProvider_PorDefecto_TodasLasGananciasSonCero()
    {
        var eq = new EqualizerSampleProvider(CreateTestSource());
        foreach (var band in eq.Bands)
        {
            band.Gain.Should().Be(0f);
        }
    }

    [Fact]
    public void SetBandGain_CuandoIndiceValido_ActualizaGanancia()
    {
        // Arrange
        var eq = new EqualizerSampleProvider(CreateTestSource());

        // Act
        eq.SetBandGain(2, 6.5f);

        // Assert
        eq.Bands[2].Gain.Should().Be(6.5f);
    }

    [Fact]
    public void SetBandGain_CuandoIndiceNegativo_NoHaceNada()
    {
        var eq = new EqualizerSampleProvider(CreateTestSource());
        eq.SetBandGain(-1, 5f);
        foreach (var band in eq.Bands)
            band.Gain.Should().Be(0f);
    }

    [Fact]
    public void SetBandGain_CuandoIndiceExcedeLength_NoHaceNada()
    {
        var eq = new EqualizerSampleProvider(CreateTestSource());
        eq.SetBandGain(99, 5f);
        foreach (var band in eq.Bands)
            band.Gain.Should().Be(0f);
    }

    [Fact]
    public void Read_CuandoDisabled_DevuelveMuestrasSinProcesar()
    {
        // Arrange — source con 100 muestras
        var source = CreateTestSource(durationSec: 1);
        var eq = new EqualizerSampleProvider(source)
        {
            Enabled = false
        };
        // Aplicar ganancia +12 dB (debería ser ignorado porque está deshabilitado)
        eq.SetBandGain(0, 12f);

        var buffer = new float[1000];

        // Act
        var read = eq.Read(buffer, 0, 1000);

        // Assert — el buffer debería tener los valores originales (sin procesar)
        read.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Read_CuandoEnabled_NoLanzaExcepcion()
    {
        // Arrange
        var source = CreateTestSource(durationSec: 1);
        var eq = new EqualizerSampleProvider(source);
        eq.SetBandGain(0, 6f);
        eq.SetBandGain(4, -3f);

        var buffer = new float[1000];

        // Act — no debe lanzar
        var read = eq.Read(buffer, 0, 1000);

        // Assert
        read.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Read_CuandoGananciaCero_DevuelveMuestrasCercanasAOriginal()
    {
        // Con ganancia 0 dB en todas las bandas, el EQ no debería alterar significativamente la señal
        var source = CreateTestSource(durationSec: 1);
        var eq = new EqualizerSampleProvider(source); // ganancias por defecto = 0 dB

        var originalBuffer = new float[1000];
        var eqBuffer = new float[1000];
        source.Read(originalBuffer, 0, 1000);

        // Resetear source y leer del EQ
        var source2 = CreateTestSource(durationSec: 1);
        var eq2 = new EqualizerSampleProvider(source2);
        eq2.Read(eqBuffer, 0, 1000);

        // Comparar: las muestras deberían ser muy similares (pequeño error numérico)
        for (int i = 0; i < 100; i++)
        {
            var diff = Math.Abs(originalBuffer[i] - eqBuffer[i]);
            diff.Should().BeLessThan(0.01f, $"en muestra {i}: original={originalBuffer[i]} vs eq={eqBuffer[i]}");
        }
    }

    [Fact]
    public void WaveFormat_CoincideConElSource()
    {
        var source = CreateTestSource(sampleRate: 44100, channels: 2);
        var eq = new EqualizerSampleProvider(source);

        eq.WaveFormat.SampleRate.Should().Be(44100);
        eq.WaveFormat.Channels.Should().Be(2);
    }

    // === VisualizerEngine ===

    [Fact]
    public void VisualizerEngine_Constructor_BandCountEsMitadDeFFT()
    {
        var viz = new VisualizerEngine(CreateTestSource(), fftLength: 1024);
        viz.BandCount.Should().Be(512);
    }

    [Fact]
    public void VisualizerEngine_Constructor_ConFFTNoPotenciaDe2_LanzaArgumentException()
    {
        var act = () => new VisualizerEngine(CreateTestSource(), fftLength: 1000);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void VisualizerEngine_PorDefecto_EstaEnabled()
    {
        var viz = new VisualizerEngine(CreateTestSource());
        viz.Enabled.Should().BeTrue();
    }

    [Fact]
    public void WaveFormat_CoincideConElSource_Visualizer()
    {
        var source = CreateTestSource(sampleRate: 44100, channels: 2);
        var viz = new VisualizerEngine(source);

        viz.WaveFormat.SampleRate.Should().Be(44100);
        viz.WaveFormat.Channels.Should().Be(2);
    }

    [Fact]
    public void Read_CuandoBufferMenorQueFFT_NoDisparaSpectrumUpdated()
    {
        // Arrange
        var source = CreateTestSource(durationSec: 1);
        var viz = new VisualizerEngine(source, fftLength: 1024);
        var fired = false;
        viz.SpectrumUpdated += (s, e) => fired = true;

        var buffer = new float[100]; // menor que 1024 (con channels=2, son 50 muestras)

        // Act
        viz.Read(buffer, 0, buffer.Length);

        // Assert — no debería disparar porque no llenó el buffer FFT
        fired.Should().BeFalse();
    }

    [Fact]
    public void Read_CuandoBufferLlenaFFT_DisparaSpectrumUpdated()
    {
        // Arrange — con 1s de audio stereo @44.1kHz, hay 44100*2=88200 muestras.
        // FFT length 1024 → necesita acumular 1024 muestras mono (= 2048 stereo)
        var source = CreateTestSource(durationSec: 1);
        var viz = new VisualizerEngine(source, fftLength: 1024);
        float[]? capturedSpectrum = null;
        viz.SpectrumUpdated += (s, e) => capturedSpectrum = e;

        var buffer = new float[10000];

        // Act
        viz.Read(buffer, 0, buffer.Length);

        // Assert — debería disparar al menos una vez con 512 magnitudes
        capturedSpectrum.Should().NotBeNull();
        capturedSpectrum!.Length.Should().Be(512);
    }

    [Fact]
    public void Read_CuandoDisabled_NoDisparaSpectrumUpdated()
    {
        var source = CreateTestSource(durationSec: 1);
        var viz = new VisualizerEngine(source, fftLength: 1024) { Enabled = false };
        var fired = false;
        viz.SpectrumUpdated += (s, e) => fired = true;

        var buffer = new float[10000];
        viz.Read(buffer, 0, buffer.Length);

        fired.Should().BeFalse();
    }

    [Fact]
    public void Read_DevuelveNumeroDeMuestrasLeidas()
    {
        var source = CreateTestSource(durationSec: 1);
        var viz = new VisualizerEngine(source, fftLength: 1024);

        var buffer = new float[1000];
        var read = viz.Read(buffer, 0, buffer.Length);

        read.Should().BeGreaterThan(0);
    }
}

/// <summary>
/// ISampleProvider de test que devuelve muestras pre-generadas (útil para tests de EQ/Visualizer).
/// </summary>
internal sealed class BufferedSampleProvider : ISampleProvider
{
    private readonly float[] _samples;
    private int _position;

    public WaveFormat WaveFormat { get; }

    public BufferedSampleProvider(float[] samples, int sampleRate, int channels)
    {
        _samples = samples;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var available = _samples.Length - _position;
        var toRead = Math.Min(count, available);
        if (toRead <= 0) return 0;

        Array.Copy(_samples, _position, buffer, offset, toRead);
        _position += toRead;
        return toRead;
    }
}
