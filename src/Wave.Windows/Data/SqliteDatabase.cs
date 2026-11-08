using System.Data;
using System.IO;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Wave.Windows.Data;

/// <summary>
/// Singleton que gestiona la conexión a SQLite y ejecuta migraciones.
/// </summary>
public sealed class SqliteDatabase : IDisposable
{
    private readonly SqliteConnection _connection;
    private static readonly object _lock = new();
    private static SqliteDatabase? _instance;

    /// <summary>Instancia singleton. Lanzar exception si no se ha inicializado.</summary>
    public static SqliteDatabase Instance =>
        _instance ?? throw new InvalidOperationException(
            "SqliteDatabase no inicializado. Llamar Init() primero.");

    /// <summary>Conexión subyacente (Dapper la usa directamente).</summary>
    public IDbConnection Connection => _connection;

    /// <summary>Inicializa la instancia singleton y ejecuta migraciones.</summary>
    /// <param name="dbPath">Ruta absoluta al archivo .db (se crea si no existe).</param>
    public static void Init(string dbPath)
    {
        lock (_lock)
        {
            if (_instance != null) return;
            _instance = new SqliteDatabase(dbPath);
            _instance.RunMigrations();
        }
    }

    private SqliteDatabase(string dbPath)
    {
        // Asegurar que el directorio exista
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var connectionString = $"Data Source={dbPath};Mode=ReadWriteCreate;Cache=Shared";
        _connection = new SqliteConnection(connectionString);
        _connection.Open();

        // Habilitar foreign keys (SQLite las desactiva por defecto)
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys = ON;";
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Ejecuta las migraciones pendientes. Si los archivos .sql no están disponibles
    /// (p.ej. en single-file publish), ejecuta el schema completo inline como fallback.
    /// </summary>
    private void RunMigrations()
    {
        // Primero crear la tabla 'migrations' si no existe (necesaria para track)
        _connection.Execute("""
            CREATE TABLE IF NOT EXISTS migrations (
                id              INTEGER PRIMARY KEY,
                name            TEXT NOT NULL,
                applied_at      INTEGER NOT NULL
            );
            """);

        var appliedMigrations = _connection
            .Query<int>("SELECT id FROM migrations").ToHashSet();

        // Intentar cargar migraciones desde archivos .sql
        var migrationsDir = Path.Combine(AppContext.BaseDirectory, "Data", "Migrations");
        var migrationFiles = Directory.Exists(migrationsDir)
            ? Directory.GetFiles(migrationsDir, "*.sql").OrderBy(f => f).ToList()
            : new List<string>();

        if (migrationFiles.Count > 0)
        {
            // Modo normal: aplicar archivos .sql uno por uno
            foreach (var file in migrationFiles)
            {
                var id = int.Parse(Path.GetFileName(file).Split('_')[0]);
                if (appliedMigrations.Contains(id)) continue;

                var sql = File.ReadAllText(file);
                using var tx = _connection.BeginTransaction();
                try
                {
                    _connection.Execute(sql, transaction: tx);
                    _connection.Execute(
                        "INSERT INTO migrations (id, name, applied_at) VALUES (@id, @name, @ts)",
                        new { id, name = Path.GetFileName(file), ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds() },
                        tx);
                    tx.Commit();
                }
                catch
                {
                    tx.Rollback();
                    throw;
                }
            }
        }
        else
        {
            // Fallback: ejecutar schema completo inline (single-file publish sin .sql)
            // Verificamos si ya se aplicó migration 1 para no re-ejecutar
            if (!appliedMigrations.Contains(1))
            {
                _connection.Execute(InlineSchema);
                _connection.Execute(
                    "INSERT INTO migrations (id, name, applied_at) VALUES (1, '001_initial_inline', @ts)",
                    new { ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
            }
        }
    }

    /// <summary>
    /// Schema SQL completo embebido en el código para cuando los archivos .sql
    /// no están disponibles (single-file publish).
    /// Debe ser idéntico a Data/Migrations/001_initial.sql.
    /// </summary>
    private const string InlineSchema = """
        CREATE TABLE IF NOT EXISTS songs (
            id              INTEGER PRIMARY KEY AUTOINCREMENT,
            file_path       TEXT NOT NULL UNIQUE,
            file_name       TEXT NOT NULL,
            file_size       INTEGER NOT NULL DEFAULT 0,
            title           TEXT,
            artist          TEXT,
            album           TEXT,
            duration_ms     INTEGER NOT NULL DEFAULT 0,
            track_no        INTEGER,
            year            INTEGER,
            mime_type       TEXT,
            date_added_sec  INTEGER NOT NULL DEFAULT 0,
            fingerprint     TEXT,
            art_uri         TEXT,
            last_modified   INTEGER NOT NULL DEFAULT 0,
            is_favorite     INTEGER NOT NULL DEFAULT 0,
            last_played_at  INTEGER NOT NULL DEFAULT 0,
            genre           TEXT
        );
        CREATE INDEX IF NOT EXISTS idx_songs_filepath ON songs(file_path);
        CREATE INDEX IF NOT EXISTS idx_songs_fingerprint ON songs(fingerprint);
        CREATE INDEX IF NOT EXISTS idx_songs_title_artist ON songs(title, artist);
        CREATE INDEX IF NOT EXISTS idx_songs_is_favorite ON songs(is_favorite);
        CREATE INDEX IF NOT EXISTS idx_songs_last_played ON songs(last_played_at);
        CREATE INDEX IF NOT EXISTS idx_songs_genre ON songs(genre);

        CREATE TABLE IF NOT EXISTS playlists (
            id              INTEGER PRIMARY KEY AUTOINCREMENT,
            name            TEXT NOT NULL UNIQUE,
            description     TEXT,
            created_at      INTEGER NOT NULL DEFAULT 0,
            updated_at      INTEGER NOT NULL DEFAULT 0,
            cover_uri       TEXT,
            folder_path     TEXT
        );
        CREATE INDEX IF NOT EXISTS idx_playlists_name ON playlists(name);

        CREATE TABLE IF NOT EXISTS playlist_songs (
            playlist_id     INTEGER NOT NULL,
            song_id         INTEGER NOT NULL,
            position        INTEGER NOT NULL DEFAULT 0,
            PRIMARY KEY (playlist_id, song_id),
            FOREIGN KEY (playlist_id) REFERENCES playlists(id) ON DELETE CASCADE,
            FOREIGN KEY (song_id)    REFERENCES songs(id)      ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS idx_playlist_songs_position ON playlist_songs(position);
        CREATE INDEX IF NOT EXISTS idx_playlist_songs_song_id ON playlist_songs(song_id);
        """;

    public void Dispose()
    {
        _connection?.Dispose();
        _instance = null;
    }
}
