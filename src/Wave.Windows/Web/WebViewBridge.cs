using System.IO;
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
    private readonly PhoneLinkService? _phoneLink;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private Views.MiniPlayerWindow? _miniPlayer;
    private Views.EqualizerWindow? _equalizer;

    public WebViewBridge(CoreWebView2 webView, ILibraryService library, IPlaylistService playlistService,
        PlaybackController playback, ILyricsService lyrics, IBackupService backup,
        SettingsService settings, AudioEngine audioEngine, PhoneLinkService? phoneLink = null)
    {
        _webView = webView; _library = library; _playlistService = playlistService;
        _playback = playback; _lyrics = lyrics; _backup = backup;
        _settings = settings; _audioEngine = audioEngine; _phoneLink = phoneLink;
        _webView.WebMessageReceived += OnMessageReceived;
        _playback.PropertyChanged += OnPlaybackChanged;
        // Suscribirse al progreso de importación para enviarlo al JS
        _library.ScanProgress += OnScanProgress;
    }

    /// <summary>
    /// Envía el progreso de importación al JS como evento.
    /// El JS lo recibe y actualiza la barra de progreso overlay.
    /// </summary>
    private void OnScanProgress(object? sender, (int Current, int Total) e)
    {
        try
        {
            _webView.PostWebMessageAsString(JsonSerializer.Serialize(new
            {
                @event = "importProgress",
                current = e.Current,
                total = e.Total
            }, JsonOpts));
        }
        catch { /* ignore */ }
    }

    private async void OnMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        int? reqId = null;
        try
        {
            var json = e.TryGetWebMessageAsString();
            var msg = JsonSerializer.Deserialize<JsonElement>(json);
            var id = msg.GetProperty("id").GetInt32();
            reqId = id;
            var method = msg.GetProperty("method").GetString() ?? "";
            var paramsEl = msg.TryGetProperty("params", out var p) ? p : default;
            var result = await HandleMethod(method, paramsEl);
            var response = JsonSerializer.Serialize(new { id, result }, JsonOpts);
            _webView.PostWebMessageAsString(response);
        }
        catch (Exception ex)
        {
            WaveLogger.Error("Bridge error", ex);
            try { _webView.PostWebMessageAsString(JsonSerializer.Serialize(new { id = reqId, error = ex.Message }, JsonOpts)); } catch { }
        }
    }

    private async Task<object?> HandleMethod(string method, JsonElement p)
    {
        return method switch
        {
            // Library
            "library.getSongs" => GetActiveSongs(),
            "library.getSongCount" => _phoneLink?.IsPhoneViewActive == true ? _phoneLink.Songs.Count : _library.SongCount,
            "library.importFromFolder" => await ImportFromFolder(p),
            "library.importFromFolderToPlaylist" => await ImportFromFolderToPlaylist(p),
            "library.importFiles" => await ImportFiles(p),
            "library.deleteSong" => DeleteSong(p),
            "library.deleteSongAndFile" => DeleteSongAndFile(p),
            "library.moveSong" => MoveSong(p),
            "library.deleteAll" => Invoke(() => _library.DeleteAll()),
            "library.scanForNewFiles" => await ScanForNewFiles(),
            "library.editSong" => EditSong(p),
            "library.selectArtImage" => SelectArtImage(),
            "library.renameSongsByMetadata" => await RenameSongsByMetadata(p),
            "library.importFolderWithSubfolderPlaylists" => await ImportFolderWithSubfolderPlaylists(p),

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
            "playback.setRate" => Invoke(() => _audioEngine.PlaybackRate = p.GetProperty("rate").GetSingle()),
            "playback.setEqBand" => SetEqBand(p),
            "playback.setEqEnabled" => Invoke(() => _audioEngine.EqualizerEnabled = p.GetProperty("enabled").GetBoolean()),

            // Playlists
            "playlists.getAll" => GetActivePlaylists(),
            "playlists.getSongs" => GetPlaylistSongs(p.GetProperty("playlistId").GetInt64()),
            "playlists.toggleFavorite" => Invoke(() => {
                var song = p.Deserialize<Song>(JsonOpts)!;
                if (song.Id < 0) return;
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

            // Phone Link (Wave Android <-> PC)
            "phone.status" => _phoneLink?.GetStatus() ?? new { connected = false, active = false, deviceName = "", songCount = 0, playlistCount = 0, host = "", port = 45454 },
            "phone.connect" => await ConnectPhoneAsync(p),
            "phone.disconnect" => DisconnectPhone(),
            "phone.setViewActive" => SetPhoneViewActive(p),
            "phone.refresh" => await RefreshPhoneAsync(),

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
        IReadOnlyList<Song> sourceSongs = songId < 0 && _phoneLink != null
            ? _phoneLink.Songs
            : _library.GetAllSongs();

        if (p.TryGetProperty("songIds", out var idsEl) && idsEl.ValueKind == JsonValueKind.Array)
        {
            var ids = idsEl.Deserialize<List<long>>() ?? new List<long>();
            var byId = sourceSongs.ToDictionary(s => s.Id);
            queue = ids
                .Where(id => byId.ContainsKey(id))
                .Select(id => byId[id])
                .ToList();
            startIndex = queue.FindIndex(s => s.Id == songId);
        }
        else
        {
            queue = sourceSongs.ToList();
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
            void Send()
            {
                try { _webView.PostWebMessageAsString(JsonSerializer.Serialize(new { @event = "playbackChanged", state = GetPlaybackState() }, JsonOpts)); }
                catch { }
            }
            var disp = Application.Current?.Dispatcher;
            if (disp != null && !disp.CheckAccess()) disp.BeginInvoke((Action)Send);
            else Send();
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
                // Aplicar tema/accent al mini player recién creado
                var theme = _settings.Get("theme", "dark");
                var accentName = _settings.Get("accent", "green");
                var accentColor = accentName switch
                {
                    "red" => System.Windows.Media.Color.FromRgb(0xe8, 0x38, 0x38),
                    "orange" => System.Windows.Media.Color.FromRgb(0xff, 0x8c, 0x1a),
                    "yellow" => System.Windows.Media.Color.FromRgb(0xff, 0xcc, 0x00),
                    "blue" => System.Windows.Media.Color.FromRgb(0x1a, 0x8c, 0xff),
                    "cyan" => System.Windows.Media.Color.FromRgb(0x00, 0xd4, 0xff),
                    "magenta" => System.Windows.Media.Color.FromRgb(0xff, 0x2d, 0xa8),
                    "purple" => System.Windows.Media.Color.FromRgb(0x9c, 0x27, 0xff),
                    _ => System.Windows.Media.Color.FromRgb(0x32, 0xc9, 0x8b),
                };
                // Delay para que el árbol visual ya esté construido
                _miniPlayer.Dispatcher.BeginInvoke(new Action(() =>
                {
                    _miniPlayer.ApplyTheme(theme, accentColor);
                }), System.Windows.Threading.DispatcherPriority.Loaded);
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

    /// <summary>
    /// Borra una canción de la DB Y mueve el archivo físico a la papelera de reciclaje.
    /// Usa Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile con RecycleOption.
    /// Si la canción está reproduciéndose, pausa el playback y detiene el engine para
    /// que NAudio libere el handle del archivo antes de moverlo a la papelera.
    /// </summary>
    private object? DeleteSongAndFile(JsonElement p)
    {
        var songId = p.GetProperty("songId").GetInt64();
        var song = _library.GetAllSongs().FirstOrDefault(s => s.Id == songId);
        if (song == null) return new { success = false, error = "Canción no encontrada" };

        // Verificar si la canción está actualmente reproduciéndose
        bool wasCurrent = _playback.CurrentSong != null && _playback.CurrentSong.Id == songId;
        if (wasCurrent)
        {
            _playback.Pause();
            _audioEngine.Stop();
            WaveLogger.Info($"DeleteSongAndFile: paused/stopped playback for file delete (songId={songId})");
            // Pequeño delay para que NAudio libere el archivo
            System.Threading.Thread.Sleep(200);
        }

        // Borrar de la DB primero
        Wave.Windows.Data.SqliteDatabase.Instance.Connection.Execute(
            "DELETE FROM songs WHERE id = @songId", new { songId });
        Wave.Windows.Data.SqliteDatabase.Instance.Connection.Execute(
            "DELETE FROM playlist_songs WHERE song_id = @songId", new { songId });

        // Mover el archivo a la papelera de reciclaje
        if (!string.IsNullOrEmpty(song.FilePath) && File.Exists(song.FilePath))
        {
            try
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                    song.FilePath,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                WaveLogger.Info($"DeleteSongAndFile: moved to recycle bin: {song.FilePath}");

                // Si era la canción actual, avanzar a la siguiente de la cola
                // (el archivo ya no existe, no se puede reanudar la misma canción)
                if (wasCurrent)
                {
                    try
                    {
                        _playback.SkipToNext();
                        WaveLogger.Info("DeleteSongAndFile: skipped to next song after delete");
                    }
                    catch (Exception exNext)
                    {
                        WaveLogger.Error("DeleteSongAndFile: error skipping to next song", exNext);
                    }
                }

                return new { success = true, recycled = true };
            }
            catch (Exception ex)
            {
                WaveLogger.Error($"DeleteSongAndFile: error sending to recycle bin: {song.FilePath}", ex);
                // Si falló el reciclado pero era la canción actual, intentar reanudar
                if (wasCurrent)
                {
                    try { _audioEngine.Load(song.FilePath); _playback.Play(); }
                    catch { /* archivo ya no accesible */ }
                }
                return new { success = true, recycled = false, error = ex.Message };
            }
        }
        return new { success = true, recycled = false, error = "Archivo no encontrado" };
    }

    /// <summary>
    /// Mueve el archivo físico de una canción a otra carpeta.
    /// Actualiza el FilePath en la DB. Si el archivo ya existe en el destino, no lo sobrescribe.
    /// Si la canción está reproduciéndose, pausa, mueve, actualiza el path y reanuda.
    /// </summary>
    private object? MoveSong(JsonElement p)
    {
        var songId = p.GetProperty("songId").GetInt64();
        var destFolder = p.GetProperty("destFolder").GetString()!;

        if (string.IsNullOrEmpty(destFolder) || !Directory.Exists(destFolder))
            return new { success = false, error = "Carpeta de destino no existe" };

        var song = _library.GetAllSongs().FirstOrDefault(s => s.Id == songId);
        if (song == null) return new { success = false, error = "Canción no encontrada" };
        if (string.IsNullOrEmpty(song.FilePath) || !File.Exists(song.FilePath))
            return new { success = false, error = "Archivo no encontrado: " + song.FilePath };

        var fileName = Path.GetFileName(song.FilePath);
        var destPath = Path.Combine(destFolder, fileName);

        // Si ya existe en destino, añadir sufijo
        if (File.Exists(destPath))
        {
            var nameNoExt = Path.GetFileNameWithoutExtension(fileName);
            var ext = Path.GetExtension(fileName);
            destPath = Path.Combine(destFolder, $"{nameNoExt}_{DateTime.Now.Ticks}{ext}");
        }

        // Verificar si la canción está actualmente reproduciéndose
        bool wasPlaying = false;
        long positionMs = 0;
        bool wasPaused = false;
        if (_playback.CurrentSong != null && _playback.CurrentSong.Id == songId)
        {
            wasPlaying = _playback.IsPlaying;
            wasPaused = !wasPlaying; // está cargada pero pausada
            positionMs = _playback.PositionMs;
            // Pausar y liberar el archivo
            _playback.Pause();
            _audioEngine.Stop();
            WaveLogger.Info($"MoveSong: paused playback for file move (wasPlaying={wasPlaying}, pos={positionMs}ms)");
            // Pequeño delay para que NAudio libere el archivo
            System.Threading.Thread.Sleep(200);
        }

        try
        {
            File.Move(song.FilePath, destPath);
            // Actualizar DB con el nuevo path
            Wave.Windows.Data.SqliteDatabase.Instance.Connection.Execute(
                "UPDATE songs SET file_path = @filePath WHERE id = @songId",
                new { filePath = destPath, songId });
            // Actualizar el objeto en la cola de reproducción si está cargado
            if (_playback.CurrentSong != null && _playback.CurrentSong.Id == songId)
            {
                _playback.CurrentSong.FilePath = destPath;
            }
            WaveLogger.Info($"MoveSong: moved '{song.FilePath}' → '{destPath}'");

            // Reanudar reproducción si estaba sonando o cargada
            if (wasPlaying || wasPaused)
            {
                try
                {
                    _audioEngine.Load(destPath);
                    _playback.Seek(positionMs);
                    if (wasPlaying)
                    {
                        _audioEngine.Play();
                        // IsPlaying se actualiza via PropertyChanged
                    }
                    WaveLogger.Info($"MoveSong: resumed playback at {positionMs}ms from new path");
                }
                catch (Exception ex)
                {
                    WaveLogger.Error("MoveSong: error resuming playback", ex);
                }
            }

            return new { success = true, newPath = destPath };
        }
        catch (Exception ex)
        {
            WaveLogger.Error($"MoveSong: error moving file", ex);
            // Intentar reanudar aunque el move falló
            if (wasPlaying)
            {
                try { _audioEngine.Load(song.FilePath); _playback.Seek(positionMs); _audioEngine.Play(); } catch { }
            }
            return new { success = false, error = ex.Message };
        }
    }

    private object? EditSong(JsonElement p)
    {
        var songId = p.GetProperty("songId").GetInt64();
        var title = p.TryGetProperty("title", out var t) ? t.GetString() : null;
        var artist = p.TryGetProperty("artist", out var a) ? a.GetString() : null;
        var album = p.TryGetProperty("album", out var al) ? al.GetString() : null;
        var genre = p.TryGetProperty("genre", out var g) ? g.GetString() : null;
        var artPath = p.TryGetProperty("artPath", out var ap) ? ap.GetString() : null;

        // Update DB
        Wave.Windows.Data.SqliteDatabase.Instance.Connection.Execute(
            "UPDATE songs SET title = @title, artist = @artist, album = @album, genre = @genre WHERE id = @songId",
            new { title, artist, album, genre, songId });

        var song = _library.GetAllSongs().FirstOrDefault(s => s.Id == songId);
        if (song == null) return null;

        // Update art_uri in DB if new image provided
        if (!string.IsNullOrEmpty(artPath) && System.IO.File.Exists(artPath))
        {
            try
            {
                // Copy image to art cache
                var artDir = Wave.Windows.Audio.TagReader.ArtDirectory;
                var destPath = Path.Combine(artDir, $"song_{songId}.jpg");
                System.IO.File.Copy(artPath, destPath, true);
                Wave.Windows.Data.SqliteDatabase.Instance.Connection.Execute(
                    "UPDATE songs SET art_uri = @artUri WHERE id = @songId",
                    new { artUri = destPath, songId });
                song.ArtUri = destPath;
            }
            catch (Exception ex)
            {
                WaveLogger.Error("Error saving art image", ex);
            }
        }

        // Also update the actual file tags using TagLib#
        try
        {
            if (System.IO.File.Exists(song.FilePath))
            {
                var tagFile = TagLib.File.Create(song.FilePath);
                if (title != null) tagFile.Tag.Title = title;
                if (artist != null) tagFile.Tag.Performers = new[] { artist };
                if (album != null) tagFile.Tag.Album = album;
                if (genre != null) tagFile.Tag.Genres = new[] { genre };
                // Save cover art to file tags
                if (!string.IsNullOrEmpty(artPath) && System.IO.File.Exists(artPath))
                {
                    try
                    {
                        var pic = new TagLib.Picture(artPath);
                        tagFile.Tag.Pictures = new[] { pic };
                    }
                    catch { /* ignore image errors */ }
                }
                tagFile.Save();
            }
        }
        catch (Exception ex)
        {
            WaveLogger.Error("Error saving tags to file", ex);
        }
        return null;
    }

    /// <summary>Abre selector de imagen y retorna la ruta del archivo seleccionado.</summary>
    private object? SelectArtImage()
    {
        string? path = null;
        Application.Current.Dispatcher.Invoke(() =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Seleccionar imagen de portada",
                Filter = "Imágenes|*.jpg;*.jpeg;*.png;*.bmp;*.gif|Todos|*.*"
            };
            if (dialog.ShowDialog() == true) path = dialog.FileName;
        });
        if (string.IsNullOrEmpty(path)) return new { cancelled = true };
        return new { cancelled = false, path };
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

    /// <summary>
    /// Escanea las carpetas conocidas en busca de archivos nuevos.
    /// Se ejecuta en background (async). Retorna el número de canciones nuevas encontradas.
    /// </summary>
    private async Task<object?> ScanForNewFiles()
    {
        var added = await _library.ScanForNewFilesAsync();
        return new { added };
    }

    /// <summary>
    /// Importa una carpeta creando una playlist por cada subcarpeta.
    /// Si el params viene vacío, abre el diálogo nativo de selección de carpeta.
    /// </summary>
    private async Task<object?> ImportFolderWithSubfolderPlaylists(JsonElement p)
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
            if (string.IsNullOrEmpty(selectedPath))
                return new { cancelled = true, playlists = new List<object>() };
            folderPath = selectedPath;
        }

        var results = await _library.ImportFolderWithSubfolderPlaylistsAsync(folderPath);
        return new
        {
            cancelled = false,
            playlists = results.Select(r => new { name = r.PlaylistName, added = r.Added, duplicates = r.Duplicates }).ToList()
        };
    }

    /// <summary>
    /// Renombra TODAS las canciones de la biblioteca al patrón "Artista - Título.ext".
    /// Si la canción actual se está reproduciendo, la pausa y reanuda tras renombrar.
    /// </summary>
    private async Task<object?> RenameSongsByMetadata(JsonElement p)
    {
        // Si la canción actual está reproduciéndose, pausarla y liberar el archivo
        // antes de empezar el rename masivo (algunas canciones pueden ser la actual).
        bool wasPlaying = _playback.IsPlaying;
        long positionMs = _playback.PositionMs;
        long? currentSongId = _playback.CurrentSong?.Id;
        string? currentOldPath = _playback.CurrentSong?.FilePath;

        if (wasPlaying)
        {
            _playback.Pause();
            _audioEngine.Stop();
            WaveLogger.Info($"RenameSongsByMetadata: paused playback before bulk rename (pos={positionMs}ms)");
            System.Threading.Thread.Sleep(200);
        }

        var (renamed, skipped, errors) = await _library.RenameSongsByMetadataAsync();

        // Si la canción actual fue renombrada (su path cambió), recargarla con el nuevo path
        if (wasPlaying && currentSongId.HasValue)
        {
            try
            {
                // Leer el path actualizado desde la DB
                var updatedSong = _library.GetAllSongs().FirstOrDefault(s => s.Id == currentSongId.Value);
                if (updatedSong != null && !string.IsNullOrEmpty(updatedSong.FilePath)
                    && File.Exists(updatedSong.FilePath))
                {
                    // Actualizar el path en el objeto CurrentSong en memoria
                    _playback.CurrentSong.FilePath = updatedSong.FilePath;
                    WaveLogger.Info($"RenameSongsByMetadata: current song path updated to '{updatedSong.FilePath}'");
                }
                else
                {
                    // No se encontró o el archivo no existe; no se puede reanudar
                    WaveLogger.Warn($"RenameSongsByMetadata: current song path not found after rename, cannot resume");
                    return new { success = true, renamed, skipped, errors, resumed = false };
                }

                _audioEngine.Load(_playback.CurrentSong.FilePath);
                _playback.Seek(positionMs);
                _audioEngine.Play();
                WaveLogger.Info($"RenameSongsByMetadata: resumed playback at {positionMs}ms");
                return new { success = true, renamed, skipped, errors, resumed = true };
            }
            catch (Exception ex)
            {
                WaveLogger.Error("RenameSongsByMetadata: error resuming playback", ex);
                return new { success = true, renamed, skipped, errors, resumed = false, error = ex.Message };
            }
        }

        return new { success = true, renamed, skipped, errors };
    }

    private IReadOnlyList<Song> GetActiveSongs() =>
        _phoneLink?.IsPhoneViewActive == true ? _phoneLink.Songs : _library.GetAllSongs();

    private IReadOnlyList<Playlist> GetActivePlaylists() =>
        _phoneLink?.IsPhoneViewActive == true ? _phoneLink.Playlists : _playlistService.GetAllSorted();

    private IReadOnlyList<Song> GetPlaylistSongs(long playlistId)
    {
        if (playlistId < 0 && _phoneLink != null)
            return _phoneLink.GetPlaylistSongs(playlistId);
        return _playlistService.GetSongs(playlistId);
    }

    private async Task<object?> ConnectPhoneAsync(JsonElement p)
    {
        if (_phoneLink == null) throw new InvalidOperationException("Servicio de conexión no disponible.");
        var code = p.TryGetProperty("code", out var cEl) ? cEl.GetString() ?? "" : "";
        var host = p.TryGetProperty("host", out var hEl) ? hEl.GetString() : null;
        return await _phoneLink.ConnectAsync(code, host);
    }

    private object? DisconnectPhone()
    {
        _playback.ClearRemoteQueueIfActive();
        _phoneLink?.Disconnect();
        return _phoneLink?.GetStatus();
    }

    private object? SetPhoneViewActive(JsonElement p)
    {
        var active = p.TryGetProperty("active", out var aEl) && aEl.GetBoolean();
        _phoneLink?.SetPhoneViewActive(active);
        return _phoneLink?.GetStatus();
    }

    private async Task<object?> RefreshPhoneAsync()
    {
        if (_phoneLink == null) throw new InvalidOperationException("Servicio de conexión no disponible.");
        return await _phoneLink.RefreshAsync();
    }

    private static object? Invoke(Action a) { a(); return null; }
    private static async Task<object?> InvokeAsync(Func<Task> f) { await f(); return null; }
}
