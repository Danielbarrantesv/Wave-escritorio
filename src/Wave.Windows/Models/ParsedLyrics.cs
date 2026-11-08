namespace Wave.Windows.Models;

/// <summary>
/// Una línea de letra sincronizada (timestamp + texto).
/// </summary>
public record LyricLine(long TimestampMs, string Text);

/// <summary>
/// Letras parseadas de una canción.
/// </summary>
/// <param name="PlainLyrics">Letras sin timestamps (si no estaban sincronizadas).</param>
/// <param name="Lines">Líneas sincronizadas (vacío si no hay timestamps).</param>
/// <param name="IsSynced">True si la letra tiene timestamps LRC.</param>
public record ParsedLyrics(string? PlainLyrics, List<LyricLine> Lines, bool IsSynced)
{
    /// <summary>True si no hay ni letra plana ni sincronizada.</summary>
    public bool IsEmpty => string.IsNullOrWhiteSpace(PlainLyrics) && Lines.Count == 0;
}
