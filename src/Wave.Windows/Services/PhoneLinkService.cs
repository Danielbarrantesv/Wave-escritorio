using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Wave.Windows.Models;

namespace Wave.Windows.Services;

/// <summary>
/// Conecta Wave Windows con Wave Android en la red local mediante un código de 6 dígitos
/// para navegar y reproducir la música del celular sin copiarla permanentemente a la PC.
/// </summary>
public sealed class PhoneLinkService
{
    public const int DefaultHttpPort = 45454;
    public const int DiscoveryUdpPort = 45455;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private readonly object _gate = new();
    private List<Song> _songs = new();
    private List<Playlist> _playlists = new();
    private Dictionary<long, List<Song>> _playlistSongs = new();

    public bool IsConnected { get; private set; }
    public bool IsPhoneViewActive { get; private set; }
    public string DeviceName { get; private set; } = "";
    public string Host { get; private set; } = "";
    public int Port { get; private set; } = DefaultHttpPort;
    private string _code = "";

    public event EventHandler? StateChanged;

    public IReadOnlyList<Song> Songs
    {
        get { lock (_gate) return _songs.ToList(); }
    }

    public IReadOnlyList<Playlist> Playlists
    {
        get { lock (_gate) return _playlists.ToList(); }
    }

    public List<Song> GetPlaylistSongs(long playlistId)
    {
        lock (_gate)
        {
            return _playlistSongs.TryGetValue(playlistId, out var list)
                ? list.ToList()
                : new List<Song>();
        }
    }

    public object GetStatus()
    {
        lock (_gate)
        {
            return new
            {
                connected = IsConnected,
                active = IsConnected && IsPhoneViewActive,
                deviceName = DeviceName,
                songCount = _songs.Count,
                playlistCount = _playlists.Count,
                host = Host,
                port = Port
            };
        }
    }

    public void SetPhoneViewActive(bool active)
    {
        if (!IsConnected) active = false;
        IsPhoneViewActive = active;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Disconnect()
    {
        lock (_gate)
        {
            IsConnected = false;
            IsPhoneViewActive = false;
            DeviceName = "";
            Host = "";
            Port = DefaultHttpPort;
            _code = "";
            _songs.Clear();
            _playlists.Clear();
            _playlistSongs.Clear();
        }
        RemoteAudioCache.ClearCache();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<object> ConnectAsync(string codeInput, string? manualHost = null, CancellationToken ct = default)
    {
        var (parsedHost, parsedPort, code) = ParseInputs(codeInput, manualHost);
        if (code.Length != 6 || !code.All(char.IsDigit))
            throw new ArgumentException("Ingresa el código de 6 dígitos que aparece en Wave de tu celular.");

        string targetHost;
        int targetPort;
        string deviceName = "Android";

        if (!string.IsNullOrWhiteSpace(parsedHost))
        {
            targetHost = parsedHost;
            targetPort = parsedPort > 0 ? parsedPort : DefaultHttpPort;
            deviceName = await VerifyPairAsync(targetHost, targetPort, code, ct);
        }
        else
        {
            var udpFound = await DiscoverViaUdpAsync(code, ct);
            if (udpFound.HasValue)
            {
                targetHost = udpFound.Value.Host;
                targetPort = udpFound.Value.Port;
                deviceName = await VerifyPairAsync(targetHost, targetPort, code, ct);
            }
            else
            {
                var lanFound = await DiscoverViaLanPingAsync(code, ct);
                if (!lanFound.HasValue)
                {
                    throw new InvalidOperationException(
                        "No se encontró el celular en tu red Wi-Fi. Verifica que ambos estén en el mismo Wi-Fi, que 'Conectar con Wave PC' esté abierto en el celular y que el código sea correcto.");
                }
                targetHost = lanFound.Value.Host;
                targetPort = lanFound.Value.Port;
                deviceName = lanFound.Value.Device;
            }
        }

        await LoadLibraryFromPhoneAsync(targetHost, targetPort, code, deviceName, ct);
        IsPhoneViewActive = true;
        StateChanged?.Invoke(this, EventArgs.Empty);
        return GetStatus();
    }

    public async Task<object> RefreshAsync(CancellationToken ct = default)
    {
        string h, c, d;
        int p;
        lock (_gate)
        {
            if (!IsConnected || string.IsNullOrWhiteSpace(Host) || string.IsNullOrWhiteSpace(_code))
                throw new InvalidOperationException("No hay ningún celular conectado.");
            h = Host;
            p = Port;
            c = _code;
            d = DeviceName;
        }
        await LoadLibraryFromPhoneAsync(h, p, c, d, ct);
        StateChanged?.Invoke(this, EventArgs.Empty);
        return GetStatus();
    }

    private async Task LoadLibraryFromPhoneAsync(string host, int port, string code, string deviceName, CancellationToken ct)
    {
        var baseUrl = $"http://{host}:{port}";
        var songsJson = await _http.GetStringAsync($"{baseUrl}/songs?code={Uri.EscapeDataString(code)}", ct);
        var playlistsJson = await _http.GetStringAsync($"{baseUrl}/playlists?code={Uri.EscapeDataString(code)}", ct);

        var remoteSongs = JsonSerializer.Deserialize<List<RemoteSongDto>>(songsJson, JsonOpts) ?? new();
        var remotePlaylists = JsonSerializer.Deserialize<List<RemotePlaylistDto>>(playlistsJson, JsonOpts) ?? new();

        var mappedSongs = new List<Song>(remoteSongs.Count);
        var byOrigId = new Dictionary<long, Song>(remoteSongs.Count);

        foreach (var rs in remoteSongs)
        {
            var ext = string.IsNullOrWhiteSpace(rs.Ext) ? "mp3" : rs.Ext.TrimStart('.');
            var virtualId = -Math.Abs(rs.Id);
            if (virtualId == 0) continue;

            var song = new Song
            {
                Id = virtualId,
                FilePath = $"{baseUrl}/stream/{rs.Id}.{ext}?code={Uri.EscapeDataString(code)}",
                FileName = rs.FileName ?? $"track_{rs.Id}.{ext}",
                FileSize = rs.FileSize,
                Title = rs.Title,
                Artist = rs.Artist,
                Album = rs.Album,
                DurationMs = rs.DurationMs,
                TrackNo = rs.TrackNo,
                Year = rs.Year,
                MimeType = rs.MimeType,
                DateAddedSec = rs.DateAddedSec,
                ArtUri = rs.HasArt ? $"{baseUrl}/art/{rs.Id}?code={Uri.EscapeDataString(code)}" : null,
                IsFavorite = rs.IsFavorite,
                Genre = rs.Genre
            };
            mappedSongs.Add(song);
            byOrigId[rs.Id] = song;
        }

        var mappedPlaylists = new List<Playlist>(remotePlaylists.Count);
        var plSongsMap = new Dictionary<long, List<Song>>(remotePlaylists.Count);

        foreach (var rp in remotePlaylists)
        {
            var virtualPlId = -Math.Abs(rp.Id);
            if (virtualPlId == 0) continue;

            var list = new List<Song>();
            if (rp.SongIds != null)
            {
                foreach (var sid in rp.SongIds)
                {
                    if (byOrigId.TryGetValue(sid, out var s)) list.Add(s);
                }
            }

            mappedPlaylists.Add(new Playlist
            {
                Id = virtualPlId,
                Name = rp.Name ?? "Playlist",
                CoverUri = list.FirstOrDefault(s => !string.IsNullOrEmpty(s.ArtUri))?.ArtUri
            });
            plSongsMap[virtualPlId] = list;
        }

        lock (_gate)
        {
            IsConnected = true;
            Host = host;
            Port = port;
            _code = code;
            DeviceName = string.IsNullOrWhiteSpace(deviceName) ? "Android" : deviceName;
            _songs = mappedSongs;
            _playlists = mappedPlaylists;
            _playlistSongs = plSongsMap;
        }
    }

    private async Task<string> VerifyPairAsync(string host, int port, string code, CancellationToken ct)
    {
        using var reqCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        reqCts.CancelAfter(TimeSpan.FromSeconds(5));
        using var resp = await _http.GetAsync($"http://{host}:{port}/pair?code={Uri.EscapeDataString(code)}", reqCts.Token);
        if (resp.StatusCode == HttpStatusCode.Unauthorized)
            throw new UnauthorizedAccessException("Código incorrecto. Verifica el código de 6 dígitos en tu celular.");
        if (resp.StatusCode == HttpStatusCode.TooManyRequests)
            throw new InvalidOperationException("Demasiados intentos incorrectos. Espera unos segundos e intenta otra vez.");
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync(reqCts.Token);
        var info = JsonSerializer.Deserialize<PairResponseDto>(body, JsonOpts);
        return info?.Device ?? "Android";
    }

    private static (string? Host, int Port, string Code) ParseInputs(string codeInput, string? manualHost)
    {
        var rawCode = (codeInput ?? "").Trim();
        var host = (manualHost ?? "").Trim();
        int port = 0;

        if (rawCode.Contains('.') && rawCode.Contains(':'))
        {
            var segs = rawCode.Split(':', StringSplitOptions.RemoveEmptyEntries);
            if (segs.Length == 2)
            {
                host = segs[0].Trim();
                rawCode = segs[1].Trim();
            }
            else if (segs.Length == 3 && int.TryParse(segs[1], out var p))
            {
                host = segs[0].Trim();
                port = p;
                rawCode = segs[2].Trim();
            }
        }

        if (!string.IsNullOrWhiteSpace(host) && host.Contains(':'))
        {
            var hParts = host.Split(':', 2);
            host = hParts[0].Trim();
            if (int.TryParse(hParts[1].Trim(), out var hp)) port = hp;
        }

        var digits = new string(rawCode.Where(char.IsDigit).ToArray());
        return (string.IsNullOrWhiteSpace(host) ? null : host, port, digits);
    }

    private async Task<(string Host, int Port, string Device)?> DiscoverViaUdpAsync(string code, CancellationToken ct)
    {
        try
        {
            using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
            udp.EnableBroadcast = true;
            var payload = Encoding.UTF8.GetBytes($"WAVE_DISCOVER_V1|{code}");

            var targets = new HashSet<IPAddress> { IPAddress.Broadcast };
            foreach (var bcast in GetSubnetBroadcastAddresses()) targets.Add(bcast);

            foreach (var addr in targets)
            {
                try { await udp.SendAsync(payload, payload.Length, new IPEndPoint(addr, DiscoveryUdpPort)); }
                catch { }
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(300, ct);
                    foreach (var addr in targets)
                    {
                        try { await udp.SendAsync(payload, payload.Length, new IPEndPoint(addr, DiscoveryUdpPort)); }
                        catch { }
                    }
                }
                catch { }
            }, ct);

            using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            waitCts.CancelAfter(TimeSpan.FromMilliseconds(1300));

            while (!waitCts.IsCancellationRequested)
            {
                var res = await udp.ReceiveAsync(waitCts.Token);
                var text = Encoding.UTF8.GetString(res.Buffer).Trim();
                if (text.StartsWith("WAVE_HERE_V1|", StringComparison.Ordinal))
                {
                    var parts = text.Split('|', 3);
                    if (parts.Length >= 2 && int.TryParse(parts[1], out var port))
                    {
                        var dev = parts.Length >= 3 ? parts[2] : "Android";
                        return (res.RemoteEndPoint.Address.ToString(), port, dev);
                    }
                }
            }
        }
        catch
        {
        }
        return null;
    }

    private async Task<(string Host, int Port, string Device)?> DiscoverViaLanPingAsync(string code, CancellationToken ct)
    {
        var candidateIps = GetLocalSubnetCandidateIps();
        if (candidateIps.Count == 0) return null;

        using var pingClient = new HttpClient { Timeout = TimeSpan.FromMilliseconds(450) };
        using var sem = new SemaphoreSlim(64, 64);
        using var sweepCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        sweepCts.CancelAfter(TimeSpan.FromSeconds(5));

        (string Host, int Port, string Device)? found = null;
        string? unauthorizedError = null;

        var tasks = candidateIps.Select(async ip =>
        {
            try
            {
                await sem.WaitAsync(sweepCts.Token);
            }
            catch { return; }

            try
            {
                if (found != null || sweepCts.IsCancellationRequested) return;
                using var resp = await pingClient.GetAsync($"http://{ip}:{DefaultHttpPort}/ping", sweepCts.Token);
                if (!resp.IsSuccessStatusCode) return;
                var json = await resp.Content.ReadAsStringAsync(sweepCts.Token);
                if (!json.Contains("wave-phone", StringComparison.OrdinalIgnoreCase)) return;

                try
                {
                    var device = await VerifyPairAsync(ip, DefaultHttpPort, code, sweepCts.Token);
                    found = (ip, DefaultHttpPort, device);
                    sweepCts.Cancel();
                }
                catch (UnauthorizedAccessException ex)
                {
                    unauthorizedError = ex.Message;
                }
            }
            catch
            {
            }
            finally
            {
                sem.Release();
            }
        }).ToList();

        try { await Task.WhenAll(tasks); } catch { }

        if (found != null) return found;
        if (!string.IsNullOrEmpty(unauthorizedError))
            throw new UnauthorizedAccessException(unauthorizedError);
        return null;
    }

    private static List<IPAddress> GetSubnetBroadcastAddresses()
    {
        var list = new List<IPAddress>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork || ua.IPv4Mask == null) continue;
                    var ipBytes = ua.Address.GetAddressBytes();
                    var maskBytes = ua.IPv4Mask.GetAddressBytes();
                    var bcast = new byte[4];
                    for (int i = 0; i < 4; i++) bcast[i] = (byte)(ipBytes[i] | ~maskBytes[i]);
                    list.Add(new IPAddress(bcast));
                }
            }
        }
        catch { }
        return list;
    }

    private static List<string> GetLocalSubnetCandidateIps()
    {
        var result = new List<string>();
        var seen = new HashSet<string>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;
                var desc = (ni.Description + " " + ni.Name).ToLowerInvariant();
                if (desc.Contains("virtual") || desc.Contains("vmware") || desc.Contains("vbox") || desc.Contains("hyper-v") || desc.Contains("wsl"))
                    continue;

                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var bytes = ua.Address.GetAddressBytes();
                    if (bytes[0] == 127 || (bytes[0] == 169 && bytes[1] == 254)) continue;
                    var own = ua.Address.ToString();
                    var prefix = $"{bytes[0]}.{bytes[1]}.{bytes[2]}.";
                    for (int host = 1; host <= 254; host++)
                    {
                        var candidate = prefix + host;
                        if (candidate != own && seen.Add(candidate))
                            result.Add(candidate);
                    }
                }
            }
        }
        catch { }
        return result;
    }

    private sealed class PairResponseDto
    {
        public bool Ok { get; set; }
        public string? Device { get; set; }
    }

    private sealed class RemoteSongDto
    {
        public long Id { get; set; }
        public string? FileName { get; set; }
        public long FileSize { get; set; }
        public string? Title { get; set; }
        public string? Artist { get; set; }
        public string? Album { get; set; }
        public long DurationMs { get; set; }
        public int? TrackNo { get; set; }
        public int? Year { get; set; }
        public string? MimeType { get; set; }
        public long DateAddedSec { get; set; }
        public bool IsFavorite { get; set; }
        public string? Genre { get; set; }
        public bool HasArt { get; set; }
        public string? Ext { get; set; }
    }

    private sealed class RemotePlaylistDto
    {
        public long Id { get; set; }
        public string? Name { get; set; }
        public List<long>? SongIds { get; set; }
    }
}
