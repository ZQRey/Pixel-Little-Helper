namespace LitleHelperServer;

public static class GlpiReportReaction
{
    public static readonly string[] Labels = ["10 минут", "30 минут", "1 час", "3 часа", "5 часов", "Более"];
    public static readonly string[] Ranges = ["От 0 до 10 минут включительно", "Более 10 и до 30 минут включительно", "Более 30 минут и до 1 часа включительно", "Более 1 и до 3 часов включительно", "Более 3 и до 5 часов включительно", "Более 5 часов"];
    static readonly double[] Limits = [600, 1800, 3600, 10800, 18000];
    public static int? Bucket(double? seconds)
    {
        if (!seconds.HasValue || !double.IsFinite(seconds.Value) || seconds < 0) return null;
        for (int i = 0; i < Limits.Length; i++) if (seconds <= Limits[i]) return i;
        return 5;
    }
    public static object[] Distribution(ReportTicket[] rows) => Enumerable.Range(0, 6).Select(i => (object)new {
        id = i, label = Labels[i], range = Ranges[i], count = rows.Count(t => Bucket(t.AcceptanceSeconds) == i)
    }).ToArray();
    public const string Note = "От создания до принятия в работу, календарное время. Интервалы не пересекаются; верхняя граница включается. Без корректных дат создания и принятия заявки не включаются в столбцы.";
}
