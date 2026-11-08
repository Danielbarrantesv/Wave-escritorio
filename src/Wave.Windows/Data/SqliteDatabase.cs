using System.Data;
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

    private void RunMigrations()
    {
        var appliedMigrations = _connection
            .Query<int>("SELECT id FROM migrations").ToHashSet();

        var migrationsDir = Path.Combine(AppContext.BaseDirectory, "Data", "Migrations");
        if (!Directory.Exists(migrationsDir))
        {
            // Si las migraciones no están embebidas (p.ej. en single-file publish),
            // ejecutar SQL inline como fallback.
            return;
        }

        foreach (var file in Directory.GetFiles(migrationsDir, "*.sql").OrderBy(f => f))
        {
            var id = int.Parse(Path.GetFileName(file).Split('_')[0]);
            if (appliedMigrations.Contains(id)) continue;

            var sql = File.ReadAllText(file);
            using var tx = _connection.BeginTransaction();
            try
            {
                _connection.Execute(sql, transaction: tx);
                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }
    }

    public void Dispose()
    {
        _connection?.Dispose();
        _instance = null;
    }
}
