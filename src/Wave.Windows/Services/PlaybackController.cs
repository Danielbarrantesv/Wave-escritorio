using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using NAudio.Wave;
using Wave.Windows.Data.Repositories;
using Wave.Windows.Models;

namespace Wave.Windows.Services;

/// <summary>
/// Controlador principal de playback (singleton). Gestiona:
/// - Cola de canciones (Queue observable)
/// - Posición actual (CurrentIndex)
/// - Shuffle y repeat
/// - Posición (PositionMs / DurationMs) actualizada via DispatcherTimer
/// - Persistencia de cola al cerrar la app (RestoreSavedQueue)
///
/// Delega la reproducción real de audio a <see cref="AudioEngine"/>.
/// </summary>
public partial class PlaybackController : ObservableObject, IDisposable
{
    private readonly AudioEngine _engine;
    private readonly DispatcherTimer _positionTimer;
    private readonly ISongRepository _songRepo;
    private readonly SettingsService _settings;

    // Cola interna
    private List<Song> _queue = new();
    private int _currentIndex = -1;
    private List<int> _shuffleOrder = new();
    private int _shuffleIndex = -1;
    private bool _disposed;
    private long _lastLastPlayedUpdate;

    // Estado observable (UI bindea estos)
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private long _positionMs;
    [ObservableProperty] private long _durationMs;
    [ObservableProperty] private bool _isShuffle;
    [ObservableProperty] private bool _repeatOne;
    [ObservableProperty] private Song? _currentSong;

    /// <summary>Cola observable (UI la muestra en la lista "Up Next").</summary>
    public ObservableCollection<Song> Queue { get; } = new();

    /// <summary>Índice actual en la cola (para resaltado en UI).</summary>
    public int CurrentIndex => _currentIndex;

    /// <summary>Volumen de 0.0 a 1.0 (delegado a AudioEngine).</summary>
    public float Volume
    {
        get => _engine.Volume;
        set
        {
            _engine.Volume = value;
            _settings.Volume = value;
        }
    }

    public PlaybackController(SettingsService settings, AudioEngine engine, ISongRepository songRepo)
    {
        _settings = settings;
        _engine = engine;
        _songRepo = songRepo;

        // Cargar volumen persistente
        _engine.Volume = _settings.Volume;

        // Suscribirse a eventos del engine
        _engine.PlaybackStopped += OnPlaybackStopped;
        _engine.PositionChanged += OnPositionChanged;

        // Timer para refrescar PositionMs en la UI cada 200ms
        _positionTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _positionTimer.Tick += OnPositionTimerTick;
        _positionTimer.Start();
    }

    /// <summary>
    /// Reemplaza la cola y opcionalmente inicia reproducción.
    /// Marcado como virtual para permitir mockeo en tests (Moq requiere métodos overridable).
    /// </summary>
    /// <param name="songs">Lista de canciones a poner en cola.</param>
    /// <param name="startIndex">Índice inicial (default 0).</param>
    /// <param name="autoPlay">Si true, comienza a reproducir inmediatamente.</param>
    public virtual void SetQueue(List<Song> songs, int startIndex = 0, bool autoPlay = true)
    {
        if (songs == null || songs.Count == 0) return;

        _queue = songs.ToList();
        _currentIndex = Math.Clamp(startIndex, 0, _queue.Count - 1);

        Queue.Clear();
        foreach (var s in _queue) Queue.Add(s);

        if (IsShuffle) BuildShuffleOrder();

        LoadAndPlay(autoPlay);
    }

    /// <summary>Inicia la reproducción. Si no hay cola, no hace nada.</summary>
    public void Play()
    {
        if (CurrentSong == null)
        {
            if (_queue.Count > 0)
            {
                _currentIndex = 0;
                LoadAndPlay(true);
            }
            return;
        }
        _engine.Play();
        IsPlaying = true;
    }

    /// <summary>Pausa la reproducción manteniendo la posición.</summary>
    public void Pause()
    {
        _engine.Pause();
        IsPlaying = false;
    }

    /// <summary>Alterna entre Play y Pause.</summary>
    public void TogglePlayPause()
    {
        if (IsPlaying) Pause();
        else Play();
    }

    /// <summary>Salta a la siguiente canción (respeta shuffle y repeat).</summary>
    public void SkipToNext()
    {
        if (_queue.Count == 0) return;

        if (IsShuffle)
        {
            _shuffleIndex++;
            if (_shuffleIndex >= _shuffleOrder.Count)
            {
                if (RepeatOne || _settings.RepeatAll)
                {
                    _shuffleIndex = 0;
                    BuildShuffleOrder();
                }
                else
                {
                    Pause();
                    return;
                }
            }
            _currentIndex = _shuffleOrder[_shuffleIndex];
        }
        else
        {
            _currentIndex++;
            if (_currentIndex >= _queue.Count)
            {
                if (RepeatOne || _settings.RepeatAll)
                    _currentIndex = 0;
                else
                {
                    Pause();
                    return;
                }
            }
        }
        LoadAndPlay(true);
    }

    /// <summary>Salta a la canción anterior (respeta shuffle y repeat).</summary>
    public void SkipToPrevious()
    {
        if (_queue.Count == 0) return;

        // Si lleva más de 3s, reiniciar la canción actual (UX común)
        if (_engine.PositionMs > 3000)
        {
            _engine.Seek(0);
            return;
        }

        if (IsShuffle)
        {
            _shuffleIndex--;
            if (_shuffleIndex < 0) _shuffleIndex = _shuffleOrder.Count - 1;
            _currentIndex = _shuffleOrder[_shuffleIndex];
        }
        else
        {
            _currentIndex--;
            if (_currentIndex < 0) _currentIndex = _queue.Count - 1;
        }
        LoadAndPlay(true);
    }

    /// <summary>Salta a una canción específica por índice en la cola.</summary>
    public void JumpTo(int index)
    {
        if (index < 0 || index >= _queue.Count) return;
        _currentIndex = index;
        LoadAndPlay(true);
    }

    /// <summary>Salta a una posición específica (seek) en la canción actual.</summary>
    public void Seek(long positionMs) => _engine.Seek(positionMs);

    /// <summary>Alterna shuffle on/off.</summary>
    public void ToggleShuffle()
    {
        IsShuffle = !IsShuffle;
        if (IsShuffle) BuildShuffleOrder();
    }

    /// <summary>Alterna "repeat one" (repetir la canción actual).</summary>
    public void ToggleRepeatOne()
    {
        RepeatOne = !RepeatOne;
    }

    /// <summary>Construye el orden aleatorio para shuffle.</summary>
    private void BuildShuffleOrder()
    {
        var indices = Enumerable.Range(0, _queue.Count).ToList();
        var rng = new Random();
        // Fisher-Yates shuffle
        for (int i = indices.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (indices[i], indices[j]) = (indices[j], indices[i]);
        }
        // Asegurar que el primer elemento sea el actual (UX: no cambia de canción al activar shuffle)
        if (_currentIndex >= 0)
        {
            indices.Remove(_currentIndex);
            indices.Insert(0, _currentIndex);
        }
        _shuffleOrder = indices;
        _shuffleIndex = 0;
    }

    /// <summary>Carga la canción actual en AudioEngine y opcionalmente reproduce.</summary>
    private void LoadAndPlay(bool autoPlay)
    {
        if (_currentIndex < 0 || _currentIndex >= _queue.Count) return;
        CurrentSong = _queue[_currentIndex];
        try
        {
            _engine.Load(CurrentSong.FilePath);
            DurationMs = _engine.LengthMs;
            if (autoPlay)
            {
                _engine.Play();
                IsPlaying = true;
            }
        }
        catch (FileNotFoundException)
        {
            // El archivo fue borrado/movido.
            // Si autoPlay=true, saltar al siguiente (UX: no queremos quedarnos en una canción que no suena).
            // Si autoPlay=false, mantener el CurrentSong apuntado (puede ser que el archivo aparezca después).
            System.Diagnostics.Debug.WriteLine($"[Wave] Archivo no encontrado: {CurrentSong.FilePath}");
            if (autoPlay)
            {
                SkipToNext();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Wave] Error cargando {CurrentSong.FilePath}: {ex.Message}");
            if (autoPlay)
            {
                SkipToNext();
            }
        }
    }

    /// <summary>Manejador del evento PlaybackStopped del engine.</summary>
    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        // Si terminó naturalmente (no por Stop()) y llegó al final
        if (e.Exception == null && _engine.PositionMs >= _engine.LengthMs - 100)
        {
            if (RepeatOne)
            {
                _engine.Seek(0);
                _engine.Play();
            }
            else
            {
                SkipToNext();
            }
        }
        else
        {
            IsPlaying = false;
        }
    }

    private void OnPositionChanged(object? sender, EventArgs e)
    {
        PositionMs = _engine.PositionMs;
    }

    /// <summary>Tick del timer: refresca PositionMs y registra last_played_at cada ~3s.</summary>
    private void OnPositionTimerTick(object? sender, EventArgs e)
    {
        if (!_engine.IsPlaying) return;

        PositionMs = _engine.PositionMs;
        DurationMs = _engine.LengthMs;

        // Throttle: actualizar last_played_at en DB cada 3 segundos
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (now - _lastLastPlayedUpdate >= 3 && CurrentSong != null)
        {
            _songRepo.UpdateLastPlayedAt(CurrentSong.Id, now);
            _lastLastPlayedUpdate = now;
        }
    }

    /// <summary>
    /// Persiste la cola actual para restaurarla al reabrir la app.
    /// Llamar al cerrar la ventana principal.
    /// </summary>
    public void SaveQueueToPrefs()
    {
        if (_queue.Count == 0) return;
        var songIds = string.Join(",", _queue.Select(s => s.Id));
        _settings.Set("queue_song_ids", songIds);
        _settings.Set("queue_index", _currentIndex);
        _settings.Set("queue_saved_at", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    /// <summary>
    /// Restaura la cola desde prefs al iniciar la app.
    /// NO reproduce automáticamente (la app debe llamar a Play() si quiere).
    /// </summary>
    public void RestoreSavedQueue()
    {
        var songIdsStr = _settings.Get<string>("queue_song_ids");
        if (string.IsNullOrEmpty(songIdsStr)) return;

        var ids = songIdsStr
            .Split(',')
            .Select(s => long.TryParse(s, out var id) ? id : 0)
            .Where(id => id > 0)
            .ToList();
        if (ids.Count == 0) return;

        var allSongs = _songRepo.ListAll();
        var songs = ids
            .Select(id => allSongs.FirstOrDefault(s => s.Id == id))
            .Where(s => s != null)
            .Cast<Song>()
            .ToList();
        if (songs.Count == 0) return;

        var savedIndex = _settings.Get<int>("queue_index");
        SetQueue(songs, Math.Clamp(savedIndex, 0, songs.Count - 1), autoPlay: false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _positionTimer.Stop();
        SaveQueueToPrefs();
        _engine.Dispose();
    }
}
