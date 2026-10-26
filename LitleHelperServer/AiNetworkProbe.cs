using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace PixelHelper.Diagnostics;

public sealed record NetworkProbeRequest(string Url, string[] AllowedAddresses);
public sealed record NetworkProbeResult(string Source, string Target, DateTimeOffset CheckedAt, string Status,
    int? HttpStatus = null, long? DnsMs = null, long? ConnectMs = null, long? HeadersMs = null, bool? TlsValidated = null);

// Shared with the WPF client. No shell, cookies, credentials, response body or redirects.
public static class AiNetworkProbe
{
    public static bool IsSafeAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) || address.IsIPv6LinkLocal || address.IsIPv6Multicast || address.IsIPv6SiteLocal)
            return false;
        byte[] bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
            return bytes[0] is not (0 or 127) && bytes[0] < 224 && !(bytes[0] == 169 && bytes[1] == 254) &&
                !address.Equals(IPAddress.Parse("100.100.100.200")); // Cloud metadata, including private-range variants.
        // Exclude IPv4-compatible, NAT64 and tunnelling forms; normal global/ULA IPv6 is allowed.
        return address.AddressFamily == AddressFamily.InterNetworkV6 && address.ScopeId == 0 && !address.Equals(IPAddress.Parse("fd00:ec2::254")) &&
            !address.Equals(IPAddress.IPv6None) && !(bytes.Take(12).All(b => b == 0)) &&
            !(bytes[0] == 0 && bytes[1] == 0x64 && bytes[2] == 0xff && bytes[3] == 0x9b) &&
            !(bytes[0] == 0x20 && bytes[1] == 0x02) && !(bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0 && bytes[3] == 0);
    }
    public static Uri Validate(NetworkProbeRequest request)
    {
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
            uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.AbsoluteUri.Length > 1500 ||
            request.AllowedAddresses is null || request.AllowedAddresses.Length is < 1 or > 16 ||
            request.AllowedAddresses.Any(a => !IPAddress.TryParse(a, out var parsed) || !IsSafeAddress(parsed)))
            throw new ArgumentException("Configure an HTTP(S) target with explicit safe IP addresses and no credentials/query");
        if (IPAddress.TryParse(uri.Host.Trim('[', ']'), out var literal) && !IsSafeAddress(literal))
            throw new ArgumentException("Loopback, metadata and special network addresses are not diagnostic targets");
        return uri;
    }
    public static async Task<NetworkProbeResult> RunAsync(NetworkProbeRequest request, string source, CancellationToken cancellation)
    {
        var uri = Validate(request);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(12));
        var clock = Stopwatch.StartNew(); long? dnsMs = null, connectMs = null;
        try
        {
            var allowed = request.AllowedAddresses.Select(IPAddress.Parse).Select(Normalize).ToHashSet();
            var addresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost, timeout.Token);
            dnsMs = clock.ElapsedMilliseconds;
            if (addresses.Length is < 1 or > 16 || addresses.Any(a => !IsSafeAddress(a) || !allowed.Contains(Normalize(a))))
                return new(source, uri.Host, DateTimeOffset.UtcNow, "AddressNotAllowed", DnsMs: dnsMs);
            // Pin the resolved addresses, so a second DNS response cannot redirect the request.
            using var handler = new SocketsHttpHandler
            {
                UseProxy = false, UseCookies = false, AllowAutoRedirect = false, MaxResponseHeadersLength = 16,
                ConnectCallback = async (context, token) =>
                {
                    if (!context.DnsEndPoint.Host.Equals(uri.DnsSafeHost, StringComparison.OrdinalIgnoreCase) || context.DnsEndPoint.Port != uri.Port)
                        throw new HttpRequestException("Unexpected endpoint");
                    foreach (var address in addresses)
                    {
                        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                        try
                        {
                            await socket.ConnectAsync(new IPEndPoint(address, uri.Port), token);
                            connectMs = clock.ElapsedMilliseconds - dnsMs;
                            return new NetworkStream(socket, ownsSocket: true);
                        }
                        catch { socket.Dispose(); if (token.IsCancellationRequested) throw; }
                    }
                    throw new HttpRequestException("Connection failed");
                }
            };
            using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            using var message = new HttpRequestMessage(HttpMethod.Get, uri);
            message.Headers.UserAgent.ParseAdd("LitleHelper-Diagnostics/1.0");
            using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            int status = (int)response.StatusCode;
            return new(source, uri.Host, DateTimeOffset.UtcNow, status is >= 300 and < 400 ? "RedirectNotFollowed" : "ResponseReceived", status,
                dnsMs, connectMs, clock.ElapsedMilliseconds, uri.Scheme == "https" ? true : null);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { return new(source, uri.Host, DateTimeOffset.UtcNow, "TimedOut", DnsMs: dnsMs, ConnectMs: connectMs); }
        catch (Exception ex) when (ex is HttpRequestException or SocketException)
        { return new(source, uri.Host, DateTimeOffset.UtcNow, "ConnectionOrTlsFailed", DnsMs: dnsMs, ConnectMs: connectMs); }
    }
    private static IPAddress Normalize(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
}
