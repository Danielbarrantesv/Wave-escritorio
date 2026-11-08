using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Wave.Windows.Models;
using Wave.Windows.Services;
using Wave.Windows.ViewModels;
using Wave.Windows.Views;

namespace Wave.Windows.Web;

/// <summary>
/// Puente de comunicación entre C# (backend) y JavaScript (frontend WebView2).
/// Protocolo JSON-RPC simplificado con camelCase serialization.
/// </summary>
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

    // JSON options camelCase para que JS lea result.added, result.duplicates, etc.
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public WebViewBridge(
        CoreWebView2 webView,
        ILibraryService library,
        IPlaylistService playlistService,
        PlaybackController playback,
        ILyricsService lyrics,
        IBackupService backup,
        SettingsService settings,
        AudioEngine audioEngine)
    {
        _webView = webView;
        _library = library;
        _playlistService = playlistService;
        _playback = playback;
        _lyrics = lyrics;
        _backup = backup;
        _settings = settings;
        _audioEngine = audioEngine;

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
            WaveLogger.Error("WebViewBridge message error", ex);
            try
            {
                var errorJson = JsonSerializer.Serialize(new { error = ex.Message });
                _webView.PostWebMessageAsString(errorJson);
            }
            catch { }
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

            // Playlists
            "playlists.getAll" => _playlistService.GetAllSorted(),
            "playlists.getSongs" => _playlistService.GetSongs(p.GetProperty("playlistId").GetInt64()),
            "playlists.toggleFavorite" => Invoke(() =>
            {
                var song = p.Deserialize<Song>(JsonOpts);
                _playlistService.ToggleFavorite(song!);
            }),
            "playlists.create" => _playlistService.CreatePlaylist(p.GetProperty("name").GetString()!),

            // Lyrics
            "lyrics.get" => await _lyrics.GetLyricsAsync(p.Deserialize<Song>(JsonOpts)!),

            // Backup
            "backup.export" => await InvokeAsync(() => _backup.ExportAsync(p.GetProperty("path").GetString()!)),

            // Settings
            "settings.get" => _settings.Get<string>(p.GetProperty("key").GetString()!, ""),
            "settings.set" => Invoke(() => _settings.Set(p.GetProperty("key").GetString()!, p.GetProperty("value").GetString()!)),

            // Window management
            "window.openMiniPlayer" => OpenMiniPlayer(),
            "window.openEqualizer" => OpenEqualizer(),

            _ => throw new NotImplementedException($"Method '{method}' not implemented")
        };
    }

    /// <summary>
    /// Importa música desde una carpeta. Si no se especifica path, abre folder picker.
    /// </summary>
    private async Task<object?> ImportFromFolder(JsonElement p)
    {
        string folderPath = p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";

        // Si no hay path, abrir folder picker nativo de Windows
        if (string.IsNullOrEmpty(folderPath))
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Selecciona la carpeta de música",
                Multiselect = false
            };

            // Ejecutar en hilo UI
            string? selectedPath = null;
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (dialog.ShowDialog() == true)
                    selectedPath = dialog.FolderName;
            });

            if (string.IsNullOrEmpty(selectedPath))
                return new { added = 0, duplicates = 0, total = 0, cancelled = true };

            folderPath = selectedPath;
        }

        var result = await _library.ImportFromFolderAsync(folderPath);
        return result;
    }

    private object? PlaySong(JsonElement p)
    {
        var songId = p.GetProperty("songId").GetInt64();
        var allSongs = _library.GetAllSongs();
        var song = allSongs.FirstOrDefault(s => s.Id == songId) ?? throw new ArgumentException($"Song {songId} not found");
        var index = allSongs.IndexOf(song);
        _playback.SetQueue(allSongs.ToList(), index, autoPlay: true);
        return null;
    }

    private object GetPlaybackState() => new
    {
        isPlaying = _playback.IsPlaying,
        positionMs = _playback.PositionMs,
        durationMs = _playback.DurationMs,
        isShuffle = _playback.IsShuffle,
        repeatOne = _playback.RepeatOne,
        volume = _audioEngine.Volume,
        currentSong = _playback.CurrentSong,
        queueCount = _playback.Queue.Count,
        currentIndex = _playback.CurrentIndex
    };

    private void OnPlaybackChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PlaybackController.IsPlaying)
            or nameof(PlaybackController.PositionMs)
            or nameof(PlaybackController.DurationMs)
            or nameof(PlaybackController.CurrentSong)
            or nameof(PlaybackController.IsShuffle)
            or nameof(PlaybackController.RepeatOne))
        {
            try
            {
                var stateJson = JsonSerializer.Serialize(new { @event = "playbackChanged", state = GetPlaybackState() }, JsonOpts);
                _webView.PostWebMessageAsString(stateJson);
            }
            catch { }
        }
    }

    private static object? Invoke(Action action) { action(); return null; }
    private static async Task<object?> InvokeAsync(Func<Task> func) { await func(); return null; }

    private object? OpenMiniPlayer()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var mini = new Views.MiniPlayerWindow(_playback);
            mini.Show();
        });
        return null;
    }

    private object? OpenEqualizer()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var eq = new Views.EqualizerWindow(
                new ViewModels.EqualizerViewModel(_audioEngine));
            eq.Show();
        });
        return null;
    }
}
