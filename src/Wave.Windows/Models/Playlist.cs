namespace Wave.Windows.Models;

/// <summary>
/// Playlist de usuario. Incluye Favoritos (siempre presente) y playlists automáticas por género.
/// </summary>
public class Playlist
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }
    public string? CoverUri { get; set; }
    public string? FolderPath { get; set; }

    /// <summary>Nombre reservado de la playlist Favoritos (siempre existe).</summary>
    public const string FavoritesName = "Favoritos";

    /// <summary>Indica si es la playlist Favoritos.</summary>
    public bool IsFavorites => string.Equals(Name, FavoritesName, StringComparison.OrdinalIgnoreCase);

    /// <summary>Indica si fue creada automáticamente por género.</summary>
    public bool IsAutoGenre => string.Equals(Description, "auto-genre", StringComparison.OrdinalIgnoreCase);
}
