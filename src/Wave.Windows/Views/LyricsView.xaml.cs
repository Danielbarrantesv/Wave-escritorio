using System.Windows.Controls;
using Wave.Windows.ViewModels;

namespace Wave.Windows.Views;

/// <summary>
/// Vista de letras sincronizadas.
/// Muestra la portada del álbum como fondo con scrim oscuro, y las letras
/// con scroll automático siguiendo la posición del playback.
/// </summary>
public partial class LyricsView : UserControl
{
    private LyricsViewModel _viewModel = null!;

    public LyricsView()
    {
        InitializeComponent();
    }

    public LyricsView(LyricsViewModel viewModel) : this()
    {
        SetViewModel(viewModel);
    }

    /// <summary>Asigna el ViewModel. Puede llamarse después de InitializeComponent.</summary>
    public void SetViewModel(LyricsViewModel viewModel)
    {
        if (_viewModel != null) return; // ya asignado
        _viewModel = viewModel;
        DataContext = _viewModel;

        // Suscribirse a cambios de ActiveLineIndex para hacer auto-scroll
        _viewModel.PropertyChanged += async (s, e) =>
        {
            if (e.PropertyName == nameof(LyricsViewModel.ActiveLineIndex))
            {
                await Dispatcher.InvokeAsync(ScrollToActiveLine);
            }
        };
    }

    /// <summary>Hace scroll automático para mantener la línea activa visible (centrada).</summary>
    private void ScrollToActiveLine()
    {
        if (_viewModel.ActiveLineIndex < 0 || _viewModel.Lines.Count == 0) return;

        // Calcular offset aproximado: cada línea tiene ~50px de altura
        var lineOffset = _viewModel.ActiveLineIndex * 50.0;
        var viewportHeight = LyricsScroll.ActualHeight;
        var targetOffset = Math.Max(0, lineOffset - viewportHeight / 2);

        LyricsScroll.ScrollToVerticalOffset(targetOffset);
    }
}

