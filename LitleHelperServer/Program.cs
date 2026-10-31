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
using System.Text.Json;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
    // Kestrel is exposed only on the Docker bridge; trust forwarded headers only from nginx there.
    options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(System.Net.IPAddress.Parse("172.18.0.0"), 16));
});
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
                if (context.Request.Path.StartsWithSegments("/helperHub") || context.Request.Path.StartsWithSegments("/messengerHub"))
                {
                    if (context.Request.Query.TryGetValue("access_token", out var hubToken) && !string.IsNullOrEmpty(hubToken))
                    {
                        context.Token = hubToken;
                    }
                }
                else if (string.IsNullOrEmpty(context.Token))
                {
                    if (context.Request.Query.TryGetValue("access_token", out var queryToken) ||
                        context.Request.Query.TryGetValue("token", out queryToken))
                    {
                        context.Token = queryToken;
                    }
                }
                return Task.CompletedTask;
            },
            OnTokenValidated = async context =>
            {
                var db = context.HttpContext.RequestServices.GetRequiredService<HelperDb>();
                string? name = context.Principal!.Identity!.Name;
                var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Username == name);
                if (user == null || !user.IsActive || !context.Principal.HasClaim("version", user.SecurityVersion.ToString())) context.Fail("Account changed; sign in again");
                if (user?.AuthSource == "AD" && !context.HttpContext.RequestServices.GetRequiredService<IntegrationSettings>().Ad().Enabled) context.Fail("AD sign-in disabled");
            }
        };
    }).AddScheme<AuthenticationSchemeOptions, AgentAuthenticationHandler>("Agent", _ => { });
builder.Services.AddAuthorization(options =>
{
    foreach (string permission in Access.Catalog.Keys)
    {
        string key = permission;
        options.AddPolicy(key, p => p.AddAuthenticationSchemes("Bearer").RequireAuthenticatedUser().RequireAssertion(c => Access.Can(c.User, key)));
    }
    options.AddPolicy("Staff", p => p.AddAuthenticationSchemes("Bearer").RequireAuthenticatedUser().RequireRole(Roles.SuperAdmin, Roles.Admin, Roles.Operator));
    options.AddPolicy("Manage", p => p.AddAuthenticationSchemes("Bearer").RequireAuthenticatedUser().RequireRole(Roles.SuperAdmin, Roles.Admin));
    options.AddPolicy("Super", p => p.AddAuthenticationSchemes("Bearer").RequireAuthenticatedUser().RequireRole(Roles.SuperAdmin));
    options.AddPolicy("Panel", p => p.AddAuthenticationSchemes("Bearer").RequireAuthenticatedUser().RequireRole(Roles.All));
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("glpi-portal", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 600, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("registration", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 12, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("resolve-client", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 300, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddSignalR(options => { options.MaximumReceiveMessageSize = 32 * 1024 * 1024; options.EnableDetailedErrors = false; });
builder.Services.AddScoped<CommandService>();
builder.Services.AddScoped<AnnouncementService>();
builder.Services.AddSingleton<PanelSessions>();
builder.Services.AddSingleton<ChatPresence>();
builder.Services.AddSingleton<ChatGroupGate>();
builder.Services.AddSingleton<CallSessionRegistry>();
builder.Services.AddSingleton<LiveKitService>();
builder.Services.AddSingleton<ReportOrganizationStore>();
builder.Services.AddSingleton<IMessengerKerberos, MessengerKerberos>();
builder.Services.AddScoped<IMessengerWindowsDirectory, MessengerWindowsDirectory>();
builder.Services.AddScoped<IAgentMachineDirectory, AgentMachineDirectory>();
builder.Services.AddSingleton<MessengerSettings>();
builder.Services.AddHostedService<ChatRetentionWorker>();
builder.Services.AddSingleton<GlpiSettingsStore>();
builder.Services.AddSingleton<IntegrationSettings>();
builder.Services.AddAi();
builder.Services.AddSingleton<ClientReleases>();
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options => options.MultipartBodyLengthLimit = PixelHelper.Updates.ClientUpdateManifest.MaximumSize + 65536);
builder.Services.AddScoped<IAdAuthentication, AdAuthentication>();
builder.Services.AddScoped<AdPasswordService>();
builder.Services.AddScoped<ITelegramDirectory, TelegramDirectory>();
builder.Services.AddSingleton<TicketManagementGate>();
builder.Services.AddSingleton<TelegramBotHealth>();
builder.Services.AddScoped<TicketManagement>();
builder.Services.AddScoped<TelegramBotHandler>();
builder.Services.AddScoped<TelegramBotInbox>();
builder.Services.AddHttpClient<TelegramClient>(http => { http.Timeout = TimeSpan.FromSeconds(15); http.MaxResponseContentBufferSize = 65536; }).RemoveAllLoggers();
builder.Services.AddHostedService<TelegramWorker>();
builder.Services.AddHostedService<TelegramBotWorker>();
builder.Services.AddHostedService<TicketSyncWorker>();
builder.Services.AddSingleton<GlpiImportHealth>();
builder.Services.AddScoped<GlpiTicketImport>();
builder.Services.AddHostedService<GlpiImportWorker>();
builder.Services.AddHostedService<TaskExpiryService>();
builder.Services.AddHttpClient();
builder.Services.AddHttpClient("EmergencyBridge").ConfigurePrimaryHttpMessageHandler(()=>new HttpClientHandler {AllowAutoRedirect=false});
builder.Services.AddHostedService<EmergencySyncWorker>();
builder.Services.AddHttpClient<GlpiService>(http => { http.Timeout = TimeSpan.FromSeconds(20); http.MaxResponseContentBufferSize = 2_097_152; });
builder.Services.AddSingleton<ReportGate>();
builder.Services.AddSingleton<ReportHealth>();
builder.Services.AddScoped<GlpiReports>();
builder.Services.AddScoped<PxeService>();
builder.Services.AddScoped<TicketSurveys>();
builder.Services.AddScoped<SurveyAlerts>();
builder.Services.AddHostedService<GlpiReportWorker>();
builder.Services.AddSingleton<AntivirusSyncWorker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<AntivirusSyncWorker>());
var app = builder.Build();
app.MapGlpiReports();
app.MapGlpiReportPortal();
app.MapAgentUserSessions();

if (builder.Configuration.GetValue<bool>("ReverseProxy:Enabled")) app.UseForwardedHeaders();
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<HelperDb>();
    await db.Database.EnsureCreatedAsync();
    await AiSchema.EnsureAsync(db);
    await db.AiMessages.Where(m => m.Status == "Running").ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, "Interrupted"));
    await db.AiDiagnosticChecks.Where(m => m.Status == "Running").ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, "Interrupted"));
    await db.AiRequestLogs.Where(m => m.Status == "Running" || m.Status == "Creating").ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, "Interrupted"));
    await db.AiSessions.Where(s => (s.Status == "Active" || s.Status == "Creating") && s.Backend == "OpenCode" && s.ExternalSessionId != "").ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, "Closing"));
    await db.AiSessions.Where(s => s.Status == "Active" || s.Status == "Creating").ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, "Closed"));
    await Access.EnsureSchema(db);
    await GlpiReports.EnsureSchema(db);
    await SurveyAlerts.EnsureSchema(db);
    await EmergencyEvents.EnsureAsync(db);
    await AntiTheftSchema.EnsureAsync(db);
    await ChatBroadcasts.Column(db,"Computers","AdMachineObjectId","TEXT NOT NULL DEFAULT ''");
    await ChatBroadcasts.Column(db,"Computers","ClientVersion","TEXT NOT NULL DEFAULT ''");
    await ChatBroadcasts.Column(db,"Buttons","Description","TEXT NOT NULL DEFAULT ''");
    await ChatBroadcasts.Column(db,"Buttons","TitleTranslationsJson","TEXT NOT NULL DEFAULT ''");
    await ChatBroadcasts.Column(db,"Buttons","DescriptionTranslationsJson","TEXT NOT NULL DEFAULT ''");
    await SystemButtons.EnsureAsync(db);
    await MedicalStatuses.EnsureAsync(db);
    await Messenger.EnsureSchemaAsync(db);
    await ChatGroups.EnsureSchemaAsync(db);
    await AnnouncementService.EnsureSchemaAsync(db);
    await TicketManagement.EnsureSchemaAsync(db);
    await TicketAttachments.EnsureSchemaAsync(db);
    await Branches.EnsureSchemaAsync(db);
    await KnowledgeBase.EnsureSchemaAsync(db);
    await ChatPolls.EnsureSchemaAsync(db);
    // Existing deployments use EnsureCreated; add the outbox without changing existing tables.
    string timestamp = db.Database.IsNpgsql() ? "timestamp with time zone" : "TEXT";
    string outboxSql = "CREATE TABLE IF NOT EXISTS \"TelegramDeliveries\" (\"TicketId\" INTEGER PRIMARY KEY REFERENCES \"Tickets\"(\"Id\") ON DELETE CASCADE, \"Attempts\" INTEGER NOT NULL, \"State\" TEXT NOT NULL, \"NextAttemptAt\" " + timestamp + " NOT NULL, \"SentAt\" " + timestamp + " NULL, \"LastError\" TEXT NOT NULL)";
    await db.Database.ExecuteSqlRawAsync(outboxSql);
    await db.Computers.ExecuteUpdateAsync(s => s.SetProperty(c => c.IsOnline, false).SetProperty(c => c.ConnectionId, (string?)null));
    if (!await db.Users.AnyAsync())
        db.Users.Add(new PanelUser { Username = "admin", FullName = "Супер администратор", Role = Roles.SuperAdmin,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin123", 12), MustChangePassword = true });
    if (!await db.Buttons.AnyAsync(b => !b.IsSystem)) db.Buttons.AddRange(
        new ActionButton { Title = "Поиск файлов и папок", ActionType = "open_url", Payload = "search-ms:", OrderIndex = 0 },
        new ActionButton { Title = "Открыть DMED", Payload = "https://krg.dmed.kz", OrderIndex = 1 },
        new ActionButton { Title = "Открыть EISZ", Payload = "https://www.eisz.kz", OrderIndex = 2 },
        new ActionButton { Title = "Написать программистам", ActionType = "ticket", OrderIndex = 3 });
    if (!await db.SuperAdminButtons.AnyAsync()) db.SuperAdminButtons.Add(new() { Title = "Сообщение клиентам", Target = "select" });
    await db.SaveChangesAsync();
}
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "same-origin";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; connect-src 'self' ws: wss: http://127.0.0.1:* http://localhost:*; img-src 'self' data:; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    try { await next(); }
    catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException or InvalidOperationException or HttpRequestException or JsonException)
    {
        if (context.Response.HasStarted) throw;
        context.Response.StatusCode = ex is UnauthorizedAccessException ? 403 : ex is ArgumentException or JsonException ? 400 : 502;
        await context.Response.WriteAsJsonAsync(new { error = ex is HttpRequestException ? "Внешний сервис недоступен" : ex.Message });
    }
});
app.UseDefaultFiles(); app.UseStaticFiles(); app.UseRouting(); app.UseRateLimiter(); app.UseAuthentication(); app.UseAuthorization();
app.Use(async (context, next) =>
{
    bool companionAi = context.Request.Path == "/api/ai/capabilities" || context.Request.Path == "/api/ai/diagnostics/options" || context.Request.Path == "/api/ai/credentials" || context.Request.Path.StartsWithSegments("/api/ai/knowledge") || context.Request.Path.StartsWithSegments("/api/ai/sessions");
    if (context.User.HasClaim("messenger", "1") && !companionAi && !context.Request.Path.StartsWithSegments("/api/messenger") && !context.Request.Path.StartsWithSegments("/messengerHub"))
    { context.Response.StatusCode = 403; return; }
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
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));
app.MapHub<HelperHub>("/helperHub", options => options.CloseOnAuthenticationExpiration = true);
app.MapPanelApi();
app.MapStaffTicketsApi();
app.MapUserTicketsApi();
app.MapPxeApi();
app.MapMailBackupApi();
app.MapMedicalStatuses();
app.MapEmergencyRecipients();
app.MapEmergencyDirectorySync();
AgentMachineEnrollment.Map(app);
app.MapIntegrationApi();
app.MapAnnouncementApi();
app.MapBranchApi();
app.MapKnowledgeBaseApi();
app.MapAi();
app.MapMessenger();
await app.RunAsync();
public partial class Program { }
