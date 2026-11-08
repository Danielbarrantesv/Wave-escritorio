using System.IO;
using System.Security.Cryptography;
using System.Text;
using NAudio.Wave;
using TagLib;
using Wave.Windows.Services;
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
    /// Todo el proceso se loguea a wave.log para diagnóstico.
    /// </summary>
    public static Models.Song? Read(string filePath, string? fileName = null, long? fileSize = null)
    {
        if (!File.Exists(filePath)) return null;

        var fileInfo = new FileInfo(filePath);
        fileName ??= fileInfo.Name;
        fileSize ??= fileInfo.Length;

        long durationMs = 0;
        TagLib.Tag? tag = null;
        bool tagLibSuccess = false;

        // PASO 1: Intentar TagLib#
        try
        {
            using var tagFile = TagFile.Create(filePath);
            tag = tagFile.Tag;
            var properties = tagFile.Properties;
            durationMs = (long)properties.Duration.TotalMilliseconds;
            tagLibSuccess = true;

            if (durationMs <= 0)
            {
                WaveLogger.Warn($"TagLib# duration=0 for {fileName}, trying NAudio fallback");
            }
        }
        catch (CorruptFileException ex)
        {
            WaveLogger.Warn($"TagLib# corrupt: {fileName}: {ex.Message}");
        }
        catch (UnsupportedFormatException ex)
        {
            WaveLogger.Warn($"TagLib# unsupported: {fileName}: {ex.Message}");
        }
        catch (Exception ex)
        {
            WaveLogger.Warn($"TagLib# failed: {fileName}: {ex.Message}");
        }

        // PASO 2: Fallback NAudio si TagLib# no pudo leer la duración
        if (durationMs <= 0)
        {
            durationMs = GetDurationWithNaudio(filePath);
            if (durationMs > 0)
            {
                WaveLogger.Info($"NAudio fallback OK: {fileName} duration={durationMs}ms");
            }
            else
            {
                WaveLogger.Error($"No duration found for {fileName} (TagLib# and NAudio both failed)");
                return null; // No se pudo leer la duración
            }
        }

        // PASO 3: Extraer metadatos restantes
        string title;
        string? artist;
        string? album;
        int? trackNo;
        int? year;
        string? genre;
        string mimeType = GetMimeType(filePath);

        if (tag != null && tagLibSuccess)
        {
            title = !string.IsNullOrWhiteSpace(tag.Title)
                ? tag.Title
                : Path.GetFileNameWithoutExtension(fileName);

            artist = !string.IsNullOrWhiteSpace(tag.FirstArtist)
                ? tag.FirstArtist
                : (!string.IsNullOrWhiteSpace(tag.FirstAlbumArtist)
                    ? tag.FirstAlbumArtist
                    : null);

            album = !string.IsNullOrWhiteSpace(tag.Album) ? tag.Album : null;
            trackNo = tag.Track > 0 ? (int?)tag.Track : null;
            year = tag.Year > 0 ? (int?)tag.Year : null;
            genre = tag.FirstGenre;
        }
        else
        {
            // TagLib# falló completamente, usar defaults
            title = Path.GetFileNameWithoutExtension(fileName);
            artist = null;
            album = null;
            trackNo = null;
            year = null;
            genre = null;
        }

        // PASO 4: Extraer portada embebida (si existe)
        string? artUri = null;
        if (tag != null)
        {
            artUri = ExtractEmbeddedPicture(tag, filePath);
        }

        // PASO 5: Fallback — buscar cover.jpg / folder.jpg en la carpeta
        if (string.IsNullOrEmpty(artUri))
        {
            artUri = FindCoverArtInFolder(filePath);
        }

        WaveLogger.Debug($"Read OK: {fileName} | duration={durationMs}ms | artist={artist} | genre={genre} | art={(artUri != null ? "yes" : "no")}");

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

    /// <summary>
    /// Fallback: busca archivos de portada comunes (cover.jpg, folder.jpg, album.jpg, etc.)
    /// en la misma carpeta del archivo de audio.
    /// </summary>
    private static string? FindCoverArtInFolder(string audioFilePath)
    {
        try
        {
            var folder = Path.GetDirectoryName(audioFilePath);
            if (string.IsNullOrEmpty(folder)) return null;

            var coverNames = new[] { "cover.jpg", "cover.png", "folder.jpg", "folder.png",
                                     "album.jpg", "album.png", "front.jpg", "front.png" };

            foreach (var name in coverNames)
            {
                var path = Path.Combine(folder, name);
                if (File.Exists(path))
                {
                    System.Diagnostics.Debug.WriteLine($"[TagReader] Found cover art: {name}");
                    return path;
                }
            }
        }
        catch { }
        return null;
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
