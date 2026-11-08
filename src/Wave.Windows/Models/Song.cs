using System.Text.Json.Serialization;

namespace Wave.Windows.Models;

/// <summary>
/// Representa una canción en la biblioteca.
/// Mapea a la tabla "songs" en SQLite (ver Migrations/001_initial.sql).
/// </summary>
public class Song
{
    public long Id { get; set; }
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public long FileSize { get; set; }
    public string? Title { get; set; }
    public string? Artist { get; set; }
    public string? Album { get; set; }
    public long DurationMs { get; set; }
    public int? TrackNo { get; set; }
    public int? Year { get; set; }
    public string? MimeType { get; set; }
    public long DateAddedSec { get; set; }
    public string? Fingerprint { get; set; }
    public string? ArtUri { get; set; }
    public long LastModified { get; set; }
    public bool IsFavorite { get; set; }
    public long LastPlayedAt { get; set; }
    public string? Genre { get; set; }

    // Display helpers (no persistidos) — evitan nulls en la UI
    [JsonIgnore]
    public string DisplayTitle => !string.IsNullOrWhiteSpace(Title) ? Title : FileName;

    [JsonIgnore]
    public string DisplayArtist => !string.IsNullOrWhiteSpace(Artist) && Artist != "<unknown>"
        ? Artist
        : "Artista desconocido";

    [JsonIgnore]
    public string DisplayAlbum => !string.IsNullOrWhiteSpace(Album) ? Album : "Álbum desconocido";

    [JsonIgnore]
    public string DisplayGenre => !string.IsNullOrWhiteSpace(Genre) ? Genre : "Sin género";

    [JsonIgnore]
    public string DisplayDuration
    {
        get
        {
            var ts = TimeSpan.FromMilliseconds(DurationMs);
            return ts.TotalHours >= 1
                ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
                : $"{ts.Minutes}:{ts.Seconds:D2}";
        }
    }
}
