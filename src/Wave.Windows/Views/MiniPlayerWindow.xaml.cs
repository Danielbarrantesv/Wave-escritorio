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
                    PlayPauseIcon.Text = _playback.IsPlaying ? "⏸" : "▶");
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

    /// <summary>Botón derecho → cerrar mini player (volver a ventana principal).</summary>
    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        Close();
        base.OnMouseRightButtonUp(e);
    }
}
