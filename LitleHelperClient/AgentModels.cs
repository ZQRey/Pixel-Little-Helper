namespace PixelHelper;

public sealed record MachineInfo(string MachineName, string UserName, string DomainName, string IpAddress, string OsVersion, string UserFullName = "", bool SupportsSystemButtons = true, bool SupportsAiDiagnostics = true, string ClientVersion = "", bool IsPortable = false);
public sealed record VisibleWifiNetwork(string Ssid, string Bssid, int SignalStrength);
public sealed record AntiTheftTelemetryRequest(string MachineName, string HardwareUuid, bool IsPortable, int BatteryPercent, bool IsCharging, string ConnectedSsid, string ConnectedBssid, List<VisibleWifiNetwork>? VisibleNetworks = null, string ClientVersion = "", bool WireGuardActive = false);
public sealed record AntiTheftTelemetryResponse(bool IsLockedDown, string LockMessage, bool TriggerBitLockerRecovery, bool Success = true, bool LockScreen = false, bool LockSession = false);

public sealed record ActionButton(
    int Id,
    string Title,
    string IconName,
    string ActionType,
    string Payload,
    int OrderIndex,
    bool IsActive = true,
    string TargetGroup = "All",
    Dictionary<string, string>? TitleTranslations = null,
    Dictionary<string, string>? DescriptionTranslations = null,
    string Description = "",
    string? SystemKey = null,
    bool IsSystem = false)
{
    public string GetLocalizedTitle(string? langCode = null)
    {
        string lang = langCode ?? Loc.Code ?? "ru";
        if (TitleTranslations != null && TitleTranslations.TryGetValue(lang, out var t) && !string.IsNullOrWhiteSpace(t))
            return t;
        return Title;
    }

    public string GetLocalizedDescription(string? langCode = null)
    {
        string lang = langCode ?? Loc.Code ?? "ru";
        if (DescriptionTranslations != null && DescriptionTranslations.TryGetValue(lang, out var d) && !string.IsNullOrWhiteSpace(d))
            return d;
        return Description;
    }
}
public sealed record CommandEnvelope(string TaskId, string Type, string Payload, bool Elevated = false, string AdminUser = "");
public sealed record MemorySlot(ulong CapacityBytes, string Manufacturer, uint SpeedMHz, string Slot);
public sealed record LogicalDisk(string Name, ulong TotalBytes, ulong FreeBytes, string FileSystem);
public sealed record PhysicalDisk(string Model, ulong SizeBytes, string InterfaceType);
public sealed record SoftwareInfo(string Name, string Version);
public sealed record HardwareInfo(string CpuModel, ulong TotalRamBytes, MemorySlot[] MemorySlots, LogicalDisk[] LogicalDisks, PhysicalDisk[] PhysicalDisks, string[] Errors, PrinterStatusInfo[]? Printers = null);
public sealed record InventorySnapshot(HardwareInfo Hardware, SoftwareInfo[] Software);
public sealed record CartridgeReadyNotice(string Username, string Marker, string Model, string Cabinet, string ItOffice, string Message);
public sealed record TicketReplyNotice(int GlpiId, string Title, string Author, string Text);
public sealed record EmergencyAlertNotice(string Code, string Title, string? Cabinet, string? Notes, string? Subcode, string? ImageBase64, string? ImageUrl, int DurationSeconds, int? CallId, string? Department, List<string>? Languages = null, string? EventId = null);

public sealed record TicketSurveyNotice(string Id, int TicketId, string Title, string Specialist, DateTime ExpiresAt,
    bool Mandatory = false, DateTime? DueAt = null, DateTime? NextNoticeAt = null, int DeferralsLeft = 0, string State = "Pending", string Error = "", bool Required = false);
public sealed record TicketSurveyResult(bool Saved, bool Queued, string? Error);

internal static class SystemButtonVisibility
{
    internal static bool Enabled(IEnumerable<ActionButton> buttons, string key) =>
        buttons.FirstOrDefault(b => b.SystemKey == key)?.IsActive ?? true;
}