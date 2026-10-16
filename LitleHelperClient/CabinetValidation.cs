namespace PixelHelper;
internal static class CabinetValidation
{
    internal static bool Valid(string? room, string machine) =>
        !string.IsNullOrWhiteSpace(room) && room.Trim().Length <= 50 &&
        !room.Trim().Equals(machine, StringComparison.OrdinalIgnoreCase) && !room.Any(char.IsControl);
}