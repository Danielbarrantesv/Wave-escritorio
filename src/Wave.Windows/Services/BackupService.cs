using System.IO;
using System.Text.Json;
using Wave.Windows.Data.Repositories;
using Wave.Windows.Models;

namespace Wave.Windows.Services;

/// <summary>
/// Servicio que exporta/importa toda la biblioteca + playlists a JSON.
///
/// Formato del JSON:
/// {
///   "Version": 1,
///   "ExportDate": 1234567890,
///   "Songs": [...],
///   "Playlists": [...],
///   "PlaylistSongs": [...]
/// }
///
/// Útil para migrar entre máquinas o como respaldo antes de resetear.
/// </summary>
public sealed class BackupService : IBackupService
{
    private readonly ISongRepository _songRepo;
    private readonly IPlaylistRepository _playlistRepo;
    private readonly IPlaylistService _playlistService;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public BackupService(
        ISongRepository songRepo,
        IPlaylistRepository playlistRepo,
        IPlaylistService playlistService)
    {
        _songRepo = songRepo;
        _playlistRepo = playlistRepo;
        _playlistService = playlistService;
    }

    /// <summary>
    /// Exporta toda la biblioteca + playlists a un archivo JSON.
    /// </summary>
    public async Task ExportAsync(string outputPath, IProgress<int>? progress = null)
    {
        if (string.IsNullOrEmpty(outputPath))
            throw new ArgumentException("Output path requerido", nameof(outputPath));

        var backup = new BackupData
        {
            Version = 1,
            ExportDate = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Songs = _songRepo.ListAll(),
            Playlists = _playlistRepo.GetAll(),
            PlaylistSongs = GetAllPlaylistSongs()
        };

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(backup, JsonOptions);
        await File.WriteAllTextAsync(outputPath, json);

        progress?.Report(100);
    }

    /// <summary>
    /// Importa biblioteca + playlists desde un archivo JSON.
    /// Borra los datos actuales antes de importar.
    /// </summary>
    public async Task ImportAsync(string inputPath, IProgress<int>? progress = null)
    {
        if (!File.Exists(inputPath))
            throw new FileNotFoundException($"Archivo de backup no encontrado: {inputPath}");

        var json = await File.ReadAllTextAsync(inputPath);
        var backup = JsonSerializer.Deserialize<BackupData>(json)
            ?? throw new InvalidDataException("El archivo de backup no contiene JSON válido");

        // Limpiar DB actual
        _songRepo.DeleteAll();
        foreach (var pl in _playlistRepo.GetAll())
        {
            _playlistRepo.DeleteById(pl.Id);
        }

        // Reinsertar canciones
        var songIdMap = new Dictionary<long, long>(); // oldId → newId
        foreach (var song in backup.Songs)
        {
            var oldId = song.Id;
            var newId = _songRepo.Insert(song with { Id = 0 });
            songIdMap[oldId] = newId;
        }

        // Reinsertar playlists y sus asociaciones
        foreach (var pl in backup.Playlists)
        {
            var oldPlaylistId = pl.Id;
            var newPlaylistId = _playlistRepo.Insert(pl with { Id = 0 });

            // Buscar las asociaciones para esta playlist
            var associations = backup.PlaylistSongs
                .Where(ps => ps.PlaylistId == oldPlaylistId)
                .OrderBy(ps => ps.Position)
                .ToList();

            // Mapear songIds antiguos a nuevos
            var newSongIds = new List<long>();
            foreach (var ps in associations)
            {
                if (songIdMap.TryGetValue(ps.SongId, out var newSongId))
                    newSongIds.Add(newSongId);
            }

            if (newSongIds.Count > 0)
            {
                _playlistRepo.AddSongsToPlaylist(newPlaylistId, newSongIds);
            }
        }

        progress?.Report(100);
    }

    /// <summary>Obtiene todas las asociaciones playlist ↔ song con su posición.</summary>
    private List<PlaylistSong> GetAllPlaylistSongs()
    {
        var result = new List<PlaylistSong>();
        foreach (var pl in _playlistRepo.GetAll())
        {
            var songs = _playlistRepo.GetSongsInPlaylist(pl.Id);
            for (int i = 0; i < songs.Count; i++)
            {
                result.Add(new PlaylistSong
                {
                    PlaylistId = pl.Id,
                    SongId = songs[i].Id,
                    Position = i
                });
            }
        }
        return result;
    }
}
