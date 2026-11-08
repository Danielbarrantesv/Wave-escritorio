using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Wave.Windows.Services;

namespace Wave.Windows.Views;

/// <summary>
/// Ventana principal de Wave.
/// v0.2.0: integra PlaybackController para que el PlayerBar refleje estado real.
/// v0.6.0+: implementará navegación entre LibraryView, NowPlayingView, etc.
/// </summary>
public partial class MainWindow : Window
{
    private readonly PlaybackController _playback;

    public MainWindow()
    {
        InitializeComponent();

        // Resolver PlaybackController desde DI (configurado en App.xaml.cs)
        _playback = App.Services.GetRequiredService<PlaybackController>();

        // Bindear el DataContext para que XAML vea IsPlaying, CurrentSong, etc.
        DataContext = _playback;
    }

    /// <summary>Botón Play/Pausa del PlayerBar.</summary>
    private void PlayPause_Click(object sender, RoutedEventArgs e)
        => _playback.TogglePlayPause();

    /// <summary>Botón "Siguiente" del PlayerBar.</summary>
    private void Next_Click(object sender, RoutedEventArgs e)
        => _playback.SkipToNext();

    /// <summary>Botón "Anterior" del PlayerBar.</summary>
    private void Prev_Click(object sender, RoutedEventArgs e)
        => _playback.SkipToPrevious();

    /// <summary>Botón Shuffle.</summary>
    private void Shuffle_Click(object sender, RoutedEventArgs e)
        => _playback.ToggleShuffle();

    /// <summary>Botón Repeat.</summary>
    private void Repeat_Click(object sender, RoutedEventArgs e)
        => _playback.ToggleRepeatOne();
}
