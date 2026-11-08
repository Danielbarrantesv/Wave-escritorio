namespace Wave.Windows.Models;

/// <summary>
/// Asociación N:M entre Playlist y Song (con posición).
/// Mapea a la tabla "playlist_songs".
/// </summary>
public class PlaylistSong
{
    public long PlaylistId { get; set; }
    public long SongId { get; set; }
    public int Position { get; set; }
}
