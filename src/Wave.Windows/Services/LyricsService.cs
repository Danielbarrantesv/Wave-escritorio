using System.IO;
using System.Text.RegularExpressions;
using Wave.Windows.External;
using Wave.Windows.Models;

namespace Wave.Windows.Services;

/// <summary>
/// Servicio de letras musicales. Combina el cliente HTTP de lrclib.net con el
/// parser LRC (formato de timestamps [mm:ss.xx]) y expone una API de alto nivel.
///
/// Formato LRC:
///   [00:01.23] Primera línea
///   [00:05.67] Segunda línea
///   [00:10.00] Tercera línea
///
/// También soporta líneas con múltiples timestamps (compressed LRC).
/// </summary>
public sealed class LyricsService : ILyricsService
{
    private readonly LrcLibClient _client;

    /// <summary>Regex para timestamps LRC: [mm:ss(.xx)?] o [mm:ss(.xxx)?]</summary>
    private static readonly Regex TimeTagRegex = new(
        @"\[(\d+):(\d{2})(?:\.(\d{2,3}))?\]",
        RegexOptions.Compiled);

    public LyricsService(LrcLibClient client)
    {
        _client = client;
    }

    /// <summary>
    /// Obtiene letras para una canción.
    /// 1. Llama a LrcLibClient (que revisa cache local + API)
    /// 2. Parsea el resultado a ParsedLyrics
    /// </summary>
    public async Task<ParsedLyrics?> GetLyricsAsync(Song song)
    {
        var artist = song.Artist ?? "";
        var track = !string.IsNullOrWhiteSpace(song.Title) ? song.Title : song.FileName;
        if (string.IsNullOrWhiteSpace(track)) return null;

        var raw = await _client.GetLyricsAsync(
            artist,
            track,
            song.Album,
            song.DurationMs / 1000);

        if (string.IsNullOrEmpty(raw)) return null;

        return Parse(raw);
    }

    /// <summary>
    /// Parsea texto crudo en formato LRC a ParsedLyrics.
    /// - Si el texto tiene timestamps → IsSynced=true, Lines con timestamps
    /// - Si no tiene timestamps → IsSynced=false, PlainLyrics con todo el texto
    /// </summary>
    public ParsedLyrics Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new ParsedLyrics(PlainLyrics: null, Lines: new(), IsSynced: false);
        }

        var lines = new List<LyricLine>();
        var plainLines = new List<string>();
        bool hasTimeTags = false;

        foreach (var line in raw.Split('\n'))
        {
            var matches = TimeTagRegex.Matches(line);
            if (matches.Count > 0)
            {
                hasTimeTags = true;
                // El texto es el resto de la línea sin los timestamps
                var text = TimeTagRegex.Replace(line, "").Trim();
                // Línea vacía → usar ♪ como marcador de instrumental
                if (string.IsNullOrEmpty(text)) text = "♪";

                // Una línea puede tener múltiples timestamps (compressed LRC)
                foreach (Match m in matches)
                {
                    var min = int.Parse(m.Groups[1].Value);
                    var sec = int.Parse(m.Groups[2].Value);
                    var msGroup = m.Groups[3];
                    var ms = msGroup.Success
                        ? int.Parse(msGroup.Value.PadRight(3, '0'))
                        : 0;
                    var ts = min * 60_000 + sec * 1000 + ms;
                    lines.Add(new LyricLine(ts, text));
                }
            }
            else
            {
                // Línea sin timestamp → texto plano
                plainLines.Add(line.Trim());
            }
        }

        // Si había timestamps, ordenar líneas por timestamp
        if (hasTimeTags)
        {
            lines = lines.OrderBy(l => l.TimestampMs).ToList();
        }

        var plainLyrics = plainLines.Count > 0
            ? string.Join("\n", plainLines)
            : null;

        return new ParsedLyrics(
            PlainLyrics: plainLyrics,
            Lines: lines,
            IsSynced: hasTimeTags);
    }

    /// <summary>
    /// Calcula el índice de la línea activa en un ParsedLyrics dado un timestamp (ms).
    /// Recorre de atrás hacia adelante y devuelve el índice de la primera línea
    /// cuyo timestamp sea ≤ positionMs.
    /// </summary>
    /// <returns>Índice (0-based) o -1 si no hay líneas o todas son posteriores.</returns>
    public int ActiveLineIndex(ParsedLyrics lyrics, long positionMs)
    {
        if (!lyrics.IsSynced || lyrics.Lines.Count == 0) return -1;

        for (int i = lyrics.Lines.Count - 1; i >= 0; i--)
        {
            if (lyrics.Lines[i].TimestampMs <= positionMs)
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// Limpia el cache local de letras (todas las .lrc en %LocalAppData%\Wave\lyrics\).
    /// Útil para tests o reseteo manual.
    /// </summary>
    public void ClearCache()
    {
        try
        {
            if (Directory.Exists(LrcLibClient.CacheDirectory))
            {
                foreach (var file in Directory.EnumerateFiles(LrcLibClient.CacheDirectory, "*.lrc"))
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Wave] Error limpiando cache de letras: {ex.Message}");
        }
    }
}
