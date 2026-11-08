using Wave.Windows.Models;

namespace Wave.Windows.Services;

/// <summary>
/// Interfaz del servicio de playlists.
/// Permite abstraer la lógica de playlists (Favoritos, género, manuales) para tests.
/// </summary>
public interface IPlaylistService
{
    /// <summary>Todas las playlists (ordenadas: Favoritos primero, luego por UpdatedAt).</summary>
    List<Playlist> GetAllSorted();

    /// <summary>Todas las playlists sin ordenar (raw).</summary>
    List<Playlist> GetAll();

    /// <summary>Crea la playlist "Favoritos" si no existe (idempotente).</summary>
    void EnsureFavoritesPlaylist();

    /// <summary>Marca/desmarca una canción como favorita. Actualiza DB + playlist Favoritos.</summary>
    void ToggleFavorite(Song song);

    /// <summary>Crea una playlist nueva con el nombre dado. Retorna el ID.</summary>
    long CreatePlaylist(string name, List<long>? songIds = null);

    /// <summary>
    /// CRITICAL (v2.5.24): crea playlists automáticas por género.
    /// Recorre todas las canciones, las agrupa por Genre y crea playlists si no existen.
    /// </summary>
    void CreateGenrePlaylists();

    /// <summary>Obtiene las canciones de una playlist ordenadas por posición.</summary>
    List<Song> GetSongs(long playlistId);

    /// <summary>Borra una playlist (no se puede borrar Favoritos).</summary>
    void Delete(long playlistId);

    /// <summary>Agrega canciones a una playlist existente.</summary>
    void AddSongs(long playlistId, List<long> songIds);

    /// <summary>Quita una canción de una playlist.</summary>
    void RemoveSong(long playlistId, long songId);
}
