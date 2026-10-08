using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

if (args.Length < 6)
{
    Console.Error.WriteLine("Usage: SignTool <version> <hash> <size> <privateKeyPath> <publicKeyPath> <outputPath>");
    return 1;
}

string version = args[0];
string hash = args[1];
if (!long.TryParse(args[2], out long size))
{
    Console.Error.WriteLine("Invalid size argument");
    return 1;
}

string privateKeyPath = args[3];
string publicKeyPath = args[4];
string outputPath = args[5];

if (!File.Exists(privateKeyPath))
{
    Console.Error.WriteLine($"Private key not found: {privateKeyPath}");
    return 1;
}

if (!File.Exists(publicKeyPath))
{
    Console.Error.WriteLine($"Public key not found: {publicKeyPath}");
    return 1;
}

byte[] data = Encoding.UTF8.GetBytes($"PixelHelper-MSI-v1\n{version}\n{hash}\n{size}");

using var rsa = RSA.Create();
rsa.ImportFromPem(File.ReadAllText(privateKeyPath));

using var trusted = RSA.Create();
trusted.ImportFromPem(File.ReadAllText(publicKeyPath));

if (rsa.ExportSubjectPublicKeyInfoPem() != trusted.ExportSubjectPublicKeyInfoPem())
{
    Console.Error.WriteLine("Signing key does not match client public key");
    return 2;
}

byte[] sig = rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
string signature = Convert.ToBase64String(sig);

var manifest = new
{
    version,
    sha256 = hash,
    size,
    signature
};

string json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(outputPath, json);
Console.WriteLine($"Signed update manifest: {outputPath}");
return 0;
