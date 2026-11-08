using System.IO;
using Windows.Media;
using Windows.Media.Playback;
using Windows.Storage.Streams;

namespace Wave.Windows.Services;

/// <summary>
/// Servicio que integra Wave con System Media Transport Controls (SMTC) de Windows.
///
/// SMTC es el overlay que aparece al presionar teclas multimedia (play/pause/next/prev)
/// o en el volume mixer de Windows. Muestra metadata (título, artista, álbum, portada)
/// y recibe comandos de playback desde cualquier fuente (teclado, Bluetooth, etc.).
///
/// Implementación:
/// - Usa MediaPlayer como puente con SMTC (NO reproduce audio real; NAudio lo hace)
/// - Solo sincroniza metadata + recibe comandos
/// - CommandManager captura play/pause/next/prev desde teclas multimedia
/// </summary>
public sealed class SmtcService : IDisposable
{
    private readonly PlaybackController _playback;
    private readonly MediaPlayer? _mediaPlayer;
    private bool _disposed;
    private bool _initialized;

    public SmtcService(PlaybackController playback)
    {
        _playback = playback;

        try
        {
            // MediaPlayer sirve como puente con SMTC
            _mediaPlayer = new MediaPlayer
            {
                Source = new MediaPlaybackList(),
                CommandManager = { IsEnabled = true }
            };

            // Suscribirse a comandos del SMTC
            _mediaPlayer.CommandManager.PlayReceived += (s, e) => _playback.Play();
            _mediaPlayer.CommandManager.PauseReceived += (s, e) => _playback.Pause();
            _mediaPlayer.CommandManager.NextReceived += (s, e) => _playback.SkipToNext();
            _mediaPlayer.CommandManager.PreviousReceived += (s, e) => _playback.SkipToPrevious();

            // Suscribirse a cambios de PlaybackController para actualizar SMTC
            _playback.PropertyChanged += OnPlaybackPropertyChanged;

            _initialized = true;
        }
        catch (Exception ex)
        {
            // En CI o entornos sin UI, la inicialización de MediaPlayer puede fallar.
            // La app continúa sin SMTC — el audio funciona normal vía NAudio.
            System.Diagnostics.Debug.WriteLine($"[Wave] SMTC no disponible: {ex.Message}");
            _initialized = false;
        }
    }

    /// <summary>True si SMTC se inicializó correctamente.</summary>
    public bool IsInitialized => _initialized;

    private void OnPlaybackPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!_initialized || _mediaPlayer == null) return;

        if (e.PropertyName == nameof(PlaybackController.CurrentSong))
        {
            SyncMetadata();
        }
        else if (e.PropertyName == nameof(PlaybackController.IsPlaying))
        {
            SyncPlaybackStatus();
        }
    }

    /// <summary>Sincroniza metadata (título, artista, álbum, portada) al SMTC.</summary>
    private void SyncMetadata()
    {
        if (!_initialized || _mediaPlayer == null) return;

        var song = _playback.CurrentSong;
        if (song == null) return;

        try
        {
            var controls = _mediaPlayer.SystemMediaTransportControls;
            controls.DisplayUpdater.Type = MediaPlaybackType.Music;

            var props = controls.DisplayUpdater.MusicProperties;
            props.Title = song.DisplayTitle;
            props.Artist = song.DisplayArtist;
            props.AlbumTitle = song.DisplayAlbum;
            props.TrackNumber = (uint)(song.TrackNo ?? 0);

            // Cover art (si existe ArtUri como path local)
            if (!string.IsNullOrEmpty(song.ArtUri) && File.Exists(song.ArtUri))
            {
                try
                {
                    var stream = RandomAccessStreamReference.CreateFromUri(new Uri(song.ArtUri));
                    controls.DisplayUpdater.Thumbnail = stream;
                }
                catch { /* ignore */ }
            }

            controls.DisplayUpdater.Update();
            SyncPlaybackStatus();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Wave] Error sync metadata SMTC: {ex.Message}");
        }
    }

    /// <summary>Sincroniza el estado de reproducción (Playing/Paused) al SMTC.</summary>
    private void SyncPlaybackStatus()
    {
        if (!_initialized || _mediaPlayer == null) return;

        try
        {
            _mediaPlayer.SystemMediaTransportControls.PlaybackStatus =
                _playback.IsPlaying
                    ? MediaPlaybackStatus.Playing
                    : MediaPlaybackStatus.Paused;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Wave] Error sync status SMTC: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_initialized)
        {
            _playback.PropertyChanged -= OnPlaybackPropertyChanged;
        }

        _mediaPlayer?.Dispose();
    }
}
