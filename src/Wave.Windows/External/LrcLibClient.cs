using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wave.Windows.External;

/// <summary>
/// Cliente HTTP de la API de lrclib.net (https://lrclib.net/api/get).
/// Busca letras sincronizadas (con timestamps LRC) o planas para una canción.
///
/// Características:
/// - Sin API key ni autenticación (rate limit ~100 req/min)
/// - Cache local en %LocalAppData%\Wave\lyrics\ con hash del artist+track
/// - Timeout de 15s
/// - Manejo silencioso de errores (devuelve null si no encuentra o falla red)
/// </summary>
public sealed class LrcLibClient : IDisposable
{
    /// <summary>Directorio donde se cachean las letras como archivos .lrc.</summary>
    public static readonly string CacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Wave", "lyrics");

    private const string ApiBase = "https://lrclib.net/api";

    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;

    /// <summary>
    /// Constructor por defecto. Crea su propio HttpClient con timeout 15s.
    /// </summary>
    public LrcLibClient() : this(CreateDefaultHttpClient())
    {
        _ownsHttpClient = true;
    }

    /// <summary>
    /// Constructor con HttpClient inyectado (útil para tests o IHttpClientFactory).
    /// </summary>
    public LrcLibClient(HttpClient http)
    {
        _http = http;
        _ownsHttpClient = false;
    }

    private static HttpClient CreateDefaultHttpClient()
    {
        return new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15),
            DefaultRequestHeaders =
            {
                UserAgent =
                {
                    new System.Net.Http.Headers.ProductInfoHeaderValue("Wave", "0.5.0")
                }
            }
        };
    }

    /// <summary>
    /// Busca letras para una canción.
    /// 1. Revisa cache local (si hay, devuelve sin red)
    /// 2. Si no, consulta lrclib.net
    /// 3. Si la API devuelve, lo guarda en cache y retorna
    /// </summary>
    /// <param name="artist">Nombre del artista.</param>
    /// <param name="track">Título de la canción.</param>
    /// <param name="album">Nombre del álbum (opcional, mejora precisión).</param>
    /// <param name="durationSec">Duración en segundos (opcional, mejora precisión).</param>
    /// <returns>Letra sincronizada (con timestamps LRC) o plana, o null si no se encuentra.</returns>
    public async Task<string?> GetLyricsAsync(string artist, string track, string? album = null, long durationSec = 0)
    {
        if (string.IsNullOrWhiteSpace(track)) return null;

        // 1. Cache local
        var cacheKey = BuildCacheKey(artist, track);
        var cached = GetCached(cacheKey);
        if (cached != null) return cached;

        // 2. Query a lrclib.net
        var url = BuildApiUrl(artist, track, album, durationSec);
        try
        {
            var response = await _http.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                // 404 = no encontrada; 5xx = error servidor. En ambos casos, null silencioso.
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonSerializer.Deserialize<LrcLibResponse>(json);
            // Preferir syncedLyrics (con timestamps); fallback a plainLyrics
            var lyrics = doc?.SyncedLyrics ?? doc?.PlainLyrics;
            if (string.IsNullOrEmpty(lyrics)) return null;

            // 3. Guardar en cache
            SaveCache(cacheKey, lyrics);
            return lyrics;
        }
        catch (HttpRequestException)
        {
            // Sin internet, timeout, DNS, etc. → silencioso
            return null;
        }
        catch (TaskCanceledException)
        {
            // Timeout → silencioso
            return null;
        }
        catch (JsonException)
        {
            // JSON inválido → silencioso
            return null;
        }
    }

    /// <summary>Construye la URL de la API con query parameters.</summary>
    private static string BuildApiUrl(string artist, string track, string? album, long durationSec)
    {
        var url = $"{ApiBase}/get?artist_name={Uri.EscapeDataString(artist)}&track_name={Uri.EscapeDataString(track)}";
        if (!string.IsNullOrEmpty(album))
            url += $"&album_name={Uri.EscapeDataString(album)}";
        if (durationSec > 0)
            url += $"&duration={durationSec}";
        return url;
    }

    /// <summary>Construye una clave única (artist|track en lowercase) para el cache.</summary>
    private static string BuildCacheKey(string artist, string track)
        => $"{artist}|{track}".ToLowerInvariant();

    /// <summary>Recupera letra cacheada del disco, si existe.</summary>
    private static string? GetCached(string key)
    {
        try
        {
            var file = GetCacheFile(key);
            return File.Exists(file) ? File.ReadAllText(file) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Guarda letra en cache de disco.</summary>
    private static void SaveCache(string key, string lyrics)
    {
        try
        {
            Directory.CreateDirectory(CacheDirectory);
            File.WriteAllText(GetCacheFile(key), lyrics);
        }
        catch
        {
            // Si no podemos cachear, no rompemos la app
        }
    }

    /// <summary>Determina el path del archivo de cache para una clave dada.</summary>
    private static string GetCacheFile(string key)
    {
        // Hash SHA256 corto (16 chars) del key para nombre determinista
        var hash = BitConverter.ToString(
            SHA256.HashData(Encoding.UTF8.GetBytes(key))
        ).Replace("-", "")[..16];
        return Path.Combine(CacheDirectory, $"{hash}.lrc");
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _http.Dispose();
        }
    }

    /// <summary>DTO interno para deserializar la respuesta de lrclib.net.</summary>
    private sealed class LrcLibResponse
    {
        [JsonPropertyName("syncedLyrics")]
        public string? SyncedLyrics { get; set; }

        [JsonPropertyName("plainLyrics")]
        public string? PlainLyrics { get; set; }
    }
}
