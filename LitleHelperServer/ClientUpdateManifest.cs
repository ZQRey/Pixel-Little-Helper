using System.Reflection;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PixelHelper.Updates;

public record ClientUpdateManifest(string Version, string Sha256, long Size, string Signature)
{
    public const long MaximumSize = 200 * 1024 * 1024;
    public byte[] SignedData() => Encoding.UTF8.GetBytes($"PixelHelper-MSI-v1\n{Version}\n{Sha256}\n{Size.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
    public bool Valid()
    {
        if (string.IsNullOrEmpty(Version) || Sha256 == null || Signature == null || !System.Version.TryParse(Version, out var version) || version.Revision != -1 || version.Build < 0 || version.Major > 255 || version.Minor > 255 || version.Build > 65535 ||
            !Regex.IsMatch(Sha256, "^[a-f0-9]{64}$") || Size is <= 0 or > MaximumSize || Signature.Length > 2000) return false;
        try
        {
            using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("PixelHelper.UpdatePublicKey")!;
            using var reader = new StreamReader(resource); using var rsa = RSA.Create(); rsa.ImportFromPem(reader.ReadToEnd());
            return rsa.VerifyData(SignedData(), Convert.FromBase64String(Signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException) { return false; }
    }
}
