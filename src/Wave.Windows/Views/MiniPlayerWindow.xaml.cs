using System.Windows;
using System.Windows.Controls.Primitives;
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
    private bool _isDraggingProgress = false;
    private long _seekTargetMs = 0;

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
            else if (e.PropertyName == nameof(PlaybackController.PositionMs) ||
                     e.PropertyName == nameof(PlaybackController.DurationMs))
            {
                Dispatcher.Invoke(() =>
                {
                    if (_isDraggingProgress) return;  // No actualizar mientras el usuario arrastra
                    var dur = _playback.DurationMs;
                    var pos = _playback.PositionMs;
                    if (dur > 0)
                    {
                        var pct = Math.Min(100, (pos * 100.0) / dur);
                        ProgressBar.Value = pct;
                        ProgressFill.Value = pct;
                    }
                    else
                    {
                        ProgressBar.Value = 0;
                        ProgressFill.Value = 0;
                    }
                });
            }
        };
    }

    /// <summary>El usuario empezó a arrastrar la bolita del slider.</summary>
    private void ProgressBar_DragStarted(object sender, DragStartedEventArgs e)
    {
        _isDraggingProgress = true;
    }

    /// <summary>El usuario soltó la bolita del slider — hacer seek a la posición final.</summary>
    private void ProgressBar_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        _isDraggingProgress = false;
        var dur = _playback.DurationMs;
        var target = (long)(dur * ProgressBar.Value / 100.0);
        _playback.Seek(target);
    }

    /// <summary>Cambio el valor del slider mientras se arrastra — actualizar seekTarget + ProgressFill.</summary>
    private void ProgressBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isDraggingProgress && _playback.DurationMs > 0)
        {
            _seekTargetMs = (long)(_playback.DurationMs * ProgressBar.Value / 100.0);
            // Sincronizar el ProgressFill visual con el slider mientras se arrastra
            ProgressFill.Value = ProgressBar.Value;
        }
    }

    /// <summary>Arrastrar ventana desde cualquier punto (excepto botones y cover).</summary>
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

    /// <summary>Click en cover → abrir reproductor principal (no cerrar mini).</summary>
    private void Cover_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ShowMain();
    }

    /// <summary>Click en info → abrir reproductor principal (no cerrar mini).</summary>
    private void Info_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ShowMain();
    }

    private DateTime _lastShowMainClick = DateTime.MinValue;

    /// <summary>
    /// Click en cover/info: alterna entre mostrar/ocultar el reproductor principal.
    /// Si el principal ya está visible y activo (último click < 1.5s), lo minimiza.
    /// Si no, lo muestra.
    /// </summary>
    private void ShowMain()
    {
        var now = DateTime.UtcNow;
        var isDoubleClick = (now - _lastShowMainClick).TotalMilliseconds < 1500;
        _lastShowMainClick = now;

        foreach (Window w in System.Windows.Application.Current.Windows)
        {
            if (w is MainWindow main)
            {
                if (isDoubleClick && main.WindowState != WindowState.Minimized && main.IsActive)
                {
                    // Ya estaba visible y activo: minimizar
                    main.WindowState = WindowState.Minimized;
                }
                else
                {
                    main.ShowFromTray();
                }
                break;
            }
        }
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        Close();
        base.OnMouseRightButtonUp(e);
    }
}
