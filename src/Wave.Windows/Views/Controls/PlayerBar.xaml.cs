using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Wave.Windows.Services;

namespace Wave.Windows.Views.Controls;

/// <summary>
/// PlayerBar con slider personalizado.
/// La bolita del Thumb siempre está visible (no solo en hover como el default de WPF).
/// Durante el drag del Thumb, deshabilitamos el binding unidireccional para evitar jitter.
/// </summary>
public partial class PlayerBar : UserControl
{
    private readonly PlaybackController _playback;
    private bool _isDragging;

    /// <summary>Se dispara cuando el usuario hace click en Play/Pause.</summary>
    public event EventHandler? PlayPauseRequested;

    /// <summary>Se dispara cuando el usuario hace click en Siguiente.</summary>
    public event EventHandler? NextRequested;

    /// <summary>Se dispara cuando el usuario hace click en Anterior.</summary>
    public event EventHandler? PrevRequested;

    /// <summary>Se dispara cuando el usuario hace click en Shuffle.</summary>
    public event EventHandler? ShuffleRequested;

    /// <summary>Se dispara cuando el usuario hace click en Repeat.</summary>
    public event EventHandler? RepeatRequested;

    public PlayerBar()
    {
        InitializeComponent();
    }

    public PlayerBar(PlaybackController playback) : this()
    {
        _playback = playback;
        DataContext = _playback;
    }

    /// <summary>Inicia drag del slider: deshabilitar binding de PositionMs para evitar jitter.</summary>
    private void SeekBar_DragStarted(object sender, DragStartedEventArgs e)
    {
        _isDragging = true;
    }

    /// <summary>Termina drag: aplicar seek a PlaybackController y restaurar binding.</summary>
    private void SeekBar_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (_isDragging && _playback != null)
        {
            _playback.Seek((long)SeekBar.Value);
        }
        _isDragging = false;
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        _playback?.TogglePlayPause();
        PlayPauseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        _playback?.SkipToNext();
        NextRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Prev_Click(object sender, RoutedEventArgs e)
    {
        _playback?.SkipToPrevious();
        PrevRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Shuffle_Click(object sender, RoutedEventArgs e)
    {
        _playback?.ToggleShuffle();
        ShuffleRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Repeat_Click(object sender, RoutedEventArgs e)
    {
        _playback?.ToggleRepeatOne();
        RepeatRequested?.Invoke(this, EventArgs.Empty);
    }
}
