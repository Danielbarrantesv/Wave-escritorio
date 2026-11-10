using System.Text.Json;
using System.Windows;
using Dapper;
using Microsoft.Web.WebView2.Core;
using Wave.Windows.Models;
using Wave.Windows.Services;
using Wave.Windows.ViewModels;
using Wave.Windows.Views;

namespace Wave.Windows.Web;

public class WebViewBridge
{
    private readonly CoreWebView2 _webView;
    private readonly ILibraryService _library;
    private readonly IPlaylistService _playlistService;
    private readonly PlaybackController _playback;
    private readonly ILyricsService _lyrics;
    private readonly IBackupService _backup;
    private readonly SettingsService _settings;
    private readonly AudioEngine _audioEngine;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private Views.MiniPlayerWindow? _miniPlayer;
    private Views.EqualizerWindow? _equalizer;

    public WebViewBridge(CoreWebView2 webView, ILibraryService library, IPlaylistService playlistService,
        PlaybackController playback, ILyricsService lyrics, IBackupService backup,
        SettingsService settings, AudioEngine audioEngine)
    {
        _webView = webView; _library = library; _playlistService = playlistService;
        _playback = playback; _lyrics = lyrics; _backup = backup;
        _settings = settings; _audioEngine = audioEngine;
        _webView.WebMessageReceived += OnMessageReceived;
        _playback.PropertyChanged += OnPlaybackChanged;
    }

    private async void OnMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var json = e.TryGetWebMessageAsString();
            var msg = JsonSerializer.Deserialize<JsonElement>(json);
            var id = msg.GetProperty("id").GetInt32();
            var method = msg.GetProperty("method").GetString() ?? "";
            var paramsEl = msg.TryGetProperty("params", out var p) ? p : default;
            var result = await HandleMethod(method, paramsEl);
            var response = JsonSerializer.Serialize(new { id, result }, JsonOpts);
            _webView.PostWebMessageAsString(response);
        }
        catch (Exception ex)
        {
            WaveLogger.Error("Bridge error", ex);
            try { _webView.PostWebMessageAsString(JsonSerializer.Serialize(new { error = ex.Message })); } catch { }
        }
    }

    private async Task<object?> HandleMethod(string method, JsonElement p)
    {
        return method switch
        {
            // Library
            "library.getSongs" => _library.GetAllSongs(),
            "library.getSongCount" => _library.SongCount,
            "library.importFromFolder" => await ImportFromFolder(p),
            "library.importFromFolderToPlaylist" => await ImportFromFolderToPlaylist(p),
            "library.importFiles" => await ImportFiles(p),
            "library.deleteSong" => DeleteSong(p),
            "library.deleteAll" => Invoke(() => _library.DeleteAll()),
            "library.editSong" => EditSong(p),

            // Playback
            "playback.play" => Invoke(() => _playback.Play()),
            "playback.pause" => Invoke(() => _playback.Pause()),
            "playback.togglePlayPause" => Invoke(() => _playback.TogglePlayPause()),
            "playback.next" => Invoke(() => _playback.SkipToNext()),
            "playback.prev" => Invoke(() => _playback.SkipToPrevious()),
            "playback.seek" => Invoke(() => _playback.Seek(p.GetProperty("positionMs").GetInt64())),
            "playback.toggleShuffle" => Invoke(() => _playback.ToggleShuffle()),
            "playback.toggleRepeat" => Invoke(() => _playback.ToggleRepeatOne()),
            "playback.getState" => GetPlaybackState(),
            "playback.playSong" => PlaySong(p),
            "playback.setVolume" => Invoke(() => _audioEngine.Volume = p.GetProperty("volume").GetSingle()),
            "playback.setEqBand" => SetEqBand(p),
            "playback.setEqEnabled" => Invoke(() => _audioEngine.EqualizerEnabled = p.GetProperty("enabled").GetBoolean()),

            // Playlists
            "playlists.getAll" => _playlistService.GetAllSorted(),
            "playlists.getSongs" => _playlistService.GetSongs(p.GetProperty("playlistId").GetInt64()),
            "playlists.toggleFavorite" => Invoke(() => {
                var song = p.Deserialize<Song>(JsonOpts)!;
                _playlistService.ToggleFavorite(song);
                // Si la canción que se toggleó es la que está reproduciéndose ahora,
                // actualizar _playback.CurrentSong.IsFavorite para que GetPlaybackState
                // devuelva el valor correcto al JS (botón corazón de la playerbar)
                if (_playback.CurrentSong != null && _playback.CurrentSong.Id == song.Id)
                {
                    _playback.CurrentSong.IsFavorite = song.IsFavorite;
                }
            }),
            "playlists.create" => _playlistService.CreatePlaylist(
                p.GetProperty("name").GetString()!,
                p.TryGetProperty("songIds", out var sIdsEl) ? sIdsEl.Deserialize<List<long>>() : null),
            "playlists.delete" => Invoke(() => _playlistService.Delete(p.GetProperty("playlistId").GetInt64())),
            "playlists.rename" => RenamePlaylist(p),
            "playlists.addSongs" => AddSongsToPlaylist(p),
            "playlists.removeSong" => Invoke(() => _playlistService.RemoveSong(p.GetProperty("playlistId").GetInt64(), p.GetProperty("songId").GetInt64())),

            // Lyrics
            "lyrics.get" => await _lyrics.GetLyricsAsync(p.Deserialize<Song>(JsonOpts)!),

            // Backup
            "backup.export" => await ExportBackup(p),

            // Settings
            "settings.get" => _settings.Get<string>(p.GetProperty("key").GetString()!, ""),
            "settings.set" => Invoke(() => _settings.Set(p.GetProperty("key").GetString()!, p.GetProperty("value").GetString()!)),

            // Window
            "window.minimize" => Invoke(() => Application.Current.MainWindow.WindowState = WindowState.Minimized),
            "window.maximize" => Invoke(() => Application.Current.MainWindow.WindowState = Application.Current.MainWindow.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized),
            "window.close" => Invoke(() => {
                // Marcar como confirmado por JS (el modal HTML ya se mostró) y cerrar
                if (Application.Current.MainWindow is MainWindow mw) mw.JsConfirmedExit = true;
                Application.Current.MainWindow.Close();
            }),
            "window.toggleMiniPlayer" => ToggleMiniPlayer(),
            "window.openEqualizer" => OpenEqualizer(),
            "window.exportLogs" => ExportLogs(),
            "window.selectFolder" => SelectFolder(),

            _ => throw new NotImplementedException($"Method '{method}' not found")
        };
    }

    private async Task<object?> ImportFromFolder(JsonElement p)
    {
        string folderPath = p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";
        if (string.IsNullOrEmpty(folderPath))
        {
            string? selectedPath = null;
            Application.Current.Dispatcher.Invoke(() =>
            {
                var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Selecciona la carpeta de música" };
                if (dialog.ShowDialog() == true) selectedPath = dialog.FolderName;
            });
            if (string.IsNullOrEmpty(selectedPath)) return new { added = 0, duplicates = 0, total = 0, cancelled = true };
            folderPath = selectedPath;
        }
        return await _library.ImportFromFolderAsync(folderPath);
    }

    private async Task<object?> ImportFiles(JsonElement p)
    {
        string[]? filePaths = null;
        Application.Current.Dispatcher.Invoke(() =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Selecciona archivos de música",
                Multiselect = true,
                Filter = "Audio|*.mp3;*.flac;*.aac;*.m4a;*.ogg;*.wav;*.wma|Todos|*.*"
            };
            if (dialog.ShowDialog() == true) filePaths = dialog.FileNames;
        });
        if (filePaths == null || filePaths.Length == 0)
            return new { added = 0, duplicates = 0, total = 0, cancelled = true };
        return await _library.ImportFilesAsync(filePaths.ToList());
    }

    private async Task<object?> ExportBackup(JsonElement p)
    {
        string path = p.ValueKind == JsonValueKind.Object && p.TryGetProperty("path", out var pathEl) ? pathEl.GetString() ?? "" : "";
        if (string.IsNullOrEmpty(path))
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "JSON|*.json", FileName = "wave-backup.json" };
                if (dialog.ShowDialog() == true) path = dialog.FileName;
            });
            if (string.IsNullOrEmpty(path)) return new { cancelled = true };
        }
        await _backup.ExportAsync(path);
        return new { success = true, path };
    }

    private object? PlaySong(JsonElement p)
    {
        var songId = p.GetProperty("songId").GetInt64();

        List<Song> queue;
        int startIndex;

        // Si el JS envía songIds, usar SOLO esos (preserva contexto de playlist/vista actual)
        // Esto es CRÍTICO para que shuffle/next/prev se mantengan dentro de la playlist actual.
        if (p.TryGetProperty("songIds", out var idsEl) && idsEl.ValueKind == JsonValueKind.Array)
        {
            var ids = idsEl.Deserialize<List<long>>() ?? new List<long>();
            var all = _library.GetAllSongs();
            var byId = all.ToDictionary(s => s.Id);
            queue = ids
                .Where(id => byId.ContainsKey(id))
                .Select(id => byId[id])
                .ToList();
            startIndex = queue.FindIndex(s => s.Id == songId);
        }
        else
        {
            // Sin contexto: usar toda la biblioteca (comportamiento legacy)
            queue = _library.GetAllSongs().ToList();
            startIndex = queue.FindIndex(s => s.Id == songId);
        }

        if (queue.Count == 0)
            throw new ArgumentException($"Song {songId} not found in queue context");

        if (startIndex < 0) startIndex = 0;

        // autoPlay opcional (default true). Si el JS lo setea en false, carga sin reproducir.
        bool autoPlay = true;
        if (p.TryGetProperty("autoPlay", out var apEl) && apEl.ValueKind == JsonValueKind.False)
            autoPlay = false;

        _playback.SetQueue(queue, startIndex, autoPlay: autoPlay);
        return null;
    }

    private object? SetEqBand(JsonElement p)
    {
        var index = p.GetProperty("index").GetInt32();
        var gain = p.GetProperty("gain").GetSingle();
        _audioEngine.Equalizer?.SetBandGain(index, gain);
        return null;
    }

    private object GetPlaybackState() => new
    {
        isPlaying = _playback.IsPlaying, positionMs = _playback.PositionMs,
        durationMs = _playback.DurationMs, isShuffle = _playback.IsShuffle,
        repeatOne = _playback.RepeatOne, volume = _audioEngine.Volume,
        currentSong = _playback.CurrentSong, queueCount = _playback.Queue.Count,
        currentIndex = _playback.CurrentIndex
    };

    private void OnPlaybackChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PlaybackController.IsPlaying) or nameof(PlaybackController.PositionMs)
            or nameof(PlaybackController.DurationMs) or nameof(PlaybackController.CurrentSong)
            or nameof(PlaybackController.IsShuffle) or nameof(PlaybackController.RepeatOne))
        {
            try { _webView.PostWebMessageAsString(JsonSerializer.Serialize(new { @event = "playbackChanged", state = GetPlaybackState() }, JsonOpts)); }
            catch { }
        }
    }

    private object? ToggleMiniPlayer()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_miniPlayer != null && _miniPlayer.IsLoaded) { _miniPlayer.Close(); _miniPlayer = null; }
            else
            {
                _miniPlayer = new MiniPlayerWindow(_playback);
                _miniPlayer.Closed += (s, e) => _miniPlayer = null;
                // Posicionar a 100px del fondo de la pantalla, centrado horizontalmente
                _miniPlayer.WindowStartupLocation = WindowStartupLocation.Manual;
                var screen = SystemParameters.WorkArea;
                _miniPlayer.Left = (screen.Width - _miniPlayer.Width) / 2;
                _miniPlayer.Top = screen.Height - _miniPlayer.Height - 100;
                _miniPlayer.Show();
            }
        });
        return null;
    }

    private object? OpenEqualizer()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_equalizer != null && _equalizer.IsLoaded) { _equalizer.Activate(); return; }
            _equalizer = new EqualizerWindow(new EqualizerViewModel(_audioEngine));
            _equalizer.Closed += (s, e) => _equalizer = null;
            _equalizer.Show();
        });
        return null;
    }

    private object? RenamePlaylist(JsonElement p)
    {
        var id = p.GetProperty("playlistId").GetInt64();
        var newName = p.GetProperty("newName").GetString()!;
        var repo = _playlistService.GetAll().FirstOrDefault(pl => pl.Id == id);
        if (repo != null)
        {
            repo.Name = newName;
            // Use repository update via reflection or direct DB
            Wave.Windows.Data.SqliteDatabase.Instance.Connection.Execute(
                "UPDATE playlists SET name = @name, updated_at = @ts WHERE id = @id",
                new { name = newName, ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), id });
        }
        return null;
    }

    private object? AddSongsToPlaylist(JsonElement p)
    {
        var playlistId = p.GetProperty("playlistId").GetInt64();
        var songIds = p.GetProperty("songIds").Deserialize<List<long>>()!;
        _playlistService.AddSongs(playlistId, songIds);
        return null;
    }

    private object? DeleteSong(JsonElement p)
    {
        var songId = p.GetProperty("songId").GetInt64();
        Wave.Windows.Data.SqliteDatabase.Instance.Connection.Execute(
            "DELETE FROM songs WHERE id = @songId", new { songId });
        return null;
    }

    private object? EditSong(JsonElement p)
    {
        var songId = p.GetProperty("songId").GetInt64();
        var title = p.TryGetProperty("title", out var t) ? t.GetString() : null;
        var artist = p.TryGetProperty("artist", out var a) ? a.GetString() : null;
        var album = p.TryGetProperty("album", out var al) ? al.GetString() : null;
        var genre = p.TryGetProperty("genre", out var g) ? g.GetString() : null;

        // Update DB
        Wave.Windows.Data.SqliteDatabase.Instance.Connection.Execute(
            "UPDATE songs SET title = @title, artist = @artist, album = @album, genre = @genre WHERE id = @songId",
            new { title, artist, album, genre, songId });

        // Also update the actual file tags using TagLib#
        try
        {
            var song = _library.GetAllSongs().FirstOrDefault(s => s.Id == songId);
            if (song != null && System.IO.File.Exists(song.FilePath))
            {
                var tagFile = TagLib.File.Create(song.FilePath);
                if (title != null) tagFile.Tag.Title = title;
                if (artist != null) tagFile.Tag.Performers = new[] { artist };
                if (album != null) tagFile.Tag.Album = album;
                if (genre != null) tagFile.Tag.Genres = new[] { genre };
                tagFile.Save();
            }
        }
        catch (Exception ex)
        {
            WaveLogger.Error("Error saving tags to file", ex);
        }
        return null;
    }

    private object? ExportLogs()
    {
        string? path = null;
        Application.Current.Dispatcher.Invoke(() =>
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Exportar logs de Wave",
                Filter = "Log|*.log|Texto|*.txt|Todos|*.*",
                FileName = "wave.log"
            };
            if (dialog.ShowDialog() == true) path = dialog.FileName;
        });
        if (string.IsNullOrEmpty(path)) return new { cancelled = true };
        try
        {
            var logPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Wave", "wave.log");
            if (System.IO.File.Exists(logPath))
                System.IO.File.Copy(logPath, path, true);
            return new { success = true, path };
        }
        catch (Exception ex)
        {
            WaveLogger.Error("Error exporting logs", ex);
            return new { success = false, error = ex.Message };
        }
    }

    private object? SelectFolder()
    {
        string? path = null;
        Application.Current.Dispatcher.Invoke(() =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Selecciona una carpeta de música"
            };
            if (dialog.ShowDialog() == true) path = dialog.FolderName;
        });
        if (string.IsNullOrEmpty(path)) return new { cancelled = true };
        return new { cancelled = false, path };
    }

    private async Task<object?> ImportFromFolderToPlaylist(JsonElement p)
    {
        var folderPath = p.GetProperty("folderPath").GetString()!;
        var playlistId = p.GetProperty("playlistId").GetInt64();

        if (string.IsNullOrWhiteSpace(folderPath))
            return new { added = 0, duplicates = 0, total = 0, cancelled = true, error = "Ruta vacía" };

        if (!System.IO.Directory.Exists(folderPath))
            return new { added = 0, duplicates = 0, total = 0, cancelled = true, error = "La carpeta no existe: " + folderPath };

        var result = await _library.ImportFromFolderAsync(folderPath, playlistId);
        return result;
    }

    private static object? Invoke(Action a) { a(); return null; }
    private static async Task<object?> InvokeAsync(Func<Task> f) { await f(); return null; }
}
