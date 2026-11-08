using Wave.Windows.Models;

namespace Wave.Windows.Services;

/// <summary>
/// Interfaz del servicio de backup/restore de la biblioteca.
/// </summary>
public interface IBackupService
{
    /// <summary>Exporta toda la biblioteca + playlists a un archivo JSON.</summary>
    Task ExportAsync(string outputPath, IProgress<int>? progress = null);

    /// <summary>Importa biblioteca + playlists desde un archivo JSON.</summary>
    Task ImportAsync(string inputPath, IProgress<int>? progress = null);
}

/// <summary>
/// DTO con todos los datos a exportar/importar.
/// </summary>
public record BackupData
{
    public int Version { get; set; } = 1;
    public long ExportDate { get; set; }
    public List<Song> Songs { get; set; } = new();
    public List<Playlist> Playlists { get; set; } = new();
    public List<PlaylistSong> PlaylistSongs { get; set; } = new();
}
