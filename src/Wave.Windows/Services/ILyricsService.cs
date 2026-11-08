using Wave.Windows.Models;

namespace Wave.Windows.Services;

/// <summary>
/// Interfaz del servicio de letras (lyrics).
/// Permite abstraer la obtención y parseo de letras para tests.
/// </summary>
public interface ILyricsService
{
    /// <summary>
    /// Obtiene letras para una canción (desde cache o API lrclib.net).
    /// Devuelve null si no hay letra disponible.
    /// </summary>
    Task<ParsedLyrics?> GetLyricsAsync(Song song);

    /// <summary>
    /// Parsea texto crudo en formato LRC (con timestamps [mm:ss.xx]) a ParsedLyrics.
    /// </summary>
    ParsedLyrics Parse(string raw);

    /// <summary>
    /// Calcula el índice de la línea activa en un ParsedLyrics dado un timestamp (ms).
    /// -1 si no hay líneas sincronizadas o todas son posteriores.
    /// </summary>
    int ActiveLineIndex(ParsedLyrics lyrics, long positionMs);

    /// <summary>
    /// Limpia el cache local de letras (todas las .lrc en %LocalAppData%\Wave\lyrics\).
    /// </summary>
    void ClearCache();
}
