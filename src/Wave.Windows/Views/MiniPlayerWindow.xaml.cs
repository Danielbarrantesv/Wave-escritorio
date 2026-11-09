using System.Windows;
using System.Windows.Input;
using Wave.Windows.Services;

namespace Wave.Windows.Views;

/// <summary>
/// Mini player flotante 320x80, topmost, sin borde, con esquinas redondeadas.
/// Permite arrastrar la ventana desde cualquier punto.
/// </summary>
public partial class MiniPlayerWindow : Window
{
    private readonly PlaybackController _playback;

    public MiniPlayerWindow(PlaybackController playback)
    {
        InitializeComponent();
        _playback = playback;
        DataContext = _playback;

        // Actualizar ícono Play/Pausa cuando cambia IsPlaying
        _playback.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(PlaybackController.IsPlaying))
            {
                Dispatcher.Invoke(() =>
                {
                    // Play: M8 5v14l11-7z  (triángulo)
                    // Pause: M6 19h4V5H6v14zm8-14v14h4V5h-4z  (dos barras)
                    PlayPauseIcon.Data = _playback.IsPlaying
                        ? System.Windows.Media.Geometry.Parse("M6,19h4V5H6v14zm8-14v14h4V5h-4z")
                        : System.Windows.Media.Geometry.Parse("M8,19V5l11,7L8,19Zm2-7Zm0,3.35L15.25,12L10,8.65v6.7Z");
                });
            }
        };
    }

    /// <summary>Permite arrastrar la ventana sin borde desde cualquier punto.</summary>
    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e) => _playback.TogglePlayPause();
    private void Prev_Click(object sender, RoutedEventArgs e) => _playback.SkipToPrevious();
    private void Next_Click(object sender, RoutedEventArgs e) => _playback.SkipToNext();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Botón derecho → cerrar mini player (volver a ventana principal).</summary>
    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        Close();
        base.OnMouseRightButtonUp(e);
    }
}
