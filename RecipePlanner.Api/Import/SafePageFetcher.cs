using System.Net;
using System.Net.Sockets;
using System.Text;

namespace RecipePlanner.Api.Import;

public interface IPageFetcher
{
    /// <summary>Downloads a public web page's HTML, or throws <see cref="ImportException"/> with a message safe to show the user.</summary>
    Task<FetchedPage> FetchAsync(Uri url, CancellationToken ct);
}

public record FetchedPage(Uri FinalUrl, string Html);

public class ImportException(string message) : Exception(message);

// Fetching a URL a user typed is a classic way to attack a server (SSRF): point it at an internal address
// like Azure's metadata service (169.254.169.254, which hands out credentials) or another container.
// Defenses, in order:
//   1. Only http/https on the standard ports, no usernames or passwords in the URL.
//   2. Every connection, including each redirect and each DNS answer, is checked at the moment of connecting
//      (ConnectCallback), so a hostname that resolves to a public IP first and a private one later
//      (DNS rebinding) still can't get through.
//   3. Size and time limits, and HTML only.
public sealed class SafePageFetcher : IPageFetcher
{
    public const int MaxBytes = 3 * 1024 * 1024;
    public const int MaxRedirects = 5;
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(12);

    private readonly HttpClient _http;

    public SafePageFetcher()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,           // redirects are followed by hand below, so each hop is re-checked
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            UseProxy = false,
            ConnectCallback = ConnectToPublicAddressAsync
        };
        _http = new HttpClient(handler) { Timeout = Timeout };
        // Some sites refuse requests without a browser-like agent; be honest about who we are
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; RecipePlannerImporter/1.0)");
        _http.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml");
    }

    public async Task<FetchedPage> FetchAsync(Uri url, CancellationToken ct)
    {
        var current = url;
        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            CheckUrl(current);
            using var response = await SendAsync(current, ct);

            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
            {
                current = location.IsAbsoluteUri ? location : new Uri(current, location);
                continue;
            }
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
                throw new ImportException("That site doesn't allow apps to read its recipes. Try copying the recipe in by hand.");
            if (!response.IsSuccessStatusCode)
                throw new ImportException($"That page couldn't be loaded (the site answered {(int)response.StatusCode}).");

            var type = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!type.Contains("html", StringComparison.OrdinalIgnoreCase))
                throw new ImportException("That link isn't a web page. Paste the link to the recipe page itself.");
            if (response.Content.Headers.ContentLength > MaxBytes)
                throw new ImportException("That page is too large to import.");

            return new FetchedPage(current, await ReadLimitedAsync(response, ct));
        }
        throw new ImportException("That link redirects too many times.");
    }

    private async Task<HttpResponseMessage> SendAsync(Uri url, CancellationToken ct)
    {
        try
        {
            return await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (HttpRequestException ex) when (ex.InnerException is ImportException blocked)
        {
            throw blocked;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new ImportException("That page couldn't be reached. Check the link and try again.");
        }
    }

    private static async Task<string> ReadLimitedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxBytes) throw new ImportException("That page is too large to import.");
            buffer.Write(chunk, 0, read);
        }
        var charset = response.Content.Headers.ContentType?.CharSet;
        Encoding encoding;
        try { encoding = string.IsNullOrEmpty(charset) ? Encoding.UTF8 : Encoding.GetEncoding(charset.Trim('"')); }
        catch (ArgumentException) { encoding = Encoding.UTF8; }
        return encoding.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    /// <summary>Rules for the URL itself, before any network traffic</summary>
    public static void CheckUrl(Uri url)
    {
        if (!url.IsAbsoluteUri || (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp))
            throw new ImportException("Use a link that starts with https:// or http://.");
        if (!string.IsNullOrEmpty(url.UserInfo))
            throw new ImportException("Links with a username or password can't be imported.");
        if (!url.IsDefaultPort && url.Port is not (80 or 443))
            throw new ImportException("That link uses an unusual port and can't be imported.");
        if (IPAddress.TryParse(url.DnsSafeHost, out var ip) && !IsPublic(ip))
            throw new ImportException("That link points to a private address and can't be imported.");
        if (url.DnsSafeHost.Equals("localhost", StringComparison.OrdinalIgnoreCase) || url.DnsSafeHost.EndsWith(".internal", StringComparison.OrdinalIgnoreCase)
            || url.DnsSafeHost.EndsWith(".local", StringComparison.OrdinalIgnoreCase) || !url.DnsSafeHost.Contains('.'))
            throw new ImportException("That link points to a private address and can't be imported.");
    }

    // Runs for every TCP connection the HttpClient makes: resolve, check, and connect to the checked address
    private static async ValueTask<Stream> ConnectToPublicAddressAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var host = context.DnsEndPoint.Host;
        var addresses = IPAddress.TryParse(host, out var literal) ? [literal] : await Dns.GetHostAddressesAsync(host, ct);
        var allowed = addresses.Where(IsPublic).ToList();
        if (allowed.Count == 0 || allowed.Count != addresses.Length)
            throw new ImportException("That link points to a private address and can't be imported.");

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(allowed.ToArray(), context.DnsEndPoint.Port, ct);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>True only for ordinary internet addresses</summary>
    public static bool IsPublic(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any) || ip.Equals(IPAddress.Broadcast))
            return false;

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return !(b[0] == 0                                   // "this network"
                || b[0] == 10                                    // private
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)    // carrier-grade NAT
                || b[0] == 127                                   // loopback
                || (b[0] == 169 && b[1] == 254)                  // link-local, incl. cloud metadata 169.254.169.254
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)     // private
                || (b[0] == 192 && b[1] == 0 && b[2] == 0)       // IETF protocol assignments
                || (b[0] == 192 && b[1] == 0 && b[2] == 2)       // documentation
                || (b[0] == 192 && b[1] == 168)                  // private
                || (b[0] == 198 && (b[1] == 18 || b[1] == 19))   // benchmarking
                || (b[0] == 198 && b[1] == 51 && b[2] == 100)    // documentation
                || (b[0] == 203 && b[1] == 0 && b[2] == 113)     // documentation
                || b[0] >= 224);                                 // multicast and reserved
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b = ip.GetAddressBytes();
            return !(ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast
                || (b[0] & 0xFE) == 0xFC                         // unique local fc00::/7
                || (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0D && b[3] == 0xB8) // documentation 2001:db8::/32
                || (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xFF && b[3] == 0x9B) // NAT64 64:ff9b::/96, can reach IPv4 private ranges
                || ip.IsIPv6Teredo);
        }
        return false;
    }
}
