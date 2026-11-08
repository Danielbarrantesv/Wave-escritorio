using Wave.Windows.Models;

namespace Wave.Windows.Services;

/// <summary>
/// Interfaz del servicio de biblioteca musical.
/// Permite abstraer la importación y consulta de canciones para tests.
/// </summary>
public interface ILibraryService
{
    /// <summary>Se dispara cuando se actualiza el progreso de un scan (Current, Total).</summary>
    event EventHandler<(int Current, int Total)>? ScanProgress;

    /// <summary>Lista todas las canciones de la biblioteca.</summary>
    List<Song> GetAllSongs();

    /// <summary>Número total de canciones en la biblioteca.</summary>
    int SongCount { get; }

    /// <summary>Escanea una carpeta recursivamente y devuelve la lista de archivos de audio.</summary>
    List<string> Scan(string folderPath);

    /// <summary>Importa una carpeta completa: extrae tags, deduplica, inserta en DB.</summary>
    Task<ImportResult> ImportFromFolderAsync(string folderPath, long? targetPlaylistId = null);

    /// <summary>Importa archivos individuales (drag & drop).</summary>
    Task<ImportResult> ImportFilesAsync(List<string> filePaths, long? targetPlaylistId = null);

    /// <summary>Borra toda la biblioteca (y limpia cache de portadas).</summary>
    void DeleteAll();

    /// <summary>Determina si un archivo es de audio por su extensión.</summary>
    bool IsAudioFile(string filePath);

    /// <summary>
    /// Re-escanea las canciones que tienen DurationMs=0 o ArtUri=null en la DB,
    /// actualizando sus metadatos con TagReader. Útil para migrar datos viejos.
    /// </summary>
    Task<int> FixMissingMetadataAsync();
}
