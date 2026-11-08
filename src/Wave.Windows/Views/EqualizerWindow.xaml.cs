using System.Windows;
using Wave.Windows.ViewModels;

namespace Wave.Windows.Views;

/// <summary>
/// Ventana modal para el ecualizador.
/// </summary>
public partial class EqualizerWindow : Window
{
    public EqualizerWindow(EqualizerViewModel viewModel)
    {
        InitializeComponent();
        EqualizerView.SetViewModel(viewModel);
    }
}
