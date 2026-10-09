namespace PixelHelper;

public sealed record MachineInfo(string MachineName, string UserName, string DomainName, string IpAddress, string OsVersion, string UserFullName = "");
public sealed record ActionButton(int Id, string Title, string IconName, string ActionType, string Payload, int OrderIndex, bool IsActive, string TargetGroup);
public sealed record CommandEnvelope(string TaskId, string Type, string Payload);
public sealed record MemorySlot(ulong CapacityBytes, string Manufacturer, uint SpeedMHz, string Slot);
public sealed record LogicalDisk(string Name, ulong TotalBytes, ulong FreeBytes, string FileSystem);
public sealed record PhysicalDisk(string Model, ulong SizeBytes, string InterfaceType);
public sealed record SoftwareInfo(string Name, string Version);
public sealed record HardwareInfo(string CpuModel, ulong TotalRamBytes, MemorySlot[] MemorySlots, LogicalDisk[] LogicalDisks, PhysicalDisk[] PhysicalDisks, string[] Errors);
public sealed record InventorySnapshot(HardwareInfo Hardware, SoftwareInfo[] Software);
public sealed record CartridgeReadyNotice(string Username, string Marker, string Model, string Cabinet, string ItOffice, string Message);
public sealed record TicketReplyNotice(int GlpiId, string Title, string Author, string Text);
public sealed record EmergencyAlertNotice(string Code, string Title, string? Cabinet, string? Notes, string? Subcode, string? ImageBase64, string? ImageUrl, int DurationSeconds, int? CallId, string? Department);
