using System.Formats.Asn1;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

public sealed class MockAd : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.IPv6Loopback, 0);
    private readonly CancellationTokenSource stop = new();
    private readonly X509Certificate2 certificate;
    private readonly Task loop;
    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
    public string Pem => certificate.ExportCertificatePem();
    public MockAd()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder(); san.AddDnsName("localhost"); request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        certificate = new X509Certificate2(generated.Export(X509ContentType.Pfx), "", X509KeyStorageFlags.UserKeySet);
        listener.Start(); loop = Accept();
    }
    private async Task Accept()
    {
        try { while (!stop.IsCancellationRequested) { var client = await listener.AcceptTcpClientAsync(stop.Token); _ = Handle(client); } }
        catch (OperationCanceledException) { }
    }
    private enum Result { Success = 0, InvalidCredentials = 49 }
    private static byte[] ResultMessage(int id, int op, Result code)
    {
        var w = new AsnWriter(AsnEncodingRules.BER); w.PushSequence(); w.WriteInteger(id);
        var tag = new Asn1Tag(TagClass.Application, op, true); w.PushSequence(tag); w.WriteEnumeratedValue(code); w.WriteOctetString([]); w.WriteOctetString([]); w.PopSequence(tag); w.PopSequence(); return w.Encode();
    }
    private static byte[] Entry(int id)
    {
        var w = new AsnWriter(AsnEncodingRules.BER); w.PushSequence(); w.WriteInteger(id);
        var tag = new Asn1Tag(TagClass.Application, 4, true); w.PushSequence(tag); w.WriteOctetString(Encoding.UTF8.GetBytes("CN=Alice,DC=ad,DC=test")); w.PushSequence();
        foreach (var (name, value) in new[] { ("sAMAccountName", "alice"), ("displayName", "Alice AD"), ("userAccountControl", "512") })
        { w.PushSequence(); w.WriteOctetString(Encoding.UTF8.GetBytes(name)); w.PushSetOf(); w.WriteOctetString(Encoding.UTF8.GetBytes(value)); w.PopSetOf(); w.PopSequence(); }
        w.PopSequence(); w.PopSequence(tag); w.PopSequence(); return w.Encode();
    }
    private async Task Handle(TcpClient client)
    {
        using (client) using (var ssl = new SslStream(client.GetStream()))
        {
            try
            {
                await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = certificate }, stop.Token);
                bool authenticated = false;
                while (!stop.IsCancellationRequested)
                {
                    byte[] prefix = new byte[2]; await ssl.ReadExactlyAsync(prefix, stop.Token);
                    int count = prefix[1], extra = 0;
                    if ((count & 128) != 0) { extra = count & 127; if (extra > 4) return; byte[] length = new byte[extra]; await ssl.ReadExactlyAsync(length, stop.Token); count = 0; foreach (byte b in length) count = count * 256 + b; }
                    if (count > 65536) return;
                    byte[] payload = new byte[count]; await ssl.ReadExactlyAsync(payload, stop.Token);
                    var header = new List<byte> { 0x30 }; if (count < 128) header.Add((byte)count); else { byte[] encoded = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(count)).SkipWhile(b => b == 0).ToArray(); header.Add((byte)(128 + encoded.Length)); header.AddRange(encoded); }
                    var outer = new AsnReader(header.Concat(payload).ToArray(), AsnEncodingRules.BER).ReadSequence(); int id = (int)outer.ReadInteger(); int op = outer.PeekTag().TagValue;
                    if (op == 0)
                    {
                        var bind = outer.ReadSequence(new Asn1Tag(TagClass.Application, 0, true)); bind.ReadInteger(); string user = Encoding.UTF8.GetString(bind.ReadOctetString()); string password = Encoding.UTF8.GetString(bind.ReadOctetString(new Asn1Tag(TagClass.ContextSpecific, 0)));
                        authenticated = user == "alice@ad.test" && password == "AD-test-password";
                        await ssl.WriteAsync(ResultMessage(id, 1, authenticated ? Result.Success : Result.InvalidCredentials), stop.Token);
                    }
                    else if (op == 3 && authenticated) { await ssl.WriteAsync(Entry(id), stop.Token); await ssl.WriteAsync(ResultMessage(id, 5, Result.Success), stop.Token); }
                    else return;
                }
            }
            catch (Exception ex) when (ex is IOException or System.Security.Authentication.AuthenticationException or OperationCanceledException) { if (ex is System.Security.Authentication.AuthenticationException) Console.WriteLine("Mock LDAPS TLS: " + ex.Message); }
        }
    }
    public async ValueTask DisposeAsync() { stop.Cancel(); listener.Stop(); await loop; certificate.Dispose(); stop.Dispose(); }
}
