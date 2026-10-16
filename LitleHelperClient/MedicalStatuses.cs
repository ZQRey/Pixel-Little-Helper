namespace PixelHelper;
public sealed record MedicalStatusConfig(int Id, string Key, string Title, string Emoji, string ColorHex, bool MuteSounds, bool MuteTts, int OrderIndex, bool IsActive = true, string Description = "")
{
    public static List<MedicalStatusConfig> Defaults() =>
    [
        new(0, "Available", "На приёме пациентов", "🟢", "#16A34A", false, false, 0),
        new(0, "InSurgery", "В операционной / На процедурах", "🔴", "#DC2626", true, true, 1),
        new(0, "InConsultation", "На консилиуме / Совещании", "🟡", "#CA8A04", true, true, 2),
        new(0, "Break", "Обед / Перерыв", "⚪", "#64748B", false, false, 3)
    ];
}