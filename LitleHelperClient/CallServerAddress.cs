namespace PixelHelper;

internal static class CallServerAddress
{
    internal static string Secure(string address)
    {
        var uri = new Uri(address.TrimEnd('/'), UriKind.Absolute);
        if (uri.Scheme == Uri.UriSchemeHttps || (uri.IsLoopback && uri.Scheme == Uri.UriSchemeHttp))
            return uri.AbsoluteUri.TrimEnd('/');
        if (uri.Scheme != Uri.UriSchemeHttp)
            throw new ArgumentException("Для звонков необходим HTTPS адрес сервера.");
        var secure = new UriBuilder(uri) { Scheme = Uri.UriSchemeHttps };
        if (uri.IsDefaultPort) secure.Port = -1;
        return secure.Uri.AbsoluteUri.TrimEnd('/');
    }
}
