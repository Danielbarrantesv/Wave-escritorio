using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Wave.Windows.Services;

namespace Wave.Windows.Views.Controls;

/// <summary>
/// PlayerBar con slider personalizado.
/// Usa DataContext para acceder al PlaybackController (no campo privado,
/// porque XAML crea el control con constructor sin parámetros).
/// </summary>
public partial class PlayerBar : UserControl
{
    private bool _isDragging;

    public PlayerBar()
    {
        InitializeComponent();
    }

    /// <summary>Obtiene el PlaybackController del DataContext.</summary>
    private PlaybackController? Playback => DataContext as PlaybackController;

    private void SeekBar_DragStarted(object sender, DragStartedEventArgs e)
    {
        _isDragging = true;
    }

    private void SeekBar_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (_isDragging && Playback != null)
        {
            Playback.Seek((long)SeekBar.Value);
        }
        _isDragging = false;
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        Playback?.TogglePlayPause();
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        Playback?.SkipToNext();
    }

    private void Prev_Click(object sender, RoutedEventArgs e)
    {
        Playback?.SkipToPrevious();
    }

    private void Shuffle_Click(object sender, RoutedEventArgs e)
    {
        Playback?.ToggleShuffle();
    }

    private void Repeat_Click(object sender, RoutedEventArgs e)
    {
        Playback?.ToggleRepeatOne();
    }
}
