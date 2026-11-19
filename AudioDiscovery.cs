using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text.Json;

namespace Vap1;

/// <summary>One audio board, from its HTTP <c>/status</c> page on port 5000.</summary>
public sealed class AudioAnnouncement
{
    public const string KindValue = "verpixeld-audio";

    public string Kind { get; init; } = KindValue;
    public string Host { get; init; } = "";
    public string Name { get; init; } = "";
    public string Version { get; init; } = "";
    public int UdpPort { get; init; } = Vap1Protocol.Port;
    public int WebPort { get; init; } = Vap1Protocol.WebPort;
    public int Volume { get; init; }
    public bool Muted { get; init; }
    public string Via { get; init; } = "";

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Host : Name;
}

/// <summary>Finds audio boards the same way PixPlane finds panels: GET /status on port 5000.</summary>
public static class AudioDiscovery
{
    public static async Task<AudioAnnouncement?> QueryStatusAsync(string host, int webPort = Vap1Protocol.WebPort,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(host)) return null;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(800) };
            using var resp = await http.GetAsync($"http://{host}:{webPort}/status", ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return TryParseStatusJson(body, host, "http", out var board) ? board : null;
        }
        catch { return null; }
    }

    public static async Task<IReadOnlyList<AudioAnnouncement>> ScanAsync(
        TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var wait = timeout ?? TimeSpan.FromSeconds(2);
        var found = new ConcurrentDictionary<string, AudioAnnouncement>(StringComparer.OrdinalIgnoreCase);
        var ips = SubnetHosts();
        using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(250) };
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(wait);
        try
        {
            await Parallel.ForEachAsync(ips, new ParallelOptions
            {
                MaxDegreeOfParallelism = 32,
                CancellationToken = cts.Token
            }, async (ip, token) =>
            {
                try
                {
                    using var resp = await http.GetAsync($"http://{ip}:{Vap1Protocol.WebPort}/status", token)
                        .ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode) return;
                    var body = await resp.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                    if (TryParseStatusJson(body, ip, "http", out var board))
                        found.TryAdd(board.Host, board);
                }
                catch { /* not a board */ }
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }

        return found.Values.OrderBy(b => b.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static bool TryParseStatusJson(string json, string host, string via, out AudioAnnouncement board)
    {
        board = null!;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            var kind = Str(r, "kind");
            if (!string.Equals(kind, AudioAnnouncement.KindValue, StringComparison.OrdinalIgnoreCase))
                return false;
            var ip = Str(r, "ip");
            if (string.IsNullOrEmpty(ip)) ip = host;
            board = new AudioAnnouncement
            {
                Kind = kind,
                Host = ip,
                Name = Str(r, "host"),
                Version = Str(r, "ver"),
                UdpPort = Int(r, "udp", Vap1Protocol.Port),
                WebPort = Int(r, "web", Vap1Protocol.WebPort),
                Volume = Int(r, "vol", 70),
                Muted = Int(r, "mute", 0) != 0,
                Via = via
            };
            return !string.IsNullOrEmpty(board.Host);
        }
        catch { return false; }
    }

    private static HashSet<string> SubnetHosts()
    {
        var ips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                    continue;
                foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) continue;
                    var bytes = ua.Address.GetAddressBytes();
                    var mask = ua.IPv4Mask?.GetAddressBytes();
                    if (mask is not { Length: 4 }) continue;
                    if (mask[0] != 255 || mask[1] != 255 || mask[2] != 255) continue;
                    var prefix = $"{bytes[0]}.{bytes[1]}.{bytes[2]}.";
                    for (var i = 1; i <= 254; i++) ips.Add(prefix + i);
                }
            }
        }
        catch { /* no interfaces */ }
        return ips;
    }

    private static string Str(JsonElement r, string name) =>
        r.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";

    private static int Int(JsonElement r, string name, int fallback)
    {
        if (!r.TryGetProperty(name, out var p)) return fallback;
        return p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var n) ? n : fallback;
    }
}
