using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wave.Windows.Models;
using Wave.Windows.Services;

namespace Wave.Windows.ViewModels;

/// <summary>
/// ViewModel de la vista de letras.
/// Muestra letras sincronizadas con scroll automático siguiendo el playback.
///
/// Bindea al PlaybackController para:
/// - PositionMs (línea activa)
/// - CurrentSong (cargar letras al cambiar canción)
/// </summary>
public partial class LyricsViewModel : ObservableObject, IDisposable
{
    private readonly ILyricsService _lyricsService;
    private readonly PlaybackController _playback;
    private bool _disposed;

    /// <summary>Letras parseadas (null si no hay, vacías si no disponibles).</summary>
    [ObservableProperty]
    private ParsedLyrics? _lyrics;

    /// <summary>True si la canción actual tiene letras cargadas.</summary>
    [ObservableProperty]
    private bool _hasLyrics;

    /// <summary>True si estamos cargando letras desde la API.</summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>True si la búsqueda falló o no hay letra disponible.</summary>
    [ObservableProperty]
    private bool _lyricsNotFound;

    /// <summary>Índice de la línea activa (para resaltar).</summary>
    [ObservableProperty]
    private int _activeLineIndex = -1;

    /// <summary>Lista observable de líneas para binding de ItemsControl.</summary>
    public ObservableCollection<LyricLineViewModel> Lines { get; } = new();

    /// <summary>Canción actual (bindea a CurrentSong del PlaybackController).</summary>
    [ObservableProperty]
    private Song? _currentSong;

    public LyricsViewModel(ILyricsService lyricsService, PlaybackController playback)
    {
        _lyricsService = lyricsService;
        _playback = playback;

        // Suscribirse a cambios de canción y posición
        _playback.PropertyChanged += OnPlaybackPropertyChanged;
    }

    private async void OnPlaybackPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlaybackController.CurrentSong))
        {
            // Canción cambió → cargar letras
            CurrentSong = _playback.CurrentSong;
            await LoadLyricsAsync();
        }
        else if (e.PropertyName == nameof(PlaybackController.PositionMs))
        {
            UpdateActiveLine();
        }
    }

    /// <summary>Carga las letras para la canción actual.</summary>
    [RelayCommand]
    private async Task LoadLyricsAsync()
    {
        if (CurrentSong == null)
        {
            Lyrics = null;
            HasLyrics = false;
            LyricsNotFound = false;
            Lines.Clear();
            return;
        }

        IsLoading = true;
        LyricsNotFound = false;
        Lines.Clear();

        try
        {
            Lyrics = await _lyricsService.GetLyricsAsync(CurrentSong);

            if (Lyrics == null || Lyrics.IsEmpty)
            {
                HasLyrics = false;
                LyricsNotFound = true;
                return;
            }

            HasLyrics = true;
            // Poblar líneas
            foreach (var line in Lyrics.Lines)
            {
                Lines.Add(new LyricLineViewModel(line, isActive: false));
            }
            UpdateActiveLine();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Wave] Error cargando letras: {ex.Message}");
            HasLyrics = false;
            LyricsNotFound = true;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Actualiza el índice de línea activa basado en PositionMs.</summary>
    private void UpdateActiveLine()
    {
        if (Lyrics == null || !Lyrics.IsSynced || CurrentSong == null)
        {
            if (ActiveLineIndex != -1)
            {
                ActiveLineIndex = -1;
                RefreshLinesActive(-1);
            }
            return;
        }

        var newIndex = _lyricsService.ActiveLineIndex(Lyrics, _playback.PositionMs);
        if (newIndex != ActiveLineIndex)
        {
            ActiveLineIndex = newIndex;
            RefreshLinesActive(newIndex);
        }
    }

    /// <summary>Actualiza el flag IsActive de cada línea.</summary>
    private void RefreshLinesActive(int activeIndex)
    {
        for (int i = 0; i < Lines.Count; i++)
        {
            Lines[i].IsActive = i == activeIndex;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _playback.PropertyChanged -= OnPlaybackPropertyChanged;
    }
}

/// <summary>
/// ViewModel de cada línea de letra (texto + flag de activa).
/// </summary>
public partial class LyricLineViewModel : ObservableObject
{
    public string Text { get; }
    public long TimestampMs { get; }

    [ObservableProperty]
    private bool _isActive;

    public LyricLineViewModel(LyricLine line, bool isActive = false)
    {
        Text = line.Text;
        TimestampMs = line.TimestampMs;
        IsActive = isActive;
    }
}
