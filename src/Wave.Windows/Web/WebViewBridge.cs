using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Wave.Windows.Models;
using Wave.Windows.Services;

namespace Wave.Windows.Web;

/// <summary>
/// Puente de comunicación entre C# (backend) y JavaScript (frontend WebView2).
/// Protocolo JSON-RPC simplificado.
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
            var response = JsonSerializer.Serialize(new { id, result });
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
            "library.getSongs" => _library.GetAllSongs(),
            "library.getSongCount" => _library.SongCount,
            "library.importFromFolder" => await _library.ImportFromFolderAsync(p.ValueKind == JsonValueKind.String ? p.GetString()! : ""),

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

            "playlists.getAll" => _playlistService.GetAllSorted(),
            "playlists.getSongs" => _playlistService.GetSongs(p.GetProperty("playlistId").GetInt64()),
            "playlists.toggleFavorite" => Invoke(() => _playlistService.ToggleFavorite(p.Deserialize<Song>()!)),
            "playlists.create" => _playlistService.CreatePlaylist(p.GetProperty("name").GetString()!),

            "lyrics.get" => await _lyrics.GetLyricsAsync(p.Deserialize<Song>()!),
            "backup.export" => await _backup.ExportAsync(p.GetProperty("path").GetString()!),

            "settings.get" => _settings.Get<string>(p.GetProperty("key").GetString()!, ""),
            "settings.set" => Invoke(() => _settings.Set(p.GetProperty("key").GetString()!, p.GetProperty("value").GetString()!)),

            _ => throw new NotImplementedException($"Method '{method}' not implemented")
        };
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
                var stateJson = JsonSerializer.Serialize(new { @event = "playbackChanged", state = GetPlaybackState() });
                _webView.PostWebMessageAsString(stateJson);
            }
            catch { }
        }
    }

    private static object? Invoke(Action action) { action(); return null; }
}
