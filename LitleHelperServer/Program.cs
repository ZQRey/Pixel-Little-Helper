using LitleHelperServer;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
Directory.CreateDirectory("data");
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine("data", "dataprotection")));
if (string.IsNullOrWhiteSpace(builder.Configuration["Jwt:SigningKey"]))
{
    string keyPath = Path.Combine("data", "jwt.key");
    if (!File.Exists(keyPath)) File.WriteAllText(keyPath, Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)));
    builder.Configuration["Jwt:SigningKey"] = File.ReadAllText(keyPath).Trim();
}
if (Encoding.UTF8.GetByteCount(builder.Configuration["Jwt:SigningKey"]!) < 32) throw new InvalidOperationException("JWT SigningKey must contain at least 32 bytes");
builder.Services.AddDbContext<HelperDb>(options =>
{
    string connection = builder.Configuration.GetConnectionString("Database")!;
    if (builder.Configuration["Database:Provider"] == "Postgres")
    {
        string? passwordFile = builder.Configuration["Database:PasswordFile"];
        if (!string.IsNullOrWhiteSpace(passwordFile))
        {
            var connectionBuilder = new Npgsql.NpgsqlConnectionStringBuilder(connection)
            { Password = File.ReadAllText(passwordFile).TrimEnd('\r', '\n') };
            connection = connectionBuilder.ConnectionString;
        }
        options.UseNpgsql(connection);
    }
    else options.UseSqlite(connection);
});
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:SigningKey"]!)),
            ValidateIssuer = true, ValidIssuer = builder.Configuration["Jwt:Issuer"], ValidateAudience = true, ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(10)
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (context.Request.Path.StartsWithSegments("/helperHub")) context.Token = context.Request.Query["access_token"];
                return Task.CompletedTask;
            },
            OnTokenValidated = async context =>
            {
                var db = context.HttpContext.RequestServices.GetRequiredService<HelperDb>();
                string? name = context.Principal!.Identity!.Name;
                var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Username == name);
                if (user == null || !user.IsActive || !context.Principal.HasClaim("version", user.SecurityVersion.ToString())) context.Fail("Account changed; sign in again");
            }
        };
    }).AddScheme<AuthenticationSchemeOptions, AgentAuthenticationHandler>("Agent", _ => { });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Staff", p => p.AddAuthenticationSchemes("Bearer").RequireAuthenticatedUser().RequireRole(Roles.SuperAdmin, Roles.Admin, Roles.Operator));
    options.AddPolicy("Manage", p => p.AddAuthenticationSchemes("Bearer").RequireAuthenticatedUser().RequireRole(Roles.SuperAdmin, Roles.Admin));
    options.AddPolicy("Super", p => p.AddAuthenticationSchemes("Bearer").RequireAuthenticatedUser().RequireRole(Roles.SuperAdmin));
    options.AddPolicy("Panel", p => p.AddAuthenticationSchemes("Bearer").RequireAuthenticatedUser().RequireRole(Roles.All));
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 12, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddSignalR(options => { options.MaximumReceiveMessageSize = 1_500_000; options.EnableDetailedErrors = false; });
builder.Services.AddScoped<CommandService>();
builder.Services.AddSingleton<PanelSessions>();
builder.Services.AddSingleton<GlpiSettingsStore>();
builder.Services.AddHostedService<TaskExpiryService>();
builder.Services.AddHttpClient<GlpiService>(http => { http.Timeout = TimeSpan.FromSeconds(20); http.MaxResponseContentBufferSize = 2_097_152; });
var app = builder.Build();
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<HelperDb>();
    await db.Database.EnsureCreatedAsync();
    await db.Computers.ExecuteUpdateAsync(s => s.SetProperty(c => c.IsOnline, false).SetProperty(c => c.ConnectionId, (string?)null));
    if (!await db.Users.AnyAsync())
        db.Users.Add(new PanelUser { Username = "admin", FullName = "Супер администратор", Role = Roles.SuperAdmin,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin123", 12), MustChangePassword = true });
    if (!await db.Buttons.AnyAsync()) db.Buttons.AddRange(
        new ActionButton { Title = "Поиск файлов и папок", ActionType = "open_url", Payload = "search-ms:", OrderIndex = 0 },
        new ActionButton { Title = "Открыть DMED", Payload = "https://krg.dmed.kz", OrderIndex = 1 },
        new ActionButton { Title = "Открыть EISZ", Payload = "https://www.eisz.kz", OrderIndex = 2 },
        new ActionButton { Title = "Написать программистам", ActionType = "ticket", OrderIndex = 3 });
    await db.SaveChangesAsync();
}
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "same-origin";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; connect-src 'self' ws: wss:; img-src 'self' data:; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    try { await next(); }
    catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException or InvalidOperationException or HttpRequestException)
    {
        if (context.Response.HasStarted) throw;
        context.Response.StatusCode = ex is UnauthorizedAccessException ? 403 : ex is ArgumentException ? 400 : 502;
        await context.Response.WriteAsJsonAsync(new { error = ex is HttpRequestException ? "Внешний сервис недоступен" : ex.Message });
    }
});
app.UseDefaultFiles(); app.UseStaticFiles(); app.UseRouting(); app.UseRateLimiter(); app.UseAuthentication(); app.UseAuthorization();
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true && !context.User.IsInRole("Agent") &&
        context.Request.Path != "/api/auth/me" && context.Request.Path != "/api/auth/change-password")
    {
        var db = context.RequestServices.GetRequiredService<HelperDb>();
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Username == context.User.Identity.Name);
        if (user?.MustChangePassword == true)
        { context.Response.StatusCode = 403; await context.Response.WriteAsJsonAsync(new { error = "Сначала смените начальный пароль", code = "password_change_required" }); return; }
    }
    await next();
});
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapHub<HelperHub>("/helperHub", options => options.CloseOnAuthenticationExpiration = true);
app.MapPanelApi();
await app.RunAsync();
public partial class Program { }
