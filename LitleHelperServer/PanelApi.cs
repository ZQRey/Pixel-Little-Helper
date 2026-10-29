using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.IO.Compression;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LitleHelperServer;

public static class PanelApi
{
    public static void MapPanelApi(this WebApplication app)
    {
        app.MapPost("/api/agents/register", async (AgentRegistrationRequest request, HttpContext context, HelperDb db, IConfiguration config, IntegrationSettings settings) =>
        {
            if (string.IsNullOrWhiteSpace(request.MachineName) || string.IsNullOrWhiteSpace(request.ClientKey)) return Results.BadRequest();
            string machine = Security.Canonical(request.MachineName);
            if (!Security.MachineValid(machine) || request.ClientKey.Length is < 32 or > 200 ||
                request.ClientKey.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('+' or '/' or '=' or '-' or '_')))
                return Results.BadRequest(new { error = "Неверные данные регистрации клиента." });
            string hash = Security.KeyHash(request.ClientKey);
            var computer = await db.Computers.SingleOrDefaultAsync(c => c.MachineName == machine);
            bool firstRegistration = computer == null || string.IsNullOrEmpty(computer.AgentKeyHash);
            var callerAddress = NetworkAccess.CallerAddress(context);
            if (firstRegistration && !config.GetValue<bool>("Registration:AllowPublic") &&
                !NetworkAccess.IsAllowed(callerAddress, settings.Registration().AllowedNetworks))
                return Results.Json(new { error = "Регистрация нового клиента разрешена только из разрешённых сетей." }, statusCode: 403);
            if (computer == null)
            {
                computer = new Computer { MachineName = machine, AgentKeyHash = hash };
                db.Computers.Add(computer);
                try { await db.SaveChangesAsync(); }
                catch (DbUpdateException)
                {
                    db.Entry(computer).State = EntityState.Detached;
                    computer = await db.Computers.SingleOrDefaultAsync(c => c.MachineName == machine);
                    if (computer == null) throw;
                }
            }
            if (!CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(computer.AgentKeyHash), System.Text.Encoding.ASCII.GetBytes(hash)))
                return Results.Conflict(new { error = "Компьютер уже зарегистрирован с другим ключом. Обратитесь к администратору." });
            return Results.Ok(new { registered = true });
        }).RequireRateLimiting("registration");
        app.MapGet("/api/settings/glpi", (GlpiSettingsStore settings) => Results.Ok(settings.View())).RequireAuthorization("settings.manage");
        app.MapPut("/api/settings/glpi", (GlpiSettingsUpdate update, GlpiSettingsStore settings) => { settings.Save(update); return Results.Ok(settings.View()); }).RequireAuthorization("settings.manage");
        app.MapPost("/api/settings/glpi/test", async (GlpiService glpi, CancellationToken token) =>
        {
            try { await glpi.TestConnectionAsync(token); return Results.Ok(new { success = true, message = "GLPI API: авторизация успешна. Тестовая заявка не создавалась." }); }
            catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or TaskCanceledException)
            { return Results.Ok(new { success = false, message = ex is TaskCanceledException ? "GLPI не ответил за 20 секунд. Проверьте сеть и адрес." : ex is HttpRequestException ? "Сервер помощника не смог подключиться к GLPI. Проверьте адрес, DNS, TLS и сеть." : ex.Message }); }
        }).RequireAuthorization("settings.manage");
        app.MapGet("/api/settings/registration", (IntegrationSettings settings) => Results.Ok(settings.RegistrationView())).RequireAuthorization("settings.manage");
        app.MapPut("/api/settings/registration", (RegistrationOptions request, IntegrationSettings settings) =>
        {
            try { settings.SaveRegistration(request); return Results.Ok(settings.RegistrationView()); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        }).RequireAuthorization("settings.manage");
        app.MapPost("/api/auth/login", async (LoginRequest request, HelperDb db, IConfiguration config, IAdAuthentication ad, IntegrationSettings integrationSettings, CancellationToken token) =>
        {
            if (request.Username.Length > 100 || request.Password.Length > 256) return Results.Unauthorized();
            string login = Security.Login(request.Username);

            if (request.Provider == "ad")
            {
                return await AuthenticateAdAsync(request, ad, db, config, token);
            }
            if (request.Provider == "local")
            {
                return await AuthenticateLocalAsync(request, login, db, config);
            }

            // Auto-detection:
            var localUser = await db.Users.SingleOrDefaultAsync(u => u.Username == login, token);
            if (localUser != null && localUser.AuthSource != "AD")
            {
                if (localUser.IsActive && request.Password.Length <= 72 && BCrypt.Net.BCrypt.Verify(request.Password, localUser.PasswordHash))
                {
                    localUser.IsSweetheart = Access.IsSweetheart(config, localUser.Username);
                    return Results.Ok(new { token = Security.Token(localUser, config), user = localUser });
                }
                return Results.Unauthorized();
            }

            if (integrationSettings.Ad().Enabled)
            {
                return await AuthenticateAdAsync(request, ad, db, config, token);
            }

            return Results.Unauthorized();
        }).RequireRateLimiting("login");
        app.MapGet("/api/auth/me", async (ClaimsPrincipal p, HelperDb db, IConfiguration config) => { var u = await db.Users.SingleAsync(x => x.Username == p.Identity!.Name); u.IsSweetheart = Access.IsSweetheart(config, u.Username); return Results.Ok(u); }).RequireAuthorization("Panel");
        app.MapPost("/api/auth/change-password", async (PasswordRequest request, ClaimsPrincipal p, HelperDb db, IConfiguration config, PanelSessions sessions) =>
        {
            var user = await db.Users.SingleAsync(u => u.Username == p.Identity!.Name);
            if (user.AuthSource == "AD") return Results.BadRequest(new { error = "Пароль AD изменяется в домене Windows." });
            if (!Security.PasswordValid(request.NewPassword) || request.NewPassword == request.CurrentPassword || !BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
                return Results.BadRequest(new { error = "Проверьте текущий пароль. Новый пароль: 8–72 символа, до 72 байт UTF-8." });
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword, 12); user.MustChangePassword = false; user.SecurityVersion++;
            await db.SaveChangesAsync(); sessions.Revoke(user.Id); user.IsSweetheart = Access.IsSweetheart(config, user.Username);
            return Results.Ok(new { token = Security.Token(user, config), user });
        }).RequireAuthorization("Panel");

        app.MapGet("/api/computers", async (ClaimsPrincipal p, HelperDb db) =>
        {
            var computers = await db.Computers.AsNoTracking().OrderBy(c => c.MachineName).ToListAsync();
            // Hardware/software and transport identifiers never appear in the status table.
            return Results.Ok(computers.Select(HelperHub.Status));
        }).RequireAuthorization("computers.view");
        app.MapPost("/api/computers/enroll", async (EnrollmentRequest request, HelperDb db) =>
        {
            string name = Security.Canonical(request.MachineName);
            if (!Security.MachineValid(name)) return Results.BadRequest(new { error = "Неверное имя ПК" });
            var computer = await db.Computers.SingleOrDefaultAsync(c => c.MachineName == name);
            if (computer?.IsOnline == true) return Results.Conflict(new { error = "Сначала отключите помощника для смены ключа" });
            if (computer == null) { computer = new Computer { MachineName = name }; db.Computers.Add(computer); }
            string key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)); computer.AgentKeyHash = Security.KeyHash(key);
            await db.SaveChangesAsync(); return Results.Ok(new { machineName = name, clientToken = key });
        }).RequireAuthorization("computers.manage");
        app.MapGet("/api/computers/{id:int}/inventory", async (int id, HelperDb db) =>
        {
            var c = await db.Computers.FindAsync(id);
            return c == null ? Results.NotFound() : Results.Ok(new { c.MachineName, c.IpAddress, c.OsVersion, c.LastSeen,
                hardware = JsonSerializer.Deserialize<JsonElement>(c.HardwareJson), software = JsonSerializer.Deserialize<JsonElement>(c.SoftwareJson) });
        }).RequireAuthorization("inventory.view");
        app.MapDelete("/api/computers/{id:int}", async (int id, HelperDb db) =>
        {
            var c = await db.Computers.FindAsync(id); if (c == null) return Results.NotFound();
            if (c.IsOnline) return Results.Conflict(new { error = "Отключите ПК перед удалением регистрации" });
            db.Computers.Remove(c); await db.SaveChangesAsync(); return Results.NoContent();
        }).RequireAuthorization("computers.delete");
        app.MapPut("/api/computers/{id:int}", async (int id, UpdateComputerLocationRequest request, HelperDb db, Microsoft.AspNetCore.SignalR.IHubContext<HelperHub> hub) =>
        {
            var c = await db.Computers.FindAsync(id);
            if (c == null) return Results.NotFound();

            string newRoom = (request.Room ?? "").Trim();
            if (newRoom.Length > 50 || newRoom.Any(char.IsControl))
                return Results.BadRequest(new { error = "Кабинет: до 50 символов без управляющих символов." });

            if (request.BranchId.HasValue && !await db.Branches.AnyAsync(b => b.Id == request.BranchId.Value && b.IsActive))
                return Results.BadRequest(new { error = "Выбранный филиал не существует." });

            int? previousBranch = c.BranchId;
            c.Room = newRoom;
            c.BranchId = request.BranchId;

            if (!string.IsNullOrWhiteSpace(c.CurrentUser))
            {
                string ticketUser = Security.TicketUser(c.CurrentUser).ToLowerInvariant();
                var u = await db.Users.FirstOrDefaultAsync(user => user.Username.ToLower() == ticketUser || user.Username.ToLower() == c.CurrentUser.ToLower());
                if (u != null && u.Role != Roles.SuperAdmin && u.Role != Roles.Admin)
                {
                    u.BranchId = request.BranchId;
                    u.Room = newRoom;
                }
            }

            await db.SaveChangesAsync();

            EmergencyDirectorySync.Request();
            if (c.IsOnline && c.ConnectionId != null)
            {
                if (previousBranch.HasValue) await hub.Groups.RemoveFromGroupAsync(c.ConnectionId, "BranchAgents:" + previousBranch);
                if (c.BranchId.HasValue) await hub.Groups.AddToGroupAsync(c.ConnectionId, "BranchAgents:" + c.BranchId);
                await hub.Clients.Group("AgentMachine:" + c.MachineName.ToLowerInvariant()).SendAsync("ClientRoomUpdated", c.Room);
            }
            await hub.Clients.Group("PanelStaff").SendAsync("ComputerChanged", HelperHub.Status(c));

            return Results.Ok(HelperHub.Status(c));
        }).RequireAuthorization("computers.manage");

        app.MapPost("/api/telemetry/anti-theft", async (AntiTheftTelemetryRequest request, HttpContext context, HelperDb db, Microsoft.AspNetCore.SignalR.IHubContext<HelperHub> hub) =>
        {
            string name = Security.Canonical(request.MachineName);
            if (!Security.MachineValid(name)) return Results.BadRequest(new { error = "Неверное имя ПК" });
            var c = await db.Computers.SingleOrDefaultAsync(comp => comp.MachineName == name);
            if (c == null)
            {
                c = new Computer { MachineName = name };
                db.Computers.Add(c);
            }
            string externalIp = context.Connection.RemoteIpAddress?.ToString() ?? "";
            c.IsPortable = request.IsPortable;
            c.BatteryPercent = request.BatteryPercent;
            c.IsCharging = request.IsCharging;
            c.LastSsid = request.ConnectedSsid ?? "";
            c.LastBssid = request.ConnectedBssid ?? "";
            c.HardwareUuid = request.HardwareUuid ?? "";
            c.ClientVersion = !string.IsNullOrWhiteSpace(request.ClientVersion) ? request.ClientVersion : c.ClientVersion;
            c.LastSeen = DateTime.UtcNow;

            var log = new LaptopMovementLog
            {
                ComputerId = c.Id,
                MachineName = c.MachineName,
                Timestamp = DateTime.UtcNow,
                ExternalIp = externalIp,
                Ssid = request.ConnectedSsid ?? "",
                Bssid = request.ConnectedBssid ?? "",
                VisibleNetworksJson = request.VisibleNetworks != null ? JsonSerializer.Serialize(request.VisibleNetworks) : "[]",
                EstimatedAddress = ""
            };
            db.LaptopMovementLogs.Add(log);
            await db.SaveChangesAsync();
            await hub.Clients.Group("PanelStaff").SendAsync("ComputerChanged", HelperHub.Status(c));

            return Results.Ok(new AntiTheftTelemetryResponse(c.IsLockedDown, c.LockMessage, c.BitLockerRecoveryTriggered || c.LockLevelBitLocker, c.IsLockedDown, c.LockLevelScreen, c.LockLevelSession));
        });

        app.MapPost("/api/computers/{id:int}/lockdown", async (int id, LockdownRequest request, ClaimsPrincipal p, HelperDb db, Microsoft.AspNetCore.SignalR.IHubContext<HelperHub> hub) =>
        {
            var c = await db.Computers.FindAsync(id);
            if (c == null) return Results.NotFound();
            c.IsLockedDown = request.Enable;
            c.LockMessage = request.Message ?? "";
            c.LockLevelScreen = request.Enable ? request.LevelScreen : false;
            c.LockLevelSession = request.Enable ? request.LevelSession : false;
            c.LockLevelBitLocker = request.Enable ? request.LevelBitLocker : false;
            if (request.Enable && request.LevelBitLocker)
            {
                c.BitLockerRecoveryTriggered = true;
            }
            string levelsSummary = request.Enable 
                ? $"Уровни: {(request.LevelScreen ? "[1:Экран] " : "")}{(request.LevelSession ? "[2:Сеанс] " : "")}{(request.LevelBitLocker ? "[3:BitLocker] " : "")}"
                : "Все уровни блокировки отключены";
            db.AuditLogs.Add(new() {
                AdminUsername = p.Identity!.Name ?? "",
                MachineName = c.MachineName,
                CommandType = request.Enable ? "anti_theft_lockdown_enable" : "anti_theft_lockdown_disable",
                CommandPayload = levelsSummary + " | " + (request.Message ?? ""),
                Status = "Completed",
                Result = request.Enable ? "Блокировка устройства активирована: " + levelsSummary : "Блокировка устройства снята"
            });
            await db.SaveChangesAsync();
            await hub.Clients.Group("PanelStaff").SendAsync("ComputerChanged", HelperHub.Status(c));
            if (c.ConnectionId != null)
            {
                await hub.Clients.Client(c.ConnectionId).SendAsync("AntiTheftLockdownChanged", c.IsLockedDown, c.LockMessage, c.LockLevelScreen, c.LockLevelSession, c.LockLevelBitLocker);
            }
            return Results.Ok(HelperHub.Status(c));
        }).RequireAuthorization("computers.manage");

        app.MapPost("/api/computers/{id:int}/bitlocker-recovery", async (int id, ClaimsPrincipal p, HelperDb db, Microsoft.AspNetCore.SignalR.IHubContext<HelperHub> hub) =>
        {
            var c = await db.Computers.FindAsync(id);
            if (c == null) return Results.NotFound();
            c.BitLockerRecoveryTriggered = true;
            db.AuditLogs.Add(new() {
                AdminUsername = p.Identity!.Name ?? "",
                MachineName = c.MachineName,
                CommandType = "anti_theft_bitlocker_recovery",
                CommandPayload = "Force BitLocker Recovery C:",
                Status = "Completed",
                Result = "Запрос на принудительную блокировку BitLocker отправлен"
            });
            await db.SaveChangesAsync();
            await hub.Clients.Group("PanelStaff").SendAsync("ComputerChanged", HelperHub.Status(c));
            if (c.ConnectionId != null)
            {
                await hub.Clients.Client(c.ConnectionId).SendAsync("AntiTheftBitLockerTriggered");
            }
            return Results.Ok(new { success = true });
        }).RequireAuthorization("computers.manage");

        app.MapPost("/api/computers/{id:int}/portable", async (int id, ClaimsPrincipal p, HelperDb db, Microsoft.AspNetCore.SignalR.IHubContext<HelperHub> hub) =>
        {
            var c = await db.Computers.FindAsync(id);
            if (c == null) return Results.NotFound();
            c.IsPortable = !c.IsPortable;
            db.AuditLogs.Add(new() {
                AdminUsername = p.Identity!.Name ?? "",
                MachineName = c.MachineName,
                CommandType = "anti_theft_toggle_portable",
                CommandPayload = "IsPortable=" + c.IsPortable,
                Status = "Completed",
                Result = c.IsPortable ? "Отмечен как переносное устройство (ноутбук)" : "Отмечен как стационарный ПК"
            });
            await db.SaveChangesAsync();
            await hub.Clients.Group("PanelStaff").SendAsync("ComputerChanged", HelperHub.Status(c));
            return Results.Ok(HelperHub.Status(c));
        }).RequireAuthorization("computers.manage");

        app.MapGet("/api/computers/{id:int}/telemetry-history", async (int id, HelperDb db) =>
        {
            var logs = await db.LaptopMovementLogs.AsNoTracking()
                .Where(l => l.ComputerId == id)
                .OrderByDescending(l => l.Timestamp)
                .Take(50)
                .ToListAsync();
            return Results.Ok(logs);
        }).RequireAuthorization("computers.view");

        // Mobile Devices (Pixel Anti-Theft Android) endpoints
        app.MapGet("/api/mobile-devices", async (HelperDb db) =>
        {
            var list = await db.MobileDevices.AsNoTracking().OrderBy(m => m.BrandModel).ThenBy(m => m.AssignedUserFullName).ToListAsync();
            return Results.Ok(list);
        }).RequireAuthorization("computers.view");

        app.MapPost("/api/mobile-devices/register", async (MobileRegisterRequest req, HelperDb db) =>
        {
            if (string.IsNullOrWhiteSpace(req.DeviceId)) return Results.BadRequest(new { error = "DeviceId обязателен" });
            var device = await db.MobileDevices.SingleOrDefaultAsync(m => m.DeviceId == req.DeviceId);
            if (device == null)
            {
                device = new MobileDevice { DeviceId = req.DeviceId };
                db.MobileDevices.Add(device);
            }
            device.BrandModel = req.BrandModel;
            device.SerialNumber = req.SerialNumber;
            device.Imei = req.Imei;
            device.AndroidVersion = req.AndroidVersion;
            device.ClientVersion = req.ClientVersion;
            if (!string.IsNullOrWhiteSpace(req.AssignedUsername)) device.AssignedUsername = req.AssignedUsername;
            if (!string.IsNullOrWhiteSpace(req.AssignedUserFullName)) device.AssignedUserFullName = req.AssignedUserFullName;
            if (req.BranchId.HasValue) device.BranchId = req.BranchId;
            if (!string.IsNullOrWhiteSpace(req.Department)) device.Department = req.Department;
            if (!string.IsNullOrWhiteSpace(req.PhoneNumber)) device.PhoneNumber = req.PhoneNumber;
            if (!string.IsNullOrWhiteSpace(req.SimIccid)) device.SimIccid = req.SimIccid;
            if (!string.IsNullOrWhiteSpace(req.WireGuardPublicKey)) device.WireGuardPublicKey = req.WireGuardPublicKey;
            device.LastSeen = DateTime.UtcNow;
            device.IsOnline = true;
            await db.SaveChangesAsync();
            return Results.Ok(device);
        });

        app.MapPost("/api/mobile-devices/{id:int}/telemetry", async (int id, MobileTelemetryRequest req, HelperDb db, HttpContext http) =>
        {
            var device = await db.MobileDevices.FindAsync(id);
            if (device == null) return Results.NotFound();
            device.BatteryPercent = req.BatteryPercent;
            device.IsCharging = req.IsCharging;
            device.CurrentWifiSsid = req.CurrentWifiSsid;
            device.CurrentWifiBssid = req.CurrentWifiBssid;
            if (req.Latitude.HasValue && req.Longitude.HasValue)
            {
                device.Latitude = req.Latitude;
                device.Longitude = req.Longitude;
                device.LocationAccuracy = req.LocationAccuracy;
                device.LastLocationTime = DateTime.UtcNow;
                if (!string.IsNullOrWhiteSpace(req.LocationAddress)) device.LocationAddress = req.LocationAddress;
            }
            device.CurrentIp = http.Connection.RemoteIpAddress?.ToString() ?? "";
            device.LastSeen = DateTime.UtcNow;
            device.IsOnline = true;
            await db.SaveChangesAsync();
            return Results.Ok(new {
                device.IsLockedDown,
                device.LockLevelScreen,
                device.LockLevelPin,
                device.LockMessage,
                device.TemporaryLockPin,
                device.IsSirenActive
            });
        });

        app.MapPost("/api/mobile-devices/{id:int}/lockdown", async (int id, MobileLockdownRequest req, ClaimsPrincipal p, HelperDb db) =>
        {
            var device = await db.MobileDevices.FindAsync(id);
            if (device == null) return Results.NotFound();
            device.IsLockedDown = req.Enable;
            device.LockMessage = req.Message;
            device.LockLevelScreen = req.LevelScreen;
            device.LockLevelPin = req.LevelPin;
            device.TemporaryLockPin = req.TemporaryPin;
            device.IsSirenActive = req.Siren;
            db.AuditLogs.Add(new AuditLog
            {
                AdminUsername = p.Identity!.Name ?? "",
                MachineName = device.BrandModel + " (" + device.DeviceId + ")",
                CommandType = "mobile_anti_theft_lockdown",
                CommandPayload = $"Enable={req.Enable}, Screen={req.LevelScreen}, Pin={req.LevelPin}, Siren={req.Siren}",
                Status = "Completed",
                Result = req.Enable ? "Мобильное устройство переведено в режим розыска" : "Режим розыска снят"
            });
            await db.SaveChangesAsync();
            return Results.Ok(device);
        }).RequireAuthorization("computers.manage");

        app.MapDelete("/api/mobile-devices/{id:int}", async (int id, HelperDb db) =>
        {
            var device = await db.MobileDevices.FindAsync(id);
            if (device == null) return Results.NotFound();
            db.MobileDevices.Remove(device);
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).RequireAuthorization("computers.delete");

        app.MapGet("/api/mobile-devices/download-installer", async (HttpContext http, IConfiguration config) =>
        {
            var memoryStream = new MemoryStream();
            using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
            {
                string scheme = http.Request.Scheme;
                string host = http.Request.Host.Value;
                string serverUrl = $"{scheme}://{host}";
                string wgEndpoint = config["WireGuard:ServerEndpoint"] ?? "vpn.hospital.local:51820";
                string wgPublicKey = config["WireGuard:ServerPublicKey"] ?? "";

                var configObj = new
                {
                    serverUrl = serverUrl,
                    wireguardEndpoint = wgEndpoint,
                    wireguardServerPublicKey = wgPublicKey,
                    generatedUtc = DateTime.UtcNow
                };

                // 1. config.json
                var cfgEntry = archive.CreateEntry("config.json");
                using (var writer = new StreamWriter(cfgEntry.Open(), Encoding.UTF8))
                {
                    await writer.WriteAsync(JsonSerializer.Serialize(configObj, new JsonSerializerOptions { WriteIndented = true }));
                }

                // 2. setup-android.bat
                var batEntry = archive.CreateEntry("setup-android.bat");
                using (var writer = new StreamWriter(batEntry.Open(), Encoding.UTF8))
                {
                    await writer.WriteAsync(BuildSetupBat(serverUrl));
                }

                // 3. README.txt
                var readmeEntry = archive.CreateEntry("README.txt");
                using (var writer = new StreamWriter(readmeEntry.Open(), Encoding.UTF8))
                {
                    await writer.WriteAsync(BuildReadmeTxt(serverUrl));
                }

                // 4. Bundled ADB binaries if available on server machine
                string wingetAdbDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Microsoft\WinGet\Packages\Google.PlatformTools_Microsoft.Winget.Source_8wekyb3d8bbwe\platform-tools");
                if (Directory.Exists(wingetAdbDir))
                {
                    foreach (var file in new[] { "adb.exe", "AdbWinApi.dll", "AdbWinUsbApi.dll" })
                    {
                        string sourceFile = Path.Combine(wingetAdbDir, file);
                        if (File.Exists(sourceFile))
                        {
                            var entry = archive.CreateEntry(file);
                            using var fs = File.OpenRead(sourceFile);
                            using var es = entry.Open();
                            await fs.CopyToAsync(es);
                        }
                    }
                }

                // 5. Bundled PixelHelper.apk if present
                string apkPath = Path.Combine(AppContext.BaseDirectory, "dist", "android", "PixelHelper.apk");
                if (!File.Exists(apkPath))
                {
                    string contentRoot = Path.Combine(Directory.GetCurrentDirectory(), "dist", "android", "PixelHelper.apk");
                    if (File.Exists(contentRoot)) apkPath = contentRoot;
                }
                if (File.Exists(apkPath))
                {
                    var apkEntry = archive.CreateEntry("PixelHelper.apk");
                    using var fs = File.OpenRead(apkPath);
                    using var es = apkEntry.Open();
                    await fs.CopyToAsync(es);
                }
            }
            memoryStream.Seek(0, SeekOrigin.Begin);
            return Results.File(memoryStream, "application/zip", "PixelHelper-Android-Setup.zip");
        }).RequireAuthorization("computers.manage");
        app.MapGet("/api/scripts", (IConfiguration config) => Results.Ok(config.GetSection("Scripts").GetChildren().Select(s => new { id = s.Key, title = s["Title"] ?? s.Key }))).RequireAuthorization("commands.execute");
        app.MapPost("/api/commands", async (CommandRequest request, ClaimsPrincipal p, CommandService service, CancellationToken token) => Results.Ok(await service.SendAsync(p, request, token))).RequireAuthorization("commands.execute");
        app.MapGet("/api/tasks/{taskId}", async (string taskId, ClaimsPrincipal p, HelperDb db) =>
        {
            var task = await db.AuditLogs.AsNoTracking().SingleOrDefaultAsync(t => t.TaskId == taskId);
            return task == null || (!Access.Can(p, "audit.all") && task.AdminUsername != p.Identity!.Name) ? Results.NotFound() : Results.Ok(task);
        }).RequireAuthorization("commands.execute");

        app.MapGet("/api/buttons", async (HelperDb db) =>
        {
            bool added = false;
            foreach (var item in new[] { ("user_tickets", "Заявки"), ("messenger", "Мессенджер"), ("emergency", "Экстренное оповещение"), ("mail", "Почта") })
            {
                if (!await db.Buttons.AnyAsync(b => b.SystemKey == item.Item1))
                {
                    db.Buttons.Add(new() { SystemKey = item.Item1, IsSystem = true, Title = item.Item2, ActionType = item.Item1, IconName = item.Item1, OrderIndex = 10000 });
                    added = true;
                }
            }
            if (added) await db.SaveChangesAsync();
            return Results.Ok(await db.Buttons.AsNoTracking().OrderBy(b => b.OrderIndex).ThenBy(b => b.Id).ToListAsync());
        }).RequireAuthorization("buttons.manage");
        app.MapPost("/api/buttons", async (ActionButton button, ClaimsPrincipal p, HelperDb db) =>
        {
            if (button.IsSystem || button.SystemKey != null) return Results.BadRequest("Системные кнопки создаются сервером");
            ValidateButton(button, p);
            var (tTrans, dTrans) = ButtonTranslationService.Translate(button.Title, button.Description, button.TitleTranslations, button.DescriptionTranslations);
            button.TitleTranslations = tTrans;
            button.DescriptionTranslations = dTrans;
            button.Id = 0; db.Buttons.Add(button); await db.SaveChangesAsync(); return Results.Ok(button);
        }).RequireAuthorization("buttons.manage");
        app.MapPut("/api/buttons/{id:int}", async (int id, ActionButton button, ClaimsPrincipal p, HelperDb db) =>
        {
            if (button.IsSystem || button.SystemKey != null) return Results.BadRequest("Системные кнопки создаются сервером");
            var old = await db.Buttons.FindAsync(id); if (old == null) return Results.NotFound();
            if (old.IsSystem) return Results.Conflict("Системную кнопку можно только включить или выключить");
            ValidateButton(button, p);
            var (tTrans, dTrans) = ButtonTranslationService.Translate(button.Title, button.Description, button.TitleTranslations, button.DescriptionTranslations);
            old.Title = button.Title; old.ActionType = button.ActionType; old.Payload = button.Payload; old.OrderIndex = button.OrderIndex;
            old.IsActive = button.IsActive; old.IconName = button.IconName; old.TargetGroup = button.TargetGroup;
            old.Description = button.Description; old.TitleTranslations = tTrans; old.DescriptionTranslations = dTrans;
            await db.SaveChangesAsync(); return Results.Ok(old);
        }).RequireAuthorization("buttons.manage");
        app.MapDelete("/api/buttons/{id:int}", async (int id, HelperDb db) =>
        { var b = await db.Buttons.FindAsync(id); if (b == null) return Results.NotFound(); if (b.IsSystem) return Results.Conflict("Системную кнопку можно только включить или выключить"); db.Remove(b); await db.SaveChangesAsync(); return Results.NoContent(); }).RequireAuthorization("buttons.manage");
        app.MapPatch("/api/buttons/{id:int}/active", async (int id, ButtonActive request, HelperDb db, IHubContext<HelperHub> hub) =>
        {
            var button = await db.Buttons.FindAsync(id);
            if (button == null) return Results.NotFound();
            button.IsActive = request.IsActive; await db.SaveChangesAsync();
            int notified = await SystemButtons.NotifyAsync(db, hub);
            return Results.Ok(new { id, isActive = button.IsActive, notified });
        }).RequireAuthorization("buttons.manage");
        app.MapPost("/api/buttons/{id:int}/toggle", async (int id, HelperDb db, IHubContext<HelperHub> hub) =>
        {
            var b = await db.Buttons.FindAsync(id);
            if (b == null) return Results.NotFound();
            b.IsActive = !b.IsActive;
            await db.SaveChangesAsync();
            var computers = await db.Computers.AsNoTracking().Where(c => c.IsOnline && c.ConnectionId != null).ToListAsync();
            foreach (var c in computers) await hub.Clients.Client(c.ConnectionId!).SendAsync("OnButtonsUpdated", await HelperHub.Buttons(db, c));
            return Results.Ok(new { id = b.Id, isActive = b.IsActive, notified = computers.Count });
        }).RequireAuthorization("buttons.manage");
        app.MapPost("/api/buttons/apply", async (HelperDb db, IHubContext<HelperHub> hub) =>
        {
            var computers = await db.Computers.AsNoTracking().Where(c => c.IsOnline && c.ConnectionId != null).ToListAsync();
            foreach (var c in computers) await hub.Clients.Client(c.ConnectionId!).SendAsync("OnButtonsUpdated", await HelperHub.Buttons(db, c));
            return Results.Ok(new { notified = computers.Count });
        }).RequireAuthorization("buttons.manage");

        app.MapGet("/api/permissions", () => Results.Ok(new { catalog = Access.Catalog, roles = Roles.All.ToDictionary(r => r, r => Access.Defaults(r)) })).RequireAuthorization("users.manage");
        app.MapGet("/api/users", async (HelperDb db) => Results.Ok(await db.Users.AsNoTracking().OrderBy(u => u.Username).ToListAsync())).RequireAuthorization("users.manage");
        app.MapPost("/api/users", async (UserRequest request, ClaimsPrincipal p, HelperDb db, AnnouncementService announcements, CancellationToken token) =>
        {
            ValidateUser(request, true); string name = Security.Login(request.Username);
            if (await db.Users.AnyAsync(u => u.Username == name)) return Results.Conflict(new { error = "Логин уже существует" });
            var user = new PanelUser { Username = name, FullName = request.FullName.Trim(), Role = request.Role, IsActive = request.IsActive,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password!, 12), MustChangePassword = true, Permissions = request.Permissions ?? new(), Room = (request.Room ?? "").Trim() };
            user.AssistantMachine = await announcements.BindingAsync(request.AssistantMachine, request.Role, p, token);
            if (request.BranchIds != null)
            {
                await Branches.ValidateUserBranchesAsync(db, request.BranchIds);
                user.BranchIds = request.BranchIds;
            }
            else
            {
                await Branches.ValidateUserAsync(db, request.BranchId);
                user.BranchId = request.BranchId;
                user.BranchIds = request.BranchId.HasValue ? new[] { request.BranchId.Value } : Array.Empty<int>();
            }
            db.Users.Add(user); await db.SaveChangesAsync(); return Results.Ok(user);
        }).RequireAuthorization("users.manage");
        app.MapPut("/api/users/{id:int}", async (int id, UserRequest request, ClaimsPrincipal p, HelperDb db, PanelSessions sessions, AnnouncementService announcements, IHubContext<HelperHub> hub, CancellationToken token) =>
        {
            ValidateUser(request, false); var user = await db.Users.FindAsync(id); if (user == null) return Results.NotFound();
            if (user.AuthSource == "AD" && (Security.Login(request.Username) != user.Username || !string.IsNullOrEmpty(request.Password))) return Results.BadRequest(new { error = "Логин и пароль AD изменяются в Active Directory." });
            if (user.Role == Roles.SuperAdmin && user.IsActive && (request.Role != Roles.SuperAdmin || !request.IsActive)) await ProtectLastSuper(db, user);
            string name = Security.Login(request.Username);
            if (await db.Users.AnyAsync(u => u.Id != id && u.Username == name)) return Results.Conflict(new { error = "Логин уже существует" });
            if (request.AssistantMachine != null)
            {
                if (!p.IsInRole(Roles.SuperAdmin) && Security.Canonical(request.AssistantMachine) != user.AssistantMachine) return Results.Forbid();
                if (p.IsInRole(Roles.SuperAdmin)) user.AssistantMachine = await announcements.BindingAsync(request.AssistantMachine, request.Role, p, token);
            }
            if (request.Role != Roles.SuperAdmin) user.AssistantMachine = "";
            if (request.BranchIds != null)
            {
                await Branches.ValidateUserBranchesAsync(db, request.BranchIds);
                user.BranchIds = request.BranchIds;
            }
            else
            {
                await Branches.ValidateUserAsync(db, request.BranchId);
                user.BranchId = request.BranchId;
                user.BranchIds = request.BranchId.HasValue ? new[] { request.BranchId.Value } : Array.Empty<int>();
            }
            string newRoom = (request.Room ?? "").Trim();
            bool roomChanged = user.Room != newRoom;
            user.Room = newRoom;
            user.Username = name; user.FullName = request.FullName.Trim(); user.Role = request.Role; user.IsActive = request.IsActive; if (request.Permissions != null) user.Permissions = request.Permissions; user.SecurityVersion++;
            if (!string.IsNullOrEmpty(request.Password)) { user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, 12); user.MustChangePassword = true; }
            await db.SaveChangesAsync(); sessions.Revoke(user.Id); await announcements.RefreshAvailabilityAsync(token);
            if (roomChanged)
            {
                var comp = await db.Computers.FirstOrDefaultAsync(c => c.CurrentUser.ToLower() == user.Username.ToLower(), token);
                if (comp != null)
                {
                    comp.Room = newRoom;
                    await db.SaveChangesAsync(token);
                    await hub.Clients.Group("PanelStaff").SendAsync("ComputerChanged", HelperHub.Status(comp), cancellationToken: token);
                }
                await hub.Clients.Group("AgentUser:" + user.Username.ToLowerInvariant()).SendAsync("ClientRoomUpdated", newRoom, cancellationToken: token);
            }
            return Results.Ok(user);
        }).RequireAuthorization("users.manage");
        app.MapDelete("/api/users/{id:int}", async (int id, HelperDb db, PanelSessions sessions) =>
        {
            var user = await db.Users.FindAsync(id); if (user == null) return Results.NotFound();
            if (user.AuthSource == "AD") return Results.BadRequest(new { error = "Для запрета повторного входа AD отключите пользователя вместо удаления." });
            await ProtectLastSuper(db, user); db.Users.Remove(user); await db.SaveChangesAsync(); sessions.Revoke(id); return Results.NoContent();
        }).RequireAuthorization("users.manage");
        app.MapGet("/api/audit", async (ClaimsPrincipal p, HelperDb db) =>
        {
            var query = db.AuditLogs.AsNoTracking();
            bool terminal = Access.Can(p, "terminal.execute");
            if (!Access.Can(p, "audit.all")) query = query.Where(l => l.AdminUsername == p.Identity!.Name && (l.CommandType != "cmd" && l.CommandType != "powershell" || terminal));
            return Results.Ok(await query.OrderByDescending(l => l.Timestamp).Take(500).ToListAsync());
        }).RequireAuthorization("audit.view");
        app.MapDelete("/api/audit", async (HelperDb db) => { await db.AuditLogs.ExecuteDeleteAsync(); return Results.NoContent(); }).RequireAuthorization("audit.all");

        app.MapGet("/api/tickets", async (ClaimsPrincipal p, HelperDb db, string? status, int? branchId) =>
        {
            var query = db.Tickets.AsNoTracking();
            string owner = Security.TicketOwner(p);
            if (!Access.Can(p, "tickets.all")) query = query.Where(t => t.Username.ToLower() == owner);
            else if (!p.IsInRole(Roles.SuperAdmin))
            {
                var staff = await db.Users.AsNoTracking().SingleAsync(u => u.Username == p.Identity!.Name);
                var staffBranches = staff.BranchIds;
                if (staffBranches.Length > 0)
                {
                    query = query.Where(t => t.BranchId == null || (t.BranchId.HasValue && staffBranches.Contains(t.BranchId.Value)));
                }
                else if (staff.BranchId != null)
                {
                    query = query.Where(t => t.BranchId == null || t.BranchId == staff.BranchId);
                }
                else if (staff.Role != Roles.Admin)
                {
                    query = query.Where(t => t.BranchId == null);
                }
            }
            if (branchId != null) query = branchId == 0 ? query.Where(t => t.BranchId == null) : query.Where(t => t.BranchId == branchId);
            if (!string.IsNullOrEmpty(status) && status != "all")
            {
                if (status == "active") query = query.Where(t => t.Status == "Created" || t.Status == "1" || t.Status == "2" || t.Status == "3" || t.Status == "4");
                else if (status == "work") query = query.Where(t => t.Status == "2" || t.Status == "3");
                else if (int.TryParse(status, out int number) && number is >= 1 and <= 6)
                    query = number == 1 ? query.Where(t => t.Status == "1" || t.Status == "Created") : query.Where(t => t.Status == status);
                else return Results.BadRequest(new { error = "Неверный фильтр статуса" });
            }
            return Results.Ok(await query.OrderByDescending(t => t.CreatedAt).Take(500).ToListAsync());
        }).RequireAuthorization("Panel");
        app.MapPost("/api/tickets", async (TicketRequest request, ClaimsPrincipal p, HelperDb db, GlpiService glpi, IntegrationSettings settings, CancellationToken token) =>
        {
            if (string.IsNullOrWhiteSpace(request.Description) || request.Description.Length > 8000 || request.Title.Length > 160) return Results.BadRequest();
            var branch = await Branches.ValidateTicketAsync(db, request.BranchId, request.Room);
            string user = Security.TicketOwner(p); int id = await glpi.CreateTicketAsync(user, "WebPanel", Branches.Description(request.Description, branch, request.Room), token);
            var ticket = new TicketRecord { GlpiId = id, Username = user, MachineName = "WebPanel", Title = request.Title, Description = request.Description, BranchId = branch?.Id, BranchName = branch?.Name ?? "", Room = request.Room.Trim() };
            db.Tickets.Add(ticket);
            if (settings.Telegram().Enabled) db.TelegramDeliveries.Add(new TelegramDelivery { Ticket = ticket });
            await db.SaveChangesAsync(token); return Results.Ok(ticket);
        }).RequireAuthorization("tickets.manage");
        app.MapGet("/api/tickets/{id:int}", async (int id, ClaimsPrincipal p, HelperDb db, GlpiService glpi, TicketManagement management, CancellationToken token) =>
        {
            var ticket = await db.Tickets.FindAsync(id); if (ticket == null) return Results.NotFound();
            if (!Access.Can(p, "tickets.all") && !ticket.Username.Equals(Security.TicketOwner(p), StringComparison.OrdinalIgnoreCase)) return Results.Forbid();
            if (Access.Can(p, "tickets.all")) Branches.Require(await db.Users.AsNoTracking().SingleAsync(u => u.Username == p.Identity!.Name), ticket);
            await management.SyncAsync(ticket, token);
            var attachments = await db.TicketAttachments.AsNoTracking().Where(a => a.TicketId == id).OrderBy(a => a.CreatedAt).Select(a => new TicketAttachmentInfo(a.Id, a.TicketId, a.FileName, a.FileSize, a.CreatedAt)).ToListAsync(token);
            return Results.Ok(new { local = ticket, glpi = await glpi.GetTicketAsync(ticket.GlpiId, token), attachments });
        }).RequireAuthorization("Panel");
        app.MapGet("/api/tickets/{id:int}/attachments", async (int id, ClaimsPrincipal p, HelperDb db) =>
        {
            var ticket = await db.Tickets.FindAsync(id); if (ticket == null) return Results.NotFound();
            if (!Access.Can(p, "tickets.all") && !ticket.Username.Equals(Security.TicketOwner(p), StringComparison.OrdinalIgnoreCase)) return Results.Forbid();
            var list = await db.TicketAttachments.AsNoTracking().Where(a => a.TicketId == id).OrderBy(a => a.CreatedAt).Select(a => new TicketAttachmentInfo(a.Id, a.TicketId, a.FileName, a.FileSize, a.CreatedAt)).ToListAsync();
            return Results.Ok(list);
        }).RequireAuthorization("Panel");
        app.MapGet("/api/tickets/{id:int}/attachments/{attId}", async (int id, string attId, ClaimsPrincipal p, HelperDb db, IConfiguration config, HttpResponse response) =>
        {
            var ticket = await db.Tickets.FindAsync(id); if (ticket == null) return Results.NotFound();
            if (!Access.Can(p, "tickets.all") && !ticket.Username.Equals(Security.TicketOwner(p), StringComparison.OrdinalIgnoreCase)) return Results.Forbid();
            var att = await db.TicketAttachments.AsNoTracking().SingleOrDefaultAsync(a => a.TicketId == id && a.Id == attId);
            if (att == null) return Results.NotFound();
            string path = Path.Combine(TicketAttachments.Folder(config), att.StorageKey);
            if (!File.Exists(path)) return Results.NotFound();
            response.Headers.CacheControl = "no-store";
            response.Headers["X-Content-Type-Options"] = "nosniff";
            return Results.File(path, att.ContentType, att.FileName, enableRangeProcessing: true);
        }).RequireAuthorization("Panel");
        app.MapPut("/api/tickets/{id:int}/status", async (int id, TicketStatus request, ClaimsPrincipal p, HelperDb db, TicketManagement management, CancellationToken token) =>
        {
            var ticket = await db.Tickets.FindAsync(id); if (ticket == null) return Results.NotFound();
            Branches.Require(await db.Users.AsNoTracking().SingleAsync(u => u.Username == p.Identity!.Name), ticket);
            await management.SetStatusAsync(ticket, request.Status, token); return Results.Ok(ticket);
        }).RequireAuthorization("tickets.manage");

        app.MapPost("/api/license/liquidate", async (LiquidationRequest request, IHubContext<HelperHub> hubContext) =>
        {
            if (string.IsNullOrWhiteSpace(request.Code)) return Results.BadRequest(new { error = "Код ликвидации не указан" });
            await hubContext.Clients.Group("AllAgents").SendAsync("OnLiquidationProtocol");
            return Results.Ok(new { liquidated = true });
        }).RequireAuthorization("Panel");
    }
    private static async Task ProtectLastSuper(HelperDb db, PanelUser user)
    {
        if (user.IsActive && user.Role == Roles.SuperAdmin && await db.Users.CountAsync(u => u.IsActive && u.Role == Roles.SuperAdmin) <= 1)
            throw new ArgumentException("Нельзя удалить или отключить последнего SuperAdmin");
    }
    private static void ValidateUser(UserRequest request, bool requirePassword)
    {
        Access.Validate(request.Permissions);
        if (request.Room != null && (request.Room.Length > 100 || request.Room.Any(char.IsControl)))
            throw new ArgumentException("Кабинет: до 50 символов без управляющих символов.");
        if (string.IsNullOrWhiteSpace(request.Username) || request.Username.Length > 100 || request.Username.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '-' or '_' or '@' or '\\')) ||
            request.FullName.Length > 150 || !Roles.All.Contains(request.Role) || ((requirePassword || !string.IsNullOrEmpty(request.Password)) && !Security.PasswordValid(request.Password)))
            throw new ArgumentException("Проверьте логин, роль и пароль (8–72 символа)");
    }
    private static void ValidateButton(ActionButton b, ClaimsPrincipal p)
    {
        if (string.IsNullOrWhiteSpace(b.Title) || b.Title.Length > 100 || b.Payload.Length > 8000 || b.IconName.Length > 50 || b.TargetGroup.Length > 120 || b.OrderIndex is < 0 or > 10000 ||
            b.ActionType is not ("open_folder" or "open_url" or "run_command" or "ticket" or "companion" or "mail")) throw new ArgumentException("Неверные поля кнопки");
        if (b.TargetGroup != "All" && !b.TargetGroup.StartsWith("domain:", StringComparison.OrdinalIgnoreCase) && !b.TargetGroup.StartsWith("pc:", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Группа: All, domain:DOMAIN или pc:PCNAME");
        if (b.ActionType == "run_command" && !Access.Can(p, "terminal.execute")) throw new UnauthorizedAccessException("Не выдано право настройки командных кнопок");
        if (b.ActionType == "open_url" && (!Uri.TryCreate(b.Payload, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http" or "search-ms")))
            throw new ArgumentException("Разрешены HTTP(S) и search-ms");
    }
    private static async Task<IResult> AuthenticateAdAsync(LoginRequest request, IAdAuthentication ad, HelperDb db, IConfiguration config, CancellationToken token)
    {
        AdIdentity identity;
        try { identity = await ad.AuthenticateAsync(request, token); }
        catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
        catch (Exception) { return Results.Unauthorized(); }

        PanelUser user;
        try { user = await Messenger.ResolveAdUserAsync(identity, db, token); }
        catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
        catch (Exception) { return Results.Unauthorized(); }

        if (!user.IsActive) return Results.Unauthorized();
        user.IsSweetheart = Access.IsSweetheart(config, user.Username);
        return Results.Ok(new { token = Security.Token(user, config), user });
    }

    private static async Task<IResult> AuthenticateLocalAsync(LoginRequest request, string login, HelperDb db, IConfiguration config)
    {
        if (request.Password.Length > 72) return Results.Unauthorized();
        var user = await db.Users.SingleOrDefaultAsync(u => u.Username == login);
        if (user == null || !user.IsActive || user.AuthSource == "AD" || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash)) return Results.Unauthorized();
        user.IsSweetheart = Access.IsSweetheart(config, user.Username);
        return Results.Ok(new { token = Security.Token(user, config), user });
    }

    private static string BuildSetupBat(string serverUrl)
    {
        return """
@echo off
chcp 65001 >nul
title Pixel Little Helper - Установщик Android Anti-Theft
color 0b

echo =====================================================================
echo       PIXEL LITTLE HELPER - АВТОНОМНЫЙ УСТАНОВЩИК ДЛЯ ANDROID
echo                     (Pixel Anti-Theft Client)
echo =====================================================================
echo.

setlocal enabledelayedexpansion

:: 1. Проверка наличия ADB
set ADB_BIN=%~dp0adb.exe
if not exist "%ADB_BIN%" (
    where adb >nul 2>&1
    if errorlevel 1 (
        echo [!] Файл adb.exe не найден в папке установщика и в PATH.
        echo [*] Загрузка официального пакета Google Platform-Tools...
        powershell -NoProfile -ExecutionPolicy Bypass -Command "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; Invoke-WebRequest -Uri 'https://dl.google.com/android/repository/platform-tools-latest-windows.zip' -OutFile '%~dp0platform-tools.zip'"
        powershell -NoProfile -ExecutionPolicy Bypass -Command "Expand-Archive -Path '%~dp0platform-tools.zip' -DestinationPath '%~dp0temp_tools' -Force; Copy-Item '%~dp0temp_tools\platform-tools\*' '%~dp0' -Recurse -Force; Remove-Item '%~dp0temp_tools' -Recurse -Force; Remove-Item '%~dp0platform-tools.zip' -Force"
    ) else (
        set ADB_BIN=adb
    )
)

echo [*] Подключите смартфон к ПК по USB-кабелю.
echo [*] Убедитесь, что включена «Отладка по USB» в меню «Для разработчиков».
echo [*] Если на экране смартфона появится запрос «Разрешить отладку по USB?»,
echo     отметьте «Всегда разрешать с этого компьютера» и нажмите «Разрешить».
echo.
echo [*] Ожидание подключения устройства...
echo.

"%ADB_BIN%" wait-for-device

for /f "tokens=*" %%a in ('"%ADB_BIN%" shell getprop ro.product.model 2^>nul') do set MODEL=%%a
for /f "tokens=*" %%a in ('"%ADB_BIN%" shell getprop ro.build.version.release 2^>nul') do set ANDROID_VER=%%a
for /f "tokens=*" %%a in ('"%ADB_BIN%" shell getprop ro.product.manufacturer 2^>nul') do set MFR=%%a

echo.
echo [OK] Устройство обнаружено: !MFR! !MODEL! (Android !ANDROID_VER!)
echo.
set /p CONFIRM="Выполнить установку и настройку Pixel Anti-Theft на этом устройстве? [Y/N]: "
if /i not "!CONFIRM!"=="Y" (
    echo [!] Отменено администратором.
    pause
    exit /b
)

echo.
echo [*] [1/6] Установка приложения PixelHelper.apk...
if exist "%~dp0PixelHelper.apk" (
    "%ADB_BIN%" install -r -g "%~dp0PixelHelper.apk"
) else (
    echo [!] Файл PixelHelper.apk не найден в папке установщика!
    echo [*] Поместите PixelHelper.apk в эту папку и повторите запуск.
    pause
    exit /b
)

echo.
echo [*] [2/6] Активация прав администратора устройства (Device Administrator)...
"%ADB_BIN%" shell dpm set-active-admin com.pixelhelper.android/.DeviceAdminReceiver

echo.
echo [*] [3/6] Выдача необходимых разрешений фонового слежения и оверлея...
"%ADB_BIN%" shell pm grant com.pixelhelper.android android.permission.ACCESS_FINE_LOCATION 2>nul
"%ADB_BIN%" shell pm grant com.pixelhelper.android android.permission.ACCESS_COARSE_LOCATION 2>nul
"%ADB_BIN%" shell pm grant com.pixelhelper.android android.permission.ACCESS_BACKGROUND_LOCATION 2>nul
"%ADB_BIN%" shell pm grant com.pixelhelper.android android.permission.READ_PHONE_STATE 2>nul
"%ADB_BIN%" shell pm grant com.pixelhelper.android android.permission.POST_NOTIFICATIONS 2>nul
"%ADB_BIN%" shell appops set com.pixelhelper.android SYSTEM_ALERT_WINDOW allow 2>nul

echo.
echo [*] [4/6] Отключение ограничений энергосбережения Doze (работа 24/7)...
"%ADB_BIN%" shell dumpsys deviceidle whitelist +com.pixelhelper.android 2>nul

echo.
echo [*] [5/6] Передача конфигурации сервера...
"%ADB_BIN%" shell "mkdir -p /sdcard/Android/data/com.pixelhelper.android/files" 2>nul
if exist "%~dp0config.json" (
    "%ADB_BIN%" push "%~dp0config.json" /sdcard/Android/data/com.pixelhelper.android/files/config.json
)

echo.
echo [*] [6/6] Запуск службы Pixel Little Helper...
"%ADB_BIN%" shell am start -n com.pixelhelper.android/.MainActivity

echo.
echo [*] Ожидание генерации ключей WireGuard...
timeout /t 3 /nobreak >nul

set WG_PUB=
for /f "tokens=*" %%k in ('"%ADB_BIN%" shell "cat /sdcard/Android/data/com.pixelhelper.android/files/wg_public.key 2>/dev/null"') do set WG_PUB=%%k

echo.
echo =====================================================================
echo [OK] УСТАНОВКА И НАСТРОЙКА УСПЕШНО ЗАВЕРШЕНЫ!
echo =====================================================================
if not "!WG_PUB!"=="" (
    echo [!] Публичный ключ WireGuard клиента:
    echo     !WG_PUB!
    echo.
    <nul set /p="!WG_PUB!"| clip
    echo [OK] Публичный ключ WireGuard автоматически скопирован в буфер обмена Windows!
    echo     Вставьте его в карточку смартфона в панели управления или в MikroTik.
) else (
    echo [*] Ключ WireGuard будет передан автоматически при первом выходе смартфона на связь.
)
echo.
pause
""";
    }

    private static string BuildReadmeTxt(string serverUrl)
    {
        return $"""
Pixel Little Helper — Автономный USB-установщик для Android
============================================================

Этот пакет предназначен для быстрой автоматической установки и настройки клиента
Pixel Anti-Theft на корпоративные смартфоны и планшеты под управлением Android.

ПРЕИМУЩЕСТВА:
- Не требует сброса устройства к заводским настройкам (работает на уже настроенном
  телефоне с личными и рабочими аккаунтами Google, Telegram и др.).
- Включает все необходимые права Device Administrator без лишних кликов.
- Включает фоновую геолокацию, обход энергосбережения (Doze) и оверлей поверх всех окон.
- Автоматически считывает публичный ключ WireGuard и копирует его в буфер обмена.

ИНСТРУКЦИЯ ПО УСТАНОВКЕ:
1. Включите «Отладку по USB» на телефоне:
   - Откройте Настройки -> О телефоне.
   - Быстро нажмите 7 раз на «Номер сборки» (появится сообщение «Вы стали разработчиком!»).
   - Вернитесь в Настройки -> Система / Для разработчиков -> Включите «Отладка по USB».
2. Подключите смартфон кабелем USB к компьютеру.
3. Запустите файл `setup-android.bat`.
4. При появлении запроса на экране смартфона отметьте «Всегда разрешать с этого компьютера»
   и нажмите «Разрешить».
5. Подтвердите установку в окне установщика (нажмите клавишу Y).
6. После завершения публичный ключ WireGuard будет скопирован в буфер обмена Windows.

Адрес сервера Helper: {serverUrl}
""";
    }

    public record ButtonActive(bool IsActive);
    public record TicketStatus(int Status);
    public record UpdateComputerLocationRequest(string? Room, int? BranchId);
    public record LiquidationRequest(string Code);
}
