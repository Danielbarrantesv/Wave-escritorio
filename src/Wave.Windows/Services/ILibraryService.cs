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

    /// <summary>
    /// Escanea las carpetas conocidas en busca de archivos NUEVOS (que no están en DB).
    /// Rápido: solo compara paths sin re-leer tags de existentes.
    /// </summary>
    Task<int> ScanForNewFilesAsync();

    /// <summary>
    /// Renombra los archivos físicos de TODAS las canciones de la biblioteca
    /// usando el patrón "Artista - Título.ext" con los metadatos de la DB.
    /// - Si la canción está reproduciéndose, la pausa y reanuda tras renombrar.
    /// - Si el nombre destino ya existe en la misma carpeta (y no es el mismo archivo),
    ///   añade sufijo _1, _2, etc.
    /// - Sanitiza caracteres inválidos para nombres de archivo en Windows.
    /// Devuelve el número de archivos renombrados correctamente.
    /// </summary>
    Task<(int Renamed, int Skipped, int Errors)> RenameSongsByMetadataAsync();

    /// <summary>
    /// Importa una carpeta creando una playlist por cada subcarpeta que contenga audio.
    /// La carpeta raíz seleccionada NO se convierte en playlist, pero cada subcarpeta
    /// directa sí (ej: /Music/Rock → playlist "Rock", /Music/Pop → playlist "Pop").
    /// Los archivos sueltos en la carpeta raíz se importan pero no se asocian a ninguna
    /// playlist (van a "Todas las canciones").
    ///
    /// Devuelve la lista de playlists creadas/actualizadas con su nombre y número de
    /// canciones, para que el JS pueda mostrarle al usuario qué se hizo.
    /// </summary>
    Task<List<SubfolderPlaylistResult>> ImportFolderWithSubfolderPlaylistsAsync(string folderPath);
}

/// <summary>
/// Resultado de importar una subcarpeta como playlist.
/// </summary>
public record SubfolderPlaylistResult(string PlaylistName, int Added, int Duplicates);
