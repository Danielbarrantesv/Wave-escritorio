using Wave.Windows.Data.Repositories;
using Wave.Windows.Models;

namespace Wave.Windows.Services;

/// <summary>
/// Servicio de playlists. Gestiona:
/// - Playlist "Favoritos" (siempre presente, idempotente)
/// - Playlists automáticas por género (v2.5.24 feature)
/// - Playlists manuales del usuario
/// - Toggle de favoritos en canciones
///
/// Delega el acceso a DB a <see cref="IPlaylistRepository"/> y <see cref="ISongRepository"/>.
/// </summary>
public sealed class PlaylistService : IPlaylistService
{
    private readonly IPlaylistRepository _repo;
    private readonly ISongRepository _songRepo;

    public PlaylistService(IPlaylistRepository repo, ISongRepository songRepo)
    {
        _repo = repo;
        _songRepo = songRepo;
    }

    /// <summary>
    /// Todas las playlists ordenadas: Favoritos primero, luego por UpdatedAt DESC.
    /// </summary>
    public List<Playlist> GetAllSorted()
    {
        var all = _repo.GetAll();
        return all
            .OrderByDescending(p => p.IsFavorites)
            .ThenByDescending(p => p.UpdatedAt)
            .ToList();
    }

    /// <summary>Todas las playlists sin ordenar.</summary>
    public List<Playlist> GetAll() => _repo.GetAll();

    /// <summary>
    /// Crea la playlist "Favoritos" si no existe.
    /// Idempotente: se puede llamar en cada startup sin riesgo.
    /// </summary>
    public void EnsureFavoritesPlaylist()
    {
        if (_repo.GetByName(Playlist.FavoritesName) != null) return;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _repo.Insert(new Playlist
        {
            Name = Playlist.FavoritesName,
            Description = "default",
            CreatedAt = now,
            UpdatedAt = now
        });
    }

    /// <summary>
    /// Alterna el estado de favorito de una canción:
    /// 1. Actualiza el campo IsFavorite en DB (tabla songs)
    /// 2. Agrega/quita la canción de la playlist Favoritos
    /// </summary>
    public void ToggleFavorite(Song song)
    {
        song.IsFavorite = !song.IsFavorite;
        _songRepo.UpdateFavorite(song.Id, song.IsFavorite);

        var fav = _repo.GetByName(Playlist.FavoritesName);
        if (fav == null)
        {
            // Si por algún motivo Favoritos no existe, crearla primero
            EnsureFavoritesPlaylist();
            fav = _repo.GetByName(Playlist.FavoritesName);
        }
        if (fav == null) return;

        if (song.IsFavorite)
        {
            _repo.AddSongsToPlaylist(fav.Id, new List<long> { song.Id });
        }
        else
        {
            _repo.RemoveSong(fav.Id, song.Id);
        }
    }

    /// <summary>
    /// Crea una playlist nueva con el nombre dado.
    /// Si ya existe una playlist con ese nombre, retorna su ID sin modificar.
    /// </summary>
    /// <returns>ID de la playlist creada o existente.</returns>
    public long CreatePlaylist(string name, List<long>? songIds = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("El nombre de la playlist no puede estar vacío.", nameof(name));

        // Idempotente: si ya existe, retornar su ID
        var existing = _repo.GetByName(name);
        if (existing != null)
        {
            if (songIds != null && songIds.Count > 0)
                _repo.AddSongsToPlaylist(existing.Id, songIds);
            return existing.Id;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var id = _repo.Insert(new Playlist
        {
            Name = name,
            CreatedAt = now,
            UpdatedAt = now
        });

        if (songIds != null && songIds.Count > 0)
            _repo.AddSongsToPlaylist(id, songIds);

        return id;
    }

    /// <summary>
    /// CRITICAL (v2.5.24): crea playlists automáticas por género.
    /// Recorre todas las canciones con Genre no vacío, las agrupa, y para cada género
    /// crea una playlist (si no existe) y le agrega las canciones correspondientes.
    /// </summary>
    public void CreateGenrePlaylists()
    {
        var allSongs = _songRepo.ListAll();
        if (allSongs.Count == 0) return;

        // Agrupar por género (case-insensitive, trimmed)
        var byGenre = allSongs
            .Where(s => !string.IsNullOrWhiteSpace(s.Genre))
            .GroupBy(s => s.Genre!.Trim(), StringComparer.OrdinalIgnoreCase);

        foreach (var grp in byGenre)
        {
            // Usar el display name del primer song (preserva capitalización original)
            var displayName = grp.First().Genre!.Trim();
            var existing = _repo.GetByName(displayName);

            long playlistId;
            if (existing == null)
            {
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                playlistId = _repo.Insert(new Playlist
                {
                    Name = displayName,
                    Description = "auto-genre",
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }
            else
            {
                playlistId = existing.Id;
            }

            // Agregar todas las canciones del género (AddSongsToPlaylist deduplica automáticamente)
            _repo.AddSongsToPlaylist(playlistId, grp.Select(s => s.Id).ToList());
        }
    }

    /// <summary>Obtiene las canciones de una playlist, ordenadas por posición.</summary>
    public List<Song> GetSongs(long playlistId) => _repo.GetSongsInPlaylist(playlistId);

    /// <summary>
    /// Borra una playlist. La playlist "Favoritos" NO se puede borrar
    /// (protección a nivel de PlaylistRepository.DeleteById).
    /// </summary>
    public void Delete(long playlistId) => _repo.DeleteById(playlistId);

    /// <summary>Agrega canciones a una playlist existente (deduplica automáticamente).</summary>
    public void AddSongs(long playlistId, List<long> songIds) =>
        _repo.AddSongsToPlaylist(playlistId, songIds);

    /// <summary>Quita una canción de una playlist.</summary>
    public void RemoveSong(long playlistId, long songId) =>
        _repo.RemoveSong(playlistId, songId);
}
