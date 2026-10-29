using System.IO;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace PixelHelper;

public static class WireGuardCrypto
{
    private static readonly BigInteger P = BigInteger.Pow(2, 255) - 19;
    private const long A24 = 121665;

    public static byte[] GeneratePrivateKey()
    {
        byte[] k = new byte[32];
        RandomNumberGenerator.Fill(k);
        k[0] &= 248;
        k[31] &= 127;
        k[31] |= 64;
        return k;
    }

    public static byte[] ComputePublicKey(byte[] privateKey)
    {
        byte[] kBytes = (byte[])privateKey.Clone();
        kBytes[0] &= 248;
        kBytes[31] &= 127;
        kBytes[31] |= 64;

        byte[] padded = new byte[33];
        Buffer.BlockCopy(kBytes, 0, padded, 0, 32);
        BigInteger k = new BigInteger(padded);

        BigInteger x1 = 9;
        BigInteger x2 = 1, z2 = 0;
        BigInteger x3 = 9, z3 = 1;
        int swap = 0;

        for (int t = 254; t >= 0; t--)
        {
            int kt = (int)((k >> t) & 1);
            swap ^= kt;
            if (swap != 0)
            {
                (x2, x3) = (x3, x2);
                (z2, z3) = (z3, z2);
            }
            swap = kt;

            BigInteger a = (x2 + z2) % P;
            BigInteger aa = (a * a) % P;
            BigInteger b = (x2 - z2 + P) % P;
            BigInteger bb = (b * b) % P;
            BigInteger e = (aa - bb + P) % P;
            BigInteger c = (x3 + z3) % P;
            BigInteger d = (x3 - z3 + P) % P;
            BigInteger da = (d * a) % P;
            BigInteger cb = (c * b) % P;

            BigInteger daPlusCb = (da + cb) % P;
            x3 = (daPlusCb * daPlusCb) % P;

            BigInteger daMinusCb = (da - cb + P) % P;
            z3 = (x1 * ((daMinusCb * daMinusCb) % P)) % P;

            x2 = (aa * bb) % P;
            z2 = (e * ((aa + A24 * e) % P)) % P;
        }

        if (swap != 0)
        {
            (x2, x3) = (x3, x2);
            (z2, z3) = (z3, z2);
        }

        BigInteger inv = BigInteger.ModPow(z2, P - 2, P);
        BigInteger res = (x2 * inv) % P;

        byte[] raw = res.ToByteArray();
        byte[] result = new byte[32];
        for (int i = 0; i < Math.Min(raw.Length, 32); i++)
            result[i] = raw[i];
        return result;
    }

    public static (string PrivateKeyBase64, string PublicKeyBase64) GenerateKeyPair()
    {
        byte[] priv = GeneratePrivateKey();
        byte[] pub = ComputePublicKey(priv);
        return (Convert.ToBase64String(priv), Convert.ToBase64String(pub));
    }

    public static string GenerateConfigContent(WireGuardSettings config)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[Interface]");
        sb.AppendLine($"PrivateKey = {config.ClientPrivateKey}");
        if (!string.IsNullOrWhiteSpace(config.ClientAddress))
            sb.AppendLine($"Address = {config.ClientAddress}");
        sb.AppendLine();
        sb.AppendLine("[Peer]");
        sb.AppendLine($"PublicKey = {config.ServerPublicKey}");
        if (!string.IsNullOrWhiteSpace(config.ServerEndpoint))
            sb.AppendLine($"Endpoint = {config.ServerEndpoint}");
        sb.AppendLine($"AllowedIPs = {(string.IsNullOrWhiteSpace(config.AllowedIPs) ? "172.16.0.0/16" : config.AllowedIPs)}");
        sb.AppendLine("PersistentKeepalive = 25");
        return sb.ToString();
    }

    public static bool IsTunnelActive()
    {
        try
        {
            var interfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
            foreach (var ni in interfaces)
            {
                if (ni.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
                {
                    string desc = (ni.Description + " " + ni.Name).ToLowerInvariant();
                    if (desc.Contains("wireguard") || desc.Contains("wg_pixel") || desc.Contains("wintun"))
                        return true;
                }
            }
        }
        catch { }
        return false;
    }
}

