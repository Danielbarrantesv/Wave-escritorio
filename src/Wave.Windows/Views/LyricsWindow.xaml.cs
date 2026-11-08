using System.Windows;
using Wave.Windows.ViewModels;

namespace Wave.Windows.Views;

/// <summary>
/// Ventana separada que muestra la vista de letras.
/// Permite tener la letra abierta mientras la biblioteca sigue visible en MainWindow.
/// </summary>
public partial class LyricsWindow : Window
{
    public LyricsWindow(LyricsViewModel viewModel)
    {
        InitializeComponent();
        LyricsView.SetViewModel(viewModel);
    }
}
