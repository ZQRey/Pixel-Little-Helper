using LitleHelperServer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;

public static class EmergencyTests
{
    private static void Check(bool ok, string text)
    {
        if (!ok) throw new Exception("FAILED: " + text);
        Console.WriteLine("PASS " + text);
    }

    private class MockHubClients : IHubCallerClients, IHubClients
    {
        public readonly Dictionary<string, MockClientProxy> GroupsMap = new();
        public IClientProxy All => GetGroup("All");
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => GetGroup("AllExcept");
        public IClientProxy Client(string connectionId) => GetGroup("Client:" + connectionId);
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => GetGroup("Clients");
        public IClientProxy Group(string groupName) => GetGroup(groupName);
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => GetGroup("GroupExcept:" + groupName);
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => GetGroup("Groups");
        public IClientProxy User(string userId) => GetGroup("User:" + userId);
        public IClientProxy Users(IReadOnlyList<string> userIds) => GetGroup("Users");

        public IClientProxy Caller => GetGroup("Caller");
        public IClientProxy Others => GetGroup("Others");
        public IClientProxy OthersInGroup(string groupName) => GetGroup("OthersInGroup:" + groupName);

        private MockClientProxy GetGroup(string name)
        {
            if (!GroupsMap.TryGetValue(name, out var proxy))
            {
                proxy = new MockClientProxy();
                GroupsMap[name] = proxy;
            }
            return proxy;
        }
    }

    private class MockClientProxy : ISingleClientProxy
    {
        public readonly List<(string Method, object?[] Args)> Invocations = new();
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            Invocations.Add((method, args));
            return Task.CompletedTask;
        }

        public Task<T> InvokeCoreAsync<T>(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            Invocations.Add((method, args));
            return Task.FromResult(default(T)!);
        }
    }

    private class MockHubContext<THub> : IHubContext<THub> where THub : Hub
    {
        public MockHubClients MockClients { get; } = new();
        public IHubClients Clients => MockClients;
        public IGroupManager Groups => throw new NotImplementedException();
    }

    private class MockHttpHandler : HttpMessageHandler
    {
        public string? ActiveAlarmId;
        public string? ActiveAlarmCode;
        public string? ActiveAlarmTitle;
        public string? ActiveAlarmCabinet;

        public int? ActiveCallId;
        public int? ActiveCallDeptId;
        public string? ActiveCallCabinet;

        public readonly List<HttpRequestMessage> CapturedRequests = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CapturedRequests.Add(request);
            string path = request.RequestUri!.AbsolutePath;

            if (path.EndsWith("/api/clients/heartbeat"))
            {
                var responseObj = new Dictionary<string, object?>();

                if (!string.IsNullOrEmpty(ActiveAlarmId))
                {
                    responseObj["active_broadcast"] = new
                    {
                        alarm_id = ActiveAlarmId,
                        code = ActiveAlarmCode ?? "CODE_RED",
                        title = ActiveAlarmTitle ?? "ПОЖАР",
                        cabinet = ActiveAlarmCabinet ?? "101"
                    };
                }
                else
                {
                    responseObj["active_broadcast"] = null;
                }

                if (ActiveCallId.HasValue && ActiveCallId.Value > 0)
                {
                    responseObj["active_specialist_call"] = new
                    {
                        id = ActiveCallId.Value,
                        department_id = ActiveCallDeptId ?? 1,
                        cabinet = ActiveCallCabinet ?? "202"
                    };
                }
                else
                {
                    responseObj["active_specialist_call"] = null;
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(responseObj)
                });
            }

            if (path.EndsWith("/api/broadcast/client/alert"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { success = true })
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private class SingleHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new HttpClient(handler);
    }

    public static async Task RunAsync()
    {
        Console.WriteLine("Running Emergency integration checks...");

        string tempFolder = Path.Combine(Path.GetTempPath(), "emergency-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempFolder);

        try
        {
            // 1. Settings persistence test
            string settingsFile = Path.Combine(tempFolder, "integrations.json");
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Integrations:SettingsFile"] = settingsFile
            }).Build();
            var protection = new EphemeralDataProtectionProvider();
            var settings = new IntegrationSettings(config, protection);

            Check(settings.Emergency().AllowStandalone == true, "emergency default allows standalone trigger");
            Check(settings.Emergency().AllowClientTrigger == true, "emergency default allows client trigger");

            var dept1 = new SpecialistDepartmentConfig
            {
                Id = 10,
                Name = "Реанимация",
                SpecialistTitle = "Реаниматолог",
                Color = "#dc2626",
                ResponsibleUsers = new() { "doctor_bob", "nurse_anna" }
            };
            var customOpts = new EmergencyOptions(
                Enabled: true,
                ServerUrl: "http://127.0.0.1:8085",
                ApiKey: "test-secret-key-123",
                AllowStandalone: true,
                AllowClientTrigger: true,
                Departments: new() { dept1 }
            );

            settings.SaveEmergency(customOpts);
            var reloaded = settings.Emergency();
            Check(reloaded.Enabled == true, "emergency options enabled persisted");
            Check(reloaded.ServerUrl == "http://127.0.0.1:8085", "emergency server url persisted");
            Check(reloaded.ApiKey == "test-secret-key-123", "emergency api key persisted");
            Check(reloaded.Departments?.Count == 1 && reloaded.Departments[0].Name == "Реанимация", "emergency department configs persisted");

            // 2. EmergencySyncWorker Heartbeat Poll Tests
            var httpHandler = new MockHttpHandler();
            var httpFactory = new SingleHttpClientFactory(httpHandler);
            var hubContext = new MockHubContext<HelperHub>();
            var worker = new EmergencySyncWorker(settings, httpFactory, hubContext, NullLogger<EmergencySyncWorker>.Instance);

            // Test case: No active alarms
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var workerTask = worker.StartAsync(cts.Token);
            await Task.Delay(300);

            Check(httpHandler.CapturedRequests.Count > 0, "EmergencySyncWorker sends heartbeat request to external server");
            var req = httpHandler.CapturedRequests[0];
            Check(req.Headers.Contains("X-Client-Key") && req.Headers.GetValues("X-Client-Key").First() == "test-secret-key-123", "heartbeat passes X-Client-Key");

            // Test case: Active broadcast alarm detected
            httpHandler.ActiveAlarmId = "alarm-999";
            httpHandler.ActiveAlarmCode = "CODE_RED";
            httpHandler.ActiveAlarmTitle = "ПОЖАР В КОРПУСЕ 2";
            httpHandler.ActiveAlarmCabinet = "315";

            await Task.Delay(3500); // allow worker loop iteration

            Check(hubContext.MockClients.GroupsMap.ContainsKey("AllAgents"), "AllAgents group received broadcast");
            var allAgentsProxy = hubContext.MockClients.GroupsMap["AllAgents"];
            var alertInv = allAgentsProxy.Invocations.FirstOrDefault(i => i.Method == "EmergencyAlertNotice");
            Check(alertInv.Method == "EmergencyAlertNotice", "EmergencyAlertNotice method invoked on AllAgents");
            var notice = alertInv.Args[0] as EmergencyAlertNotice;
            Check(notice != null && notice.Code == "CODE_RED" && notice.Cabinet == "315", "alert notice contains correct code and cabinet");

            // Test deduplication: Same alarm is not rebroadcasted
            int invCount = allAgentsProxy.Invocations.Count;
            await Task.Delay(3500);
            Check(allAgentsProxy.Invocations.Count == invCount, "identical active alarm is deduplicated and not rebroadcast");

            // Test case: Specialist call detected with targeted department
            httpHandler.ActiveAlarmId = ""; // clear alarm
            httpHandler.ActiveCallId = 555;
            httpHandler.ActiveCallDeptId = 10; // matches dept1 (Реанимация: doctor_bob, nurse_anna)
            httpHandler.ActiveCallCabinet = "410";

            await Task.Delay(3500);

            Check(hubContext.MockClients.GroupsMap.ContainsKey("AgentUser:doctor_bob"), "AgentUser:doctor_bob group received specialist call");
            Check(hubContext.MockClients.GroupsMap.ContainsKey("AgentUser:nurse_anna"), "AgentUser:nurse_anna group received specialist call");
            var doctorProxy = hubContext.MockClients.GroupsMap["AgentUser:doctor_bob"];
            var docInv = doctorProxy.Invocations.LastOrDefault(i => i.Method == "EmergencyAlertNotice");
            var docNotice = docInv.Args[0] as EmergencyAlertNotice;
            Check(docNotice != null && docNotice.CallId == 555 && docNotice.Cabinet == "410", "specialist notice delivered to targeted doctor");

            cts.Cancel();
            await worker.StopAsync(CancellationToken.None);

            // 3. HelperHub TriggerEmergencyAlert Direct Dispatch & Standalone Logic
            using var db = new HelperDb(new DbContextOptionsBuilder<HelperDb>().UseSqlite("Data Source=" + Path.Combine(tempFolder, "helper.db")).Options);
            await db.Database.EnsureCreatedAsync();

            var computer = new Computer
            {
                MachineName = "DESKTOP-TEST1",
                ConnectionId = "conn-machine-1",
                CurrentUser = "testuser",
                DomainName = "DOMAIN",
                IsOnline = true,
                LastSeen = DateTime.UtcNow
            };
            db.Computers.Add(computer);
            await db.SaveChangesAsync();

            // Check Pink code with base64 photo
            string pinkBase64 = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
            var ticketGate = new TicketManagementGate();
            var hub2 = new HelperHub(
                db,
                null!,
                null!,
                null!,
                settings,
                ticketGate,
                httpFactory
            );

            // Set hub caller identity via HubCallerContext
            var hubContextProp = typeof(Hub).GetProperty(nameof(Hub.Context));
            var mockCallerContext = new MockCallerContext("DESKTOP-TEST1", "conn-machine-1");
            hubContextProp?.SetValue(hub2, mockCallerContext);

            // Set hub Clients
            var hubClientsProp = typeof(Hub).GetProperty(nameof(Hub.Clients));
            var hubClientsMock = new MockHubClients();
            hubClientsProp?.SetValue(hub2, hubClientsMock);

            await hub2.TriggerEmergencyAlert("CODE_PINK", "101", "Ребёнок потерялся в холле", pinkBase64, null);

            var pinkProxy = hubClientsMock.GroupsMap["AllAgents"];
            var pinkInv = pinkProxy.Invocations.LastOrDefault(i => i.Method == "EmergencyAlertNotice");
            Check(pinkInv.Method == "EmergencyAlertNotice", "CODE_PINK alert notice sent to AllAgents");
            var pinkNotice = pinkInv.Args[0] as EmergencyAlertNotice;
            Check(pinkNotice != null && pinkNotice.Code == "CODE_PINK" && pinkNotice.ImageBase64 == pinkBase64, "CODE_PINK alert notice carries photo Base64 data");

            // Verify Audit Log persisted
            var audit = await db.AuditLogs.OrderByDescending(a => a.Id).FirstOrDefaultAsync();
            Check(audit != null && audit.CommandType == "emergency_trigger" && audit.CommandPayload.Contains("CODE_PINK"), "audit log record created for emergency_trigger");

            // Check Specialist Call dispatch through hub
            await hub2.TriggerEmergencyAlert("SPECIALIST_CALL", "305", "Срочно в операционную", null, "Реанимация");
            Check(hubClientsMock.GroupsMap.ContainsKey("AgentUser:doctor_bob"), "Hub TriggerEmergencyAlert routes SPECIALIST_CALL to department responsible user doctor_bob");
            Check(hubClientsMock.GroupsMap.ContainsKey("AgentUser:nurse_anna"), "Hub TriggerEmergencyAlert routes SPECIALIST_CALL to department responsible user nurse_anna");

            // Check disabled trigger rejection
            settings.SaveEmergency(customOpts with { AllowClientTrigger = false, AllowStandalone = false });
            try
            {
                await hub2.TriggerEmergencyAlert("CODE_RED", "101", "Тест");
                throw new Exception("Disabled emergency trigger should throw exception");
            }
            catch (HubException ex)
            {
                Check(ex.Message.Contains("отключён"), "hub rejects trigger when AllowClientTrigger and AllowStandalone are disabled");
            }

            Console.WriteLine("ALL EMERGENCY INTEGRATION CHECKS PASSED!");
        }
        finally
        {
            try { Directory.Delete(tempFolder, true); } catch { }
        }
    }

    private class MockCallerContext(string machineName, string connectionId) : HubCallerContext
    {
        public override string ConnectionId => connectionId;
        public override string? UserIdentifier => machineName;
        public override ClaimsPrincipal? User
        {
            get
            {
                var identity = new ClaimsIdentity(
                    new[]
                    {
                        new Claim(ClaimTypes.Name, machineName),
                        new Claim(ClaimTypes.Role, "Agent")
                    },
                    "AgentAuth"
                );
                return new ClaimsPrincipal(identity);
            }
        }
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features => new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() { }
    }

    private class FeatureCollection : IFeatureCollection
    {
        private readonly Dictionary<Type, object> features = new();
        public object? this[Type key] { get => features.TryGetValue(key, out var v) ? v : null; set { if (value != null) features[key] = value; else features.Remove(key); } }
        public bool IsReadOnly => false;
        public int Revision => 0;
        public TFeature? Get<TFeature>() => (TFeature?)this[typeof(TFeature)];
        public void Set<TFeature>(TFeature? instance) => this[typeof(TFeature)] = instance!;
        public IEnumerator<KeyValuePair<Type, object>> GetEnumerator() => features.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => features.GetEnumerator();
    }
}
