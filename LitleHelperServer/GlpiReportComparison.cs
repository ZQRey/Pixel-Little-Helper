namespace LitleHelperServer;

public record ReportComparisonMetric(string Key, string Label, string Unit, double? Current, double? Previous,
    double? Delta, double? Percent, int? CurrentSamples = null, int? PreviousSamples = null);
public record ReportComparisonData(string Mode, DateOnly CurrentFrom, DateOnly CurrentTo, DateOnly? PreviousFrom,
    DateOnly? PreviousTo, string Rule, string Note, string? Warning, ReportComparisonMetric[] Metrics);

public static class GlpiReportComparison
{
    public static (DateOnly From, DateOnly To, string Rule) Previous(ReportFilter f, DateOnly today)
    {
        if (f.Comparison == "custom") return (f.CompareFrom!.Value, f.CompareTo!.Value, "Период задан вручную");
        int days = f.To.DayNumber - f.From.DayNumber + 1;
        string kind=f.PeriodKind;
        if (days==1 && kind is "auto" or "day") return (f.From.AddDays(-1),f.From.AddDays(-1),"Предыдущий день");
        bool fullYear = f.From.Month == 1 && f.From.Day == 1 && f.To == new DateOnly(f.From.Year,12,31);
        bool fullMonth = f.From.Day == 1 && f.To == f.From.AddMonths(1).AddDays(-1);
        if (kind is "auto" or "year" && (fullYear || f.To == today && f.From == new DateOnly(today.Year,1,1)))
            return (f.From.AddYears(-1),f.To.AddYears(-1), fullYear ? "Предыдущий календарный год" : "Та же прошедшая часть предыдущего года");
        if (kind is "auto" or "month" && (fullMonth || f.To == today && f.From == new DateOnly(today.Year,today.Month,1))) {
            var start = f.From.AddMonths(-1);
            return (start,fullMonth ? f.From.AddDays(-1) : start.AddDays(Math.Min(f.To.Day,DateTime.DaysInMonth(start.Year,start.Month))-1),
                fullMonth ? "Предыдущий календарный месяц" : "Та же прошедшая часть предыдущего месяца");
        }
        if (kind is "auto" or "week" && f.From.DayOfWeek == DayOfWeek.Monday && (days == 7 || f.To == today && days < 7))
            return (f.From.AddDays(-7),f.To.AddDays(-7), days == 7 ? "Предыдущая календарная неделя" : "Та же прошедшая часть предыдущей недели");
        return (f.From.AddDays(-days),f.From.AddDays(-1), days == 1 ? "Предыдущий день" : "Предыдущий интервал той же длительности");
    }
    public static ReportComparisonMetric Metric(string key,string label,string unit,double? current,double? previous,int? n=null,int? oldN=null) =>
        new(key,label,unit,current,previous,current.HasValue && previous.HasValue ? current-previous : null,
            current.HasValue && previous.HasValue && previous != 0 ? 100*(current-previous)/previous : previous == 0 && current == 0 ? 0 : null,n,oldN);

    public static ReportComparisonData? Build(ReportTicket[] visible,ReportFilter f,DateTime asOf)
    {
        if (f.Comparison == "none") return null;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(asOf,DateTimeKind.Utc),ReportMath.Zone));
        (DateOnly From,DateOnly To,string Rule) prior;
        try { prior = Previous(f,today); }
        catch (ArgumentOutOfRangeException) { return new(f.Comparison,f.From,f.To,null,null,"Период недоступен","Выберите даты сравнения вручную.","Предыдущий период выходит за поддерживаемый диапазон дат.",[]); }
        var current = visible.Where(f.Matches).ToArray();
        var oldFilter = f with {From=prior.From,To=prior.To,Snapshot=null,Comparison="none"};
        var old = visible.Where(oldFilter.Matches).ToArray();
        var metrics = new List<ReportComparisonMetric> {
            Metric("total","Заявки в выборке","count",current.Length,old.Length),
            Metric("closed","Закрыты сейчас (в выборке)","count",current.Count(t=>t.Status==6),old.Count(t=>t.Status==6))
        };
        void Time(string key,string label,Func<ReportTicket,double?> value,Func<IEnumerable<double?>,double?> aggregate) {
            var a=current.Select(value).ToArray(); var b=old.Select(value).ToArray();
            metrics.Add(Metric(key,label,"seconds",aggregate(a),aggregate(b),a.Count(x=>x.HasValue),b.Count(x=>x.HasValue)));
        }
        Time("meanSeconds","Среднее исполнения",t=>t.ExecutionSeconds,ReportMath.Mean);
        Time("medianSeconds","Медиана исполнения",t=>t.ExecutionSeconds,x=>ReportMath.Percentile(x,.5));
        Time("p90Seconds","P90 исполнения",t=>t.ExecutionSeconds,x=>ReportMath.Percentile(x,.9));
        Time("reactionSeconds","Среднее время реакции на заявку",t=>t.AcceptanceSeconds,ReportMath.Mean);
        var scores=current.Select(t=>t.CurrentScore).ToArray();var oldScores=old.Select(t=>t.CurrentScore).ToArray();
        metrics.Add(Metric("meanScore","Оценка результата","score",ReportMath.Mean(scores),ReportMath.Mean(oldScores),scores.Count(x=>x.HasValue),oldScores.Count(x=>x.HasValue)));
        metrics.Add(Metric("rated","Количество оценок","count",scores.Count(x=>x.HasValue),oldScores.Count(x=>x.HasValue)));
        Time("effortSeconds","Трудозатраты",t=>t.EffortSeconds,x=>x.Any(v=>v.HasValue)?x.Sum(v=>v??0):null);
        var warnings = new List<string>();
        var earliest = visible.Where(t=>!t.Deleted).Select(t=>t.CreatedAt).Min();
        if (earliest.HasValue && prior.From < DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(earliest.Value,ReportMath.Zone)))
            warnings.Add("Период сравнения начинается раньше самой ранней даты создания в доступных данных; история за него может быть неполной.");
        if (f.To.DayNumber-f.From.DayNumber != prior.To.DayNumber-prior.From.DayNumber)
            warnings.Add("Длительность периодов различается из-за календаря или выбранных дат; значения не пересчитываются на день.");
        if (f.To > today || prior.To > today) warnings.Add("Один из периодов ещё не завершён; будущие события не прогнозируются.");
        if (f.To == today || prior.To == today) warnings.Add("Текущий день ещё не завершён. Сравнение идёт по календарным датам без почасового выравнивания.");
        if (f.Start < oldFilter.End && oldFilter.Start < f.End) warnings.Add("Выбранные периоды пересекаются: одна заявка может входить в обе выборки.");
        return new(f.Comparison,f.From,f.To,prior.From,prior.To,prior.Rule,
            "Оба периода используют одинаковые фильтры, права и один срез данных. Отбор — по выбранному событию даты. «Закрыты сейчас» отражает текущий статус заявок каждой выборки, а не исторический остаток. Для длительностей и оценок указан размер выборки; отсутствие данных не равно нулю. Изменение оценки — в баллах.",
            warnings.Count==0?null:string.Join(" ",warnings),metrics.ToArray());
    }
}
