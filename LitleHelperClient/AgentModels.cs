namespace PixelHelper;

public sealed record MachineInfo(string MachineName, string UserName, string DomainName, string IpAddress, string OsVersion);
public sealed record ActionButton(int Id, string Title, string IconName, string ActionType, string Payload, int OrderIndex, bool IsActive, string TargetGroup);
public sealed record CommandEnvelope(string TaskId, string Type, string Payload);
public sealed record MemorySlot(ulong CapacityBytes, string Manufacturer, uint SpeedMHz, string Slot);
public sealed record LogicalDisk(string Name, ulong TotalBytes, ulong FreeBytes, string FileSystem);
public sealed record PhysicalDisk(string Model, ulong SizeBytes, string InterfaceType);
public sealed record SoftwareInfo(string Name, string Version);
public sealed record HardwareInfo(string CpuModel, ulong TotalRamBytes, MemorySlot[] MemorySlots, LogicalDisk[] LogicalDisks, PhysicalDisk[] PhysicalDisks, string[] Errors);
public sealed record InventorySnapshot(HardwareInfo Hardware, SoftwareInfo[] Software);
