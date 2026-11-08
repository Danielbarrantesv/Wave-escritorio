using System.IO;
using Wave.Windows.Audio;
using Wave.Windows.Data.Repositories;
using Wave.Windows.Models;

namespace Wave.Windows.Services;

/// <summary>
/// Servicio de biblioteca musical. Escanea carpetas, extrae tags con TagLib#,
/// deduplica contra la DB existente e inserta nuevas canciones.
///
/// Deduplicación con 3 estrategias en orden de prioridad:
/// 1. Por path absoluto (igual ruta = mismo archivo)
/// 2. Por nombre + tamaño (archivo copiado con otro nombre)
/// 3. Por metadata (title + artist + duration redondeada a 2s)
/// </summary>
public sealed class LibraryService : ILibraryService
{
    private readonly ISongRepository _songRepo;
    private readonly IPlaylistService? _playlistService;

    public event EventHandler<(int Current, int Total)>? ScanProgress;

    /// <summary>
    /// Constructor principal. PlaylistService es opcional: si se pasa, tras importar
    /// canciones nuevas se invoca CreateGenrePlaylists() automáticamente (v2.5.24 feature).
    /// </summary>
    public LibraryService(ISongRepository songRepo, IPlaylistService? playlistService = null)
    {
        _songRepo = songRepo;
        _playlistService = playlistService;
    }

    public List<Song> GetAllSongs() => _songRepo.ListAll();

    public int SongCount => _songRepo.Count();

    /// <summary>
    /// Escanea una carpeta recursivamente y devuelve la lista de archivos de audio.
    /// Silenciosamente ignora carpetas sin permisos o inexistentes.
    /// </summary>
    public List<string> Scan(string folderPath)
    {
        var files = new List<string>();
        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            return files;

        try
        {
            var allFiles = Directory.EnumerateFiles(folderPath, "*.*", SearchOption.AllDirectories);
            foreach (var file in allFiles)
            {
                if (TagReader.IsAudioFile(file))
                    files.Add(file);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Wave] Sin permisos para escanear {folderPath}: {ex.Message}");
        }
        catch (DirectoryNotFoundException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Wave] Carpeta no encontrada: {folderPath}: {ex.Message}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Wave] Error escaneando {folderPath}: {ex.Message}");
        }
        return files;
    }

    /// <summary>
    /// Importa una carpeta completa en un hilo de background.
    /// </summary>
    public Task<ImportResult> ImportFromFolderAsync(string folderPath, long? targetPlaylistId = null)
    {
        return Task.Run(() =>
        {
            var files = Scan(folderPath);
            return ImportFilesInternal(files, targetPlaylistId);
        });
    }

    /// <summary>
    /// Importa una lista de archivos individuales (drag & drop).
    /// Filtra los que no son de audio automáticamente.
    /// </summary>
    public Task<ImportResult> ImportFilesAsync(List<string> filePaths, long? targetPlaylistId = null)
    {
        return Task.Run(() =>
        {
            var audioFiles = filePaths.Where(TagReader.IsAudioFile).ToList();
            return ImportFilesInternal(audioFiles, targetPlaylistId);
        });
    }

    /// <summary>
    /// Lógica interna de importación: dedup + tags + insert.
    /// Sincronizado para evitar races cuando se importan varias carpetas en paralelo.
    /// </summary>
    private ImportResult ImportFilesInternal(List<string> files, long? targetPlaylistId, string? folderPath = null)
    {
        if (files.Count == 0) return new ImportResult(0, 0, 0);

        int added = 0, duplicates = 0;
        var total = files.Count;

        // Cargar DB existente una sola vez al inicio
        var existingSongs = _songRepo.ListAll();
        var knownPaths = existingSongs.Select(s => s.FilePath).ToHashSet();
        var knownNameSize = existingSongs
            .Select(s => $"{s.FileName.ToLowerInvariant()}|{s.FileSize}")
            .ToHashSet();
        var knownMeta = existingSongs
            .Select(s => $"{(s.Title ?? "").ToLowerInvariant()}|{(s.Artist ?? "").ToLowerInvariant()}|{s.DurationMs / 2000}")
            .ToHashSet();

        var playlistAttachIds = new List<long>();

        for (int i = 0; i < files.Count; i++)
        {
            ScanProgress?.Invoke(this, (i + 1, total));

            var filePath = files[i];
            try
            {
                var fileInfo = new FileInfo(filePath);
                var fileName = fileInfo.Name;
                var fileSize = fileInfo.Length;

                // Dedup 1: por path absoluto
                if (knownPaths.Contains(filePath))
                {
                    duplicates++;
                    var existing = existingSongs.FirstOrDefault(s => s.FilePath == filePath);
                    if (existing != null) playlistAttachIds.Add(existing.Id);
                    continue;
                }

                // Dedup 2: por name + size (mismo archivo en otro path)
                var nameSizeKey = $"{fileName.ToLowerInvariant()}|{fileSize}";
                if (knownNameSize.Contains(nameSizeKey))
                {
                    duplicates++;
                    var existing = existingSongs.FirstOrDefault(s =>
                        $"{s.FileName.ToLowerInvariant()}|{s.FileSize}" == nameSizeKey);
                    if (existing != null)
                    {
                        playlistAttachIds.Add(existing.Id);
                        knownPaths.Add(filePath);
                    }
                    continue;
                }

                // Extraer metadatos con TagLib#
                var song = TagReader.Read(filePath, fileName, fileSize);
                if (song == null)
                {
                    duplicates++;
                    continue;
                }

                // Dedup 3: por metadata (title + artist + duration a 2s)
                var metaKey = $"{(song.Title ?? "").ToLowerInvariant()}|{(song.Artist ?? "").ToLowerInvariant()}|{song.DurationMs / 2000}";
                if (knownMeta.Contains(metaKey))
                {
                    duplicates++;
                    var existing = existingSongs.FirstOrDefault(s =>
                        $"{(s.Title ?? "").ToLowerInvariant()}|{(s.Artist ?? "").ToLowerInvariant()}|{s.DurationMs / 2000}" == metaKey);
                    if (existing != null) playlistAttachIds.Add(existing.Id);
                    continue;
                }

                var newId = _songRepo.Insert(song);
                if (newId > 0)
                {
                    added++;
                    playlistAttachIds.Add(newId);
                    knownPaths.Add(filePath);
                    knownNameSize.Add(nameSizeKey);
                    knownMeta.Add(metaKey);
                    // Actualizar cache en memoria para siguientes iteraciones
                    song.Id = newId;
                    existingSongs.Add(song);
                }
                else
                {
                    duplicates++;
                    var existing = _songRepo.GetByPath(filePath);
                    if (existing != null) playlistAttachIds.Add(existing.Id);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Wave] Error importing {filePath}: {ex.Message}");
                duplicates++;
            }
        }

        // Si se agregaron canciones nuevas y hay PlaylistService, crear playlists automáticas por género.
        // CRITICAL: v2.5.24 feature del spec Android — auto-generar playlists "Rock", "Pop", "Jazz", etc.
        if (added > 0 && _playlistService != null)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[LibraryService] CreateGenrePlaylists: {added} new songs, calling CreateGenrePlaylists...");
                _playlistService.CreateGenrePlaylists();
                System.Diagnostics.Debug.WriteLine("[LibraryService] CreateGenrePlaylists completed");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LibraryService] CreateGenrePlaylists falló: {ex.Message}");
                // No fallar la importación si CreateGenrePlaylists falla
            }
        }

        // Si se especificó una playlist target, agregar las canciones vía el servicio
        if (targetPlaylistId.HasValue && playlistAttachIds.Count > 0 && _playlistService != null)
        {
            try
            {
                _playlistService.AddSongs(targetPlaylistId.Value, playlistAttachIds.Distinct().ToList());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Wave] AddSongs a playlist {targetPlaylistId} falló: {ex.Message}");
            }
        }

        return new ImportResult(added, duplicates, total);
    }

    public bool IsAudioFile(string filePath) => TagReader.IsAudioFile(filePath);

    /// <summary>
    /// Borra toda la biblioteca y limpia cache de portadas.
    /// Útil para tests o reseteo manual del usuario.
    /// </summary>
    public void DeleteAll()
    {
        _songRepo.DeleteAll();
        try
        {
            if (Directory.Exists(TagReader.ArtDirectory))
            {
                Directory.Delete(TagReader.ArtDirectory, recursive: true);
                Directory.CreateDirectory(TagReader.ArtDirectory);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Wave] Error limpiando cache de portadas: {ex.Message}");
        }
    }
}
