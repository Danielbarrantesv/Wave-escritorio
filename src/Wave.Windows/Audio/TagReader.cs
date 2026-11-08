using System.IO;
using System.Security.Cryptography;
using System.Text;
using NAudio.Wave;
using TagLib;
using File = System.IO.File;
using TagFile = TagLib.File;

namespace Wave.Windows.Audio;

/// <summary>
/// Lector de metadatos de archivos de audio usando TagLib#.
///
/// Extrae: title, artist, album, duration, trackNo, year, genre, mimeType, portada.
/// La portada embebida se guarda como JPG en %LocalAppData%\Wave\art\ para persistencia.
///
/// Si TagLib# no puede leer la duración (devuelve 0), usa NAudio como fallback.
/// </summary>
public static class TagReader
{
    /// <summary>Directorio donde se guardan las portadas extraídas.</summary>
    public static readonly string ArtDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Wave", "art");

    /// <summary>Extensiones de audio soportadas (case-insensitive).</summary>
    public static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".aac", ".m4a", ".ogg", ".wav", ".wma"
    };

    /// <summary>
    /// Lee los metadatos de un archivo de audio usando TagLib#.
    /// Si TagLib# no puede leer la duración, usa NAudio como fallback.
    /// </summary>
    public static Models.Song? Read(string filePath, string? fileName = null, long? fileSize = null)
    {
        if (!File.Exists(filePath)) return null;

        var fileInfo = new FileInfo(filePath);
        fileName ??= fileInfo.Name;
        fileSize ??= fileInfo.Length;

        try
        {
            using var tagFile = TagFile.Create(filePath);
            var tag = tagFile.Tag;
            var properties = tagFile.Properties;

            // Intentar leer duración con TagLib#
            var durationMs = (long)properties.Duration.TotalMilliseconds;

            // Fallback: si TagLib# no pudo leer la duración, usar NAudio
            if (durationMs <= 0)
            {
                durationMs = GetDurationWithNaudio(filePath);
                if (durationMs <= 0) return null; // archivo corrupto o sin audio
            }

            var title = !string.IsNullOrWhiteSpace(tag.Title)
                ? tag.Title
                : Path.GetFileNameWithoutExtension(fileName);

            var artist = !string.IsNullOrWhiteSpace(tag.FirstArtist)
                ? tag.FirstArtist
                : (!string.IsNullOrWhiteSpace(tag.FirstAlbumArtist)
                    ? tag.FirstAlbumArtist
                    : null);

            var album = !string.IsNullOrWhiteSpace(tag.Album) ? tag.Album : null;
            var trackNo = tag.Track > 0 ? (int?)tag.Track : null;
            var year = tag.Year > 0 ? (int?)tag.Year : null;
            var genre = tag.FirstGenre;
            var mimeType = GetMimeType(filePath);

            // Extraer portada embebida (si existe)
            var artUri = ExtractEmbeddedPicture(tag, filePath);

            return new Models.Song
            {
                FilePath = filePath,
                FileName = fileName,
                FileSize = fileSize.Value,
                Title = title,
                Artist = artist,
                Album = album,
                DurationMs = durationMs,
                TrackNo = trackNo,
                Year = year,
                Genre = genre,
                MimeType = mimeType,
                DateAddedSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ArtUri = artUri,
                LastModified = new DateTimeOffset(fileInfo.LastWriteTime).ToUnixTimeSeconds()
            };
        }
        catch (CorruptFileException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Wave] TagLib# corrupt file: {filePath}: {ex.Message}");
            return null;
        }
        catch (UnsupportedFormatException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Wave] TagLib# unsupported format: {filePath}: {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Wave] TagReader failed for {filePath}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Fallback: lee la duración del archivo usando NAudio (MediaFoundationReader).
    /// TagLib# a veces devuelve 0 para algunos formatos; NAudio es más robusto.
    /// </summary>
    private static long GetDurationWithNaudio(string filePath)
    {
        try
        {
            using var reader = new MediaFoundationReader(filePath);
            return (long)reader.TotalTime.TotalMilliseconds;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>Determina si un archivo es de audio por su extensión.</summary>
    public static bool IsAudioFile(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        return AudioExtensions.Contains(ext);
    }

    /// <summary>Extrae la primera portada embebida del tag y la guarda como JPG en disco.</summary>
    private static string? ExtractEmbeddedPicture(Tag tag, string filePath)
    {
        try
        {
            var pictures = tag.Pictures;
            if (pictures == null || pictures.Length == 0) return null;
            var pic = pictures[0];
            if (pic.Data == null || pic.Data.Count == 0) return null;

            Directory.CreateDirectory(ArtDirectory);

            // Hash del filePath para nombre único y determinista
            var hash = ComputeShortHash(filePath);
            var artFile = Path.Combine(ArtDirectory, $"art_{hash}.jpg");

            // Solo escribir si no existe (idempotente)
            if (!File.Exists(artFile))
            {
                File.WriteAllBytes(artFile, pic.Data.ToArray());
            }
            return artFile;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Wave] Failed to extract picture from {filePath}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Calcula un hash SHA256 corto (16 chars) para usar como nombre de archivo.</summary>
    private static string ComputeShortHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return BitConverter.ToString(bytes).Replace("-", "")[..16];
    }

    /// <summary>Determina el MIME type por la extensión del archivo.</summary>
    private static string GetMimeType(string filePath)
    {
        return Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".mp3" => "audio/mpeg",
            ".flac" => "audio/flac",
            ".aac" => "audio/aac",
            ".m4a" => "audio/mp4",
            ".ogg" => "audio/ogg",
            ".wav" => "audio/wav",
            ".wma" => "audio/x-ms-wma",
            _ => "audio/unknown"
        };
    }
}
