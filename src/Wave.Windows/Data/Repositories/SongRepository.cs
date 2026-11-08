using System.Data;
using Dapper;
using Wave.Windows.Models;

namespace Wave.Windows.Data.Repositories;

/// <summary>
/// Acceso a la tabla "songs" usando Dapper.
/// </summary>
public class SongRepository
{
    private readonly IDbConnection _db;

    public SongRepository() : this(SqliteDatabase.Instance.Connection) { }

    public SongRepository(IDbConnection db) => _db = db;

    public List<Song> ListAll()
    {
        const string sql = "SELECT * FROM songs ORDER BY date_added_sec DESC";
        return _db.Query<Song>(sql).ToList();
    }

    public Song? GetById(long id)
    {
        const string sql = "SELECT * FROM songs WHERE id = @id";
        return _db.QuerySingleOrDefault<Song>(sql, new { id });
    }

    public Song? GetByPath(string filePath)
    {
        const string sql = "SELECT * FROM songs WHERE file_path = @filePath";
        return _db.QuerySingleOrDefault<Song>(sql, new { filePath });
    }

    public List<Song> Search(string query)
    {
        const string sql = """
            SELECT * FROM songs
            WHERE title LIKE @q OR artist LIKE @q OR album LIKE @q
            ORDER BY title
            """;
        var q = $"%{query}%";
        return _db.Query<Song>(sql, new { q }).ToList();
    }

    public List<Song> GetFavorites()
    {
        const string sql = "SELECT * FROM songs WHERE is_favorite = 1 ORDER BY last_played_at DESC";
        return _db.Query<Song>(sql).ToList();
    }

    public List<Song> GetRecentlyPlayed(int limit = 50)
    {
        const string sql = """
            SELECT * FROM songs
            WHERE last_played_at > 0
            ORDER BY last_played_at DESC
            LIMIT @limit
            """;
        return _db.Query<Song>(sql, new { limit }).ToList();
    }

    public long Insert(Song song)
    {
        const string sql = """
            INSERT INTO songs (file_path, file_name, file_size, title, artist, album,
                duration_ms, track_no, year, mime_type, date_added_sec, fingerprint,
                art_uri, last_modified, is_favorite, last_played_at, genre)
            VALUES (@FilePath, @FileName, @FileSize, @Title, @Artist, @Album,
                @DurationMs, @TrackNo, @Year, @MimeType, @DateAddedSec, @Fingerprint,
                @ArtUri, @LastModified, @IsFavorite, @LastPlayedAt, @Genre);
            SELECT last_insert_rowid();
            """;
        return _db.ExecuteScalar<long>(sql, song);
    }

    public void UpdateFavorite(long songId, bool isFavorite)
    {
        const string sql = "UPDATE songs SET is_favorite = @isFavorite WHERE id = @songId";
        _db.Execute(sql, new { isFavorite = isFavorite ? 1 : 0, songId });
    }

    public void UpdateLastPlayedAt(long songId, long timestamp)
    {
        const string sql = "UPDATE songs SET last_played_at = @timestamp WHERE id = @songId";
        _db.Execute(sql, new { timestamp, songId });
    }

    public int Count() => _db.ExecuteScalar<int>("SELECT COUNT(*) FROM songs");

    public void Delete(long id)
    {
        _db.Execute("DELETE FROM songs WHERE id = @id", new { id });
    }

    public void DeleteAll()
    {
        _db.Execute("DELETE FROM songs");
    }
}
