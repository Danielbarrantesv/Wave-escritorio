using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace Wave.Windows.Services;

/// <summary>
/// Descarga y cachea en disco temporal canciones remotas transmitidas desde Wave Android
/// para que NAudio MediaFoundationReader reproduzca y haga seek sin cortes ni errores de red.
/// </summary>
public static class RemoteAudioCache
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(120)
    };

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UrlLocks = new();
    private const long MaxCacheBytes = 250L * 1024 * 1024;

    public static string CacheDirectory
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Wave",
                "remote-cache");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static bool IsRemoteUrl(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
         path.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

    public static async Task<string> ResolveToLocalFileAsync(string url, CancellationToken ct = default)
    {
        var ext = ExtractExtension(url);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))).ToLowerInvariant();
        var finalPath = Path.Combine(CacheDirectory, $"{hash}{ext}");

        if (File.Exists(finalPath) && new FileInfo(finalPath).Length > 0)
        {
            try { File.SetLastAccessTimeUtc(finalPath, DateTime.UtcNow); } catch { }
            return finalPath;
        }

        var sem = UrlLocks.GetOrAdd(url, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);
        try
        {
            if (File.Exists(finalPath) && new FileInfo(finalPath).Length > 0)
                return finalPath;

            var partPath = finalPath + $".{Guid.NewGuid():N}.part";
            try
            {
                using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                resp.EnsureSuccessStatusCode();
                await using (var src = await resp.Content.ReadAsStreamAsync(ct))
                await using (var dst = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true))
                {
                    await src.CopyToAsync(dst, 65536, ct);
                }
                File.Move(partPath, finalPath, overwrite: true);
                TrimCacheIfNeeded(finalPath);
                return finalPath;
            }
            catch
            {
                try { if (File.Exists(partPath)) File.Delete(partPath); } catch { }
                throw;
            }
        }
        finally
        {
            sem.Release();
        }
    }

    public static void PrefetchInBackground(string? url)
    {
        if (!IsRemoteUrl(url)) return;
        _ = Task.Run(async () =>
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                await ResolveToLocalFileAsync(url!, cts.Token);
            }
            catch
            {
            }
        });
    }

    public static void ClearCache()
    {
        try
        {
            if (!Directory.Exists(CacheDirectory)) return;
            foreach (var f in Directory.EnumerateFiles(CacheDirectory))
            {
                try { File.Delete(f); } catch { }
            }
        }
        catch { }
    }

    private static string ExtractExtension(string url)
    {
        try
        {
            var path = new Uri(url).AbsolutePath;
            var ext = Path.GetExtension(path);
            if (!string.IsNullOrWhiteSpace(ext) && ext.Length <= 6)
                return ext.ToLowerInvariant();
        }
        catch { }
        return ".mp3";
    }

    private static void TrimCacheIfNeeded(string keepPath)
    {
        try
        {
            var files = new DirectoryInfo(CacheDirectory)
                .GetFiles()
                .Where(f => !f.Name.EndsWith(".part", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f.LastAccessTimeUtc)
                .ToList();
            long total = files.Sum(f => f.Length);
            foreach (var f in files)
            {
                if (total <= MaxCacheBytes) break;
                if (string.Equals(f.FullName, keepPath, StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    var len = f.Length;
                    f.Delete();
                    total -= len;
                }
                catch { }
            }
        }
        catch { }
    }
}
