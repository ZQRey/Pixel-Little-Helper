using System.Net;
using System.Net.Sockets;

namespace LitleHelperServer;

public static class NetworkAccess
{
    public static readonly string[] DefaultRegistrationNetworks =
    [
        "172.16.16.0/23",
        "192.168.0.0/23",
        "192.168.2.0/23",
        "10.0.0.0/23"
    ];

    public static IPAddress? CallerAddress(HttpContext context)
    {
        string value = context.Request.Headers["X-Real-IP"].ToString();
        if (string.IsNullOrWhiteSpace(value)) return context.Connection.RemoteIpAddress;
        return IPAddress.TryParse(value.Trim(), out var address) ? Normalize(address) : context.Connection.RemoteIpAddress;
    }

    public static bool IsAllowed(IPAddress? address, IEnumerable<string>? networks)
    {
        if (address == null || networks == null) return false;
        address = Normalize(address);
        byte[] bytes = address.GetAddressBytes();
        foreach (string network in networks)
        {
            if (!TryParse(network, out var networkAddress, out int prefix)) continue;
            if (networkAddress.AddressFamily != address.AddressFamily) continue;
            if (Matches(bytes, networkAddress.GetAddressBytes(), prefix)) return true;
        }
        return false;
    }

    public static IReadOnlyList<string> NormalizeNetworks(IEnumerable<string>? values)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (values == null) return result;
        foreach (string value in values)
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            foreach (string item in value.Split([',', ';', ' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!TryParse(item, out var address, out int prefix))
                    throw new ArgumentException($"Некорректная сеть «{item}». Укажите IPv4/IPv6-адрес или CIDR, например 172.16.16.0/23.");
                string normalized = address + "/" + prefix;
                if (seen.Add(normalized)) result.Add(normalized);
            }
        }
        if (result.Count > 100) throw new ArgumentException("Можно указать не более 100 сетей.");
        if (result.Sum(x => x.Length + 1) > 4000) throw new ArgumentException("Список сетей слишком длинный.");
        return result;
    }

    private static IPAddress Normalize(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    private static bool TryParse(string value, out IPAddress network, out int prefix)
    {
        network = IPAddress.None;
        prefix = 0;
        string text = value.Trim();
        if (text.Length == 0 || text.Length > 80) return false;
        int slash = text.IndexOf('/');
        string addressPart = slash < 0 ? text : text[..slash];
        string prefixPart = slash < 0 ? "" : text[(slash + 1)..];
        if (!IPAddress.TryParse(addressPart, out var address)) return false;
        address = Normalize(address);
        bool ipv4 = address.AddressFamily == AddressFamily.InterNetwork;
        int maxPrefix = ipv4 ? 32 : 128;
        if (slash < 0) prefix = maxPrefix;
        else if (!int.TryParse(prefixPart, out prefix) || prefix < 0 || prefix > maxPrefix) return false;

        byte[] bytes = address.GetAddressBytes();
        int fullBytes = prefix / 8;
        int remainingBits = prefix % 8;
        for (int i = 0; i < bytes.Length; i++)
        {
            if (i < fullBytes) continue;
            bytes[i] = i == fullBytes && remainingBits > 0
                ? (byte)(bytes[i] & (0xFF << (8 - remainingBits)))
                : (byte)0;
        }
        network = new IPAddress(bytes);
        return true;
    }

    private static bool Matches(byte[] address, byte[] network, int prefix)
    {
        if (address.Length != network.Length) return false;
        int fullBytes = prefix / 8;
        int remainingBits = prefix % 8;
        for (int i = 0; i < fullBytes; i++)
            if (address[i] != network[i]) return false;
        if (remainingBits == 0) return true;
        int mask = 0xFF << (8 - remainingBits);
        return (address[fullBytes] & mask) == (network[fullBytes] & mask);
    }
}
