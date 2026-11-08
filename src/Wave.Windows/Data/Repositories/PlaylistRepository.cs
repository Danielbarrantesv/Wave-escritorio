using System.Data;
using Dapper;
using Wave.Windows.Models;

namespace Wave.Windows.Data.Repositories;

/// <summary>
/// Interfaz del repositorio de playlists.
/// Permite mockear en tests sin depender de SQLite.
/// </summary>
public interface IPlaylistRepository
{
    List<Playlist> GetAll();
    Playlist? GetById(long id);
    Playlist? GetByName(string name);
    long Insert(Playlist playlist);
    void Update(Playlist playlist);
    void DeleteById(long id);
    List<Song> GetSongsInPlaylist(long playlistId);
    void AddSongsToPlaylist(long playlistId, List<long> songIds);
    void RemoveSong(long playlistId, long songId);
    void ReorderPlaylist(long playlistId, List<long> songIdsInOrder);
}

/// <summary>
/// Implementación de IPlaylistRepository usando Dapper + SQLite.
/// </summary>
public class PlaylistRepository : IPlaylistRepository
{
    private readonly IDbConnection _db;

    public PlaylistRepository() : this(SqliteDatabase.Instance.Connection) { }

    public PlaylistRepository(IDbConnection db) => _db = db;

    public List<Playlist> GetAll() =>
        _db.Query<Playlist>("SELECT * FROM playlists ORDER BY updated_at DESC").ToList();

    public Playlist? GetById(long id) =>
        _db.QuerySingleOrDefault<Playlist>("SELECT * FROM playlists WHERE id = @id", new { id });

    public Playlist? GetByName(string name) =>
        _db.QuerySingleOrDefault<Playlist>(
            "SELECT * FROM playlists WHERE name = @name COLLATE NOCASE", new { name });

    public long Insert(Playlist playlist)
    {
        const string sql = """
            INSERT INTO playlists (name, description, created_at, updated_at, cover_uri, folder_path)
            VALUES (@Name, @Description, @CreatedAt, @UpdatedAt, @CoverUri, @FolderPath);
            SELECT last_insert_rowid();
            """;
        return _db.ExecuteScalar<long>(sql, playlist);
    }

    public void Update(Playlist playlist) =>
        _db.Execute(
            "UPDATE playlists SET name=@Name, description=@Description, updated_at=@UpdatedAt WHERE id=@Id",
            playlist);

    public void DeleteById(long id) =>
        _db.Execute("DELETE FROM playlists WHERE id = @id AND name != 'Favoritos'", new { id });

    public List<Song> GetSongsInPlaylist(long playlistId)
    {
        const string sql = """
            SELECT s.* FROM songs s
            INNER JOIN playlist_songs ps ON ps.song_id = s.id
            WHERE ps.playlist_id = @playlistId
            ORDER BY ps.position ASC
            """;
        return _db.Query<Song>(sql, new { playlistId }).ToList();
    }

    public void AddSongsToPlaylist(long playlistId, List<long> songIds)
    {
        if (songIds.Count == 0) return;

        var existing = _db.Query<long>(
            "SELECT song_id FROM playlist_songs WHERE playlist_id = @playlistId",
            new { playlistId }
        ).ToHashSet();

        var maxPos = _db.ExecuteScalar<int>(
            "SELECT COALESCE(MAX(position), -1) FROM playlist_songs WHERE playlist_id = @playlistId",
            new { playlistId }
        );

        var toAdd = songIds.Where(id => !existing.Contains(id)).ToList();
        if (toAdd.Count == 0) return;

        using var tx = _db.BeginTransaction();
        foreach (var songId in toAdd)
        {
            maxPos++;
            _db.Execute(
                "INSERT INTO playlist_songs (playlist_id, song_id, position) VALUES (@playlistId, @songId, @position)",
                new { playlistId, songId, position = maxPos }, tx);
        }
        _db.Execute("UPDATE playlists SET updated_at = @ts WHERE id = @playlistId",
            new { ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), playlistId }, tx);
        tx.Commit();
    }

    public void RemoveSong(long playlistId, long songId) =>
        _db.Execute(
            "DELETE FROM playlist_songs WHERE playlist_id = @playlistId AND song_id = @songId",
            new { playlistId, songId });

    public void ReorderPlaylist(long playlistId, List<long> songIdsInOrder)
    {
        using var tx = _db.BeginTransaction();
        _db.Execute("DELETE FROM playlist_songs WHERE playlist_id = @playlistId", new { playlistId }, tx);
        for (int i = 0; i < songIdsInOrder.Count; i++)
        {
            _db.Execute(
                "INSERT INTO playlist_songs (playlist_id, song_id, position) VALUES (@playlistId, @songId, @position)",
                new { playlistId, songId = songIdsInOrder[i], position = i }, tx);
        }
        _db.Execute("UPDATE playlists SET updated_at = @ts WHERE id = @playlistId",
            new { ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), playlistId }, tx);
        tx.Commit();
    }
}

