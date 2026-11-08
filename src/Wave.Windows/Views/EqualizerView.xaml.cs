using System.Windows.Controls;
using Wave.Windows.ViewModels;

namespace Wave.Windows.Views;

/// <summary>
/// Vista del ecualizador. 5 sliders verticales para bandas: 60Hz, 230Hz, 910Hz, 3.6kHz, 14kHz.
/// </summary>
public partial class EqualizerView : UserControl
{
    private EqualizerViewModel _viewModel = null!;

    public EqualizerView()
    {
        InitializeComponent();
    }

    public EqualizerView(EqualizerViewModel viewModel) : this()
    {
        SetViewModel(viewModel);
    }

    /// <summary>Asigna el ViewModel y refresca las bandas desde el AudioEngine.</summary>
    public void SetViewModel(EqualizerViewModel viewModel)
    {
        if (_viewModel != null) return;
        _viewModel = viewModel;
        DataContext = _viewModel;
        _viewModel.RefreshFromEngine();
    }

    private void ToggleEnabled_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        _viewModel.ToggleEnabledCommand.Execute(null);
    }

    private void Reset_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        _viewModel.ResetBandsCommand.Execute(null);
    }
}

