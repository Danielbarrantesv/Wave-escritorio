using System.Text.Json;
using System.Windows;
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
            "library.importFiles" => await ImportFiles(p),

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

            // Playlists
            "playlists.getAll" => _playlistService.GetAllSorted(),
            "playlists.getSongs" => _playlistService.GetSongs(p.GetProperty("playlistId").GetInt64()),
            "playlists.toggleFavorite" => Invoke(() => _playlistService.ToggleFavorite(p.Deserialize<Song>(JsonOpts)!)),
            "playlists.create" => _playlistService.CreatePlaylist(p.GetProperty("name").GetString()!),
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
            "window.close" => Invoke(() => Application.Current.MainWindow.Close()),
            "window.toggleMiniPlayer" => ToggleMiniPlayer(),
            "window.openEqualizer" => OpenEqualizer(),

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
        var all = _library.GetAllSongs();
        var song = all.FirstOrDefault(s => s.Id == songId) ?? throw new ArgumentException($"Song {songId} not found");
        _playback.SetQueue(all.ToList(), all.IndexOf(song), autoPlay: true);
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
            else { _miniPlayer = new MiniPlayerWindow(_playback); _miniPlayer.Closed += (s, e) => _miniPlayer = null; _miniPlayer.Show(); }
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

    private static object? Invoke(Action a) { a(); return null; }
    private static async Task<object?> InvokeAsync(Func<Task> f) { await f(); return null; }
}
