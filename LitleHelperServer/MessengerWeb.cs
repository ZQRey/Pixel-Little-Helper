using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace LitleHelperServer;

public static class MessengerWeb
{
    private const string Cookie = "__Host-HelperMessenger";
    private static bool SameOrigin(HttpRequest request) => !request.Headers.ContainsKey("Origin") || request.Headers.Origin == $"{request.Scheme}://{request.Host}";
    private static bool Safe(HttpRequest request) => SameOrigin(request) && (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) || request.Headers["X-Messenger-Web"] == "1");
    private static void Session(HttpContext context, PanelUser user, IConfiguration config)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Cookies.Append(Cookie, Messenger.Token(user, config), new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Path = "/", MaxAge = TimeSpan.FromHours(8), IsEssential = true });
    }
    public static void Map(WebApplication app)
    {
        // Same restricted JWT and revocation checks as desktop; cookie is accepted only on messenger routes.
        var options = app.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get("Bearer");
        var original = options.Events.OnMessageReceived;
        options.Events.OnMessageReceived = async context =>
        {
            await original(context);
            if ((!context.Request.Path.StartsWithSegments("/api/messenger") && !context.Request.Path.StartsWithSegments("/messengerHub")) || context.Request.Headers.ContainsKey("Authorization") || !string.IsNullOrEmpty(context.Token)) return;
            if (context.Request.Cookies.TryGetValue(Cookie, out var token))
            {
                if (!context.Request.IsHttps || !Safe(context.Request)) { context.Fail("Messenger browser request rejected"); return; }
                context.Token = token;
            }
        };
        app.MapGet("/api/messenger/web/windows", async (HttpContext http, IMessengerKerberos kerberos, IMessengerWindowsDirectory directory, HelperDb db, IConfiguration config, MessengerSettings settings, CancellationToken cancellation) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            if (!http.Request.IsHttps || !SameOrigin(http.Request)) return Results.BadRequest();
            if (!settings.Value.Enabled) return Results.Json(new { error = "Мессенджер отключён администратором." }, statusCode:403);
            string header = http.Request.Headers.Authorization.ToString();
            IResult Challenge() { http.Response.Headers.WWWAuthenticate = "Negotiate"; return Results.Unauthorized(); }
            if (!header.StartsWith("Negotiate ", StringComparison.OrdinalIgnoreCase) || header.Length > 65536) return Challenge();
            KerberosIdentity verified;
            try { verified = kerberos.Authenticate(header[10..]); }
            catch(Exception ex) when(ex is System.Security.Authentication.AuthenticationException or FormatException or ArgumentException) { return Challenge(); }
            var user = await Messenger.ResolveAdUserAsync(await directory.FindAsync(verified.Name,cancellation),db,cancellation);
            if (!string.IsNullOrEmpty(verified.ResponseToken)) http.Response.Headers.WWWAuthenticate = "Negotiate " + verified.ResponseToken;
            Session(http,user,config); return Results.Ok(new { user.Id,user.FullName });
        }).RequireRateLimiting("login");
        app.MapPost("/api/messenger/web/login", async (LoginRequest request,HttpContext http,IAdAuthentication ad,HelperDb db,IConfiguration config,MessengerSettings settings,CancellationToken cancellation) =>
        {
            if (!http.Request.IsHttps || !Safe(http.Request)) return Results.BadRequest();
            if (!settings.Value.Enabled) return Results.Json(new { error = "Мессенджер отключён администратором." },statusCode:403);
            if(request.Username.Length>100 || request.Password.Length>256) return Results.Unauthorized();
            AdIdentity identity; try { identity=await ad.AuthenticateAsync(request,cancellation); } catch(UnauthorizedAccessException) { return Results.Unauthorized(); }
            var user=await Messenger.ResolveAdUserAsync(identity,db,cancellation); Session(http,user,config); return Results.Ok(new { user.Id,user.FullName });
        }).RequireRateLimiting("login");
        app.MapPost("/api/messenger/web/logout", (HttpContext http) =>
        {
            if (!http.Request.IsHttps || !Safe(http.Request)) return Results.BadRequest();
            http.Response.Cookies.Delete(Cookie,new CookieOptions { Secure=true,HttpOnly=true,SameSite=SameSiteMode.Strict,Path="/" }); return Results.Ok();
        });
    }
}
