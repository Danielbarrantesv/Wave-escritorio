using System.Windows;
using System.Windows.Input;
using Wave.Windows.Services;

namespace Wave.Windows.Views;

/// <summary>
/// Mini player flotante, topmost, sin borde, con esquinas redondeadas.
/// Permite arrastrar la ventana desde cualquier punto.
/// Click en cover o info abre el reproductor principal.
/// </summary>
public partial class MiniPlayerWindow : Window
{
    private readonly PlaybackController _playback;

    public MiniPlayerWindow(PlaybackController playback)
    {
        InitializeComponent();
        _playback = playback;
        DataContext = _playback;

        _playback.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(PlaybackController.IsPlaying))
            {
                Dispatcher.Invoke(() =>
                {
                    PlayPauseIcon.Data = _playback.IsPlaying
                        ? System.Windows.Media.Geometry.Parse("M6,19h4V5H6v14zm8-14v14h4V5h-4z")
                        : System.Windows.Media.Geometry.Parse("M8,19V5l11,7L8,19Zm2-7Zm0,3.35L15.25,12L10,8.65v6.7Z");
                });
            }
        };
    }

    /// <summary>Arrastrar ventana desde cualquier punto (excepto botones y cover).</summary>
    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    /// <summary>Click en cover → abrir reproductor principal.</summary>
    private void Cover_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        GoToMain();
    }

    /// <summary>Click en info (título/artista) → abrir reproductor principal.</summary>
    private void Info_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        GoToMain();
    }

    /// <summary>Abre la ventana principal y cierra el mini player.</summary>
    private void GoToMain()
    {
        foreach (Window w in System.Windows.Application.Current.Windows)
        {
            if (w is MainWindow main)
            {
                main.ShowFromTray();
                break;
            }
        }
        // No cerrar el mini player, solo mostrar el grande
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e) => _playback.TogglePlayPause();
    private void Prev_Click(object sender, RoutedEventArgs e) => _playback.SkipToPrevious();
    private void Next_Click(object sender, RoutedEventArgs e) => _playback.SkipToNext();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        Close();
        base.OnMouseRightButtonUp(e);
    }
}
