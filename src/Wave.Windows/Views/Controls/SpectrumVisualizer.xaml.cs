using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Wave.Windows.Audio;

namespace Wave.Windows.Views.Controls;

/// <summary>
/// Visualizador de espectro de audio en tiempo real.
/// Dibuja barras verticales que representan las magnitudes FFT.
///
/// Se suscribe al evento SpectrumUpdated del VisualizerEngine.
/// Renderiza con DrawingContext + OnRender (más eficiente que crear Rectangles en XAML).
/// </summary>
public partial class SpectrumVisualizer : UserControl
{
    private float[] _spectrum = Array.Empty<float>();
    private const int BarCount = 32;

    /// <summary>Color de las barras (default: AccentBrush del tema).</summary>
    public Brush BarBrush { get; set; } = (Brush)Application.Current.FindResource("AccentBrush");

    /// <summary>Color de fondo de las barras cuando están inactivas.</summary>
    public Brush TrackBrush { get; set; } = (Brush)Application.Current.FindResource("OutlineBrush");

    /// <summary>VisualizerEngine al que suscribirse (se setea desde MainWindow).</summary>
    private VisualizerEngine? _visualizerEngine;

    public SpectrumVisualizer()
    {
        InitializeComponent();
    }

    /// <summary>Conecta este visualizador al VisualizerEngine del AudioEngine activo.</summary>
    public void AttachTo(VisualizerEngine? visualizer)
    {
        // Desuscribirse del anterior
        if (_visualizerEngine != null)
        {
            _visualizerEngine.SpectrumUpdated -= OnSpectrumUpdated;
        }

        _visualizerEngine = visualizer;
        if (_visualizerEngine != null)
        {
            _visualizerEngine.SpectrumUpdated += OnSpectrumUpdated;
        }
    }

    /// <summary>Recibe las magnitudes FFT y dispara un re-render.</summary>
    private void OnSpectrumUpdated(object? sender, float[] spectrum)
    {
        _spectrum = spectrum;
        // InvalidateVisual debe llamarse en el hilo UI
        Dispatcher.Invoke(InvalidateVisual);
    }

    /// <summary>Dibuja las barras del espectro usando DrawingContext.</summary>
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0) return;

        // Fondo opcional (track)
        var barWidth = width / BarCount;
        var gap = Math.Max(1, barWidth * 0.15);

        if (_spectrum.Length == 0)
        {
            // Estado idle: dibujar barras mínimas (1px de altura)
            for (int i = 0; i < BarCount; i++)
            {
                var x = i * barWidth;
                var barHeight = 2.0;
                var rect = new Rect(x + gap / 2, height - barHeight, barWidth - gap, barHeight);
                dc.DrawRectangle(TrackBrush, null, rect);
            }
            return;
        }

        // Mapear _spectrum (512 valores) a BarCount (32) barras
        // Usar escala logarítmica para la frecuencia (más natural al oído)
        for (int i = 0; i < BarCount; i++)
        {
            // Mapeo logarítmico: cada barra cubre un rango de frecuencias
            var ratio = (double)i / BarCount;
            var spectrumIdx = (int)(ratio * ratio * _spectrum.Length);
            spectrumIdx = Math.Clamp(spectrumIdx, 0, _spectrum.Length - 1);

            // Magnitud normalizada (amplificar para que se vea mejor)
            var magnitude = Math.Min(1.0, _spectrum[spectrumIdx] * 4.0);

            // Aplicar curva de decaimiento suave (las barras bajas son más altas)
            magnitude = Math.Sqrt(magnitude);

            var barHeight = magnitude * height;
            var x = i * barWidth;
            var rect = new Rect(x + gap / 2, height - barHeight, barWidth - gap, barHeight);
            dc.DrawRectangle(BarBrush, null, rect);
        }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        InvalidateVisual();
    }
}
