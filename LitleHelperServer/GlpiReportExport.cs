using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using PdfSharpCore.Drawing;
using PdfSharpCore.Fonts;
using PdfSharpCore.Pdf;

namespace LitleHelperServer;

public static class GlpiReportExport
{
    static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static string Time(double? s) => s == null ? "Нет данных" : s < 60 ? s.Value.ToString("0.#", Ru) + " с" : s < 3600 ? (s.Value / 60).ToString("0.#", Ru) + " мин" : (s.Value / 3600).ToString("0.00", Ru) + " ч";
    static string Date(DateTime? d) => d.HasValue ? TimeZoneInfo.ConvertTimeFromUtc(d.Value, ReportMath.Zone).ToString("dd.MM.yyyy HH:mm", Ru) : "Нет данных";
    static string Basis(string b) => b switch { "solved" => "решения", "closed" => "закрытия", "answered" => "оценки", _ => "создания" };
    static string Status(int n) => n switch { 1 => "Новая", 2 => "В работе", 3 => "Запланирована", 4 => "Ожидание", 5 => "Решена", 6 => "Закрыта", _ => "Неизвестно" };
    static double? Number(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
    static string Text(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind != JsonValueKind.Null ? v.ToString() : "";
    static string Score(double? v) => v.HasValue ? v.Value.ToString("0.00", Ru) + " / 5" : "Нет оценок";
    static string Clean(string s) => new(s.Where(XmlConvert.IsXmlChar).Take(32767).ToArray());
    static string Filters(ReportFilter f) => $"Филиал: {f.BranchId?.ToString() ?? "все"}; специалист: {f.Technician?.ToString() ?? "все"}; заявитель: {f.Requester?.ToString() ?? "все"}; статус: {(f.Status.HasValue ? Status(f.Status.Value) : "все")}; поиск: {(f.Search.Length == 0 ? "не задан" : f.Search)}" + (f.Reaction.HasValue ? "; реакция: " + GlpiReportReaction.Ranges[f.Reaction.Value] : "");
    const string Method = "Исполнение: принята → закрыта; жизнь: создана → закрыта. Календарное время, включая ожидание. Нет данных ≠ ноль. Оценки относятся к текущему закрытию. При менее 5 ответах выборка мала.";
    record Metric(string Label, object? Value, int Style, string Display, string Note);
    record Section(string Title, string[] Headers, object?[][] Rows);
    record ChartSeries(string Name, double[] Values, string Color);
    record Chart(string Title, string[] Categories, ChartSeries[] Series, bool Horizontal = false);
    sealed class Model(JsonElement data, ReportTicket[] tickets, ReportFilter filter)
    {
        public DateTime Generated { get; } = DateTime.UtcNow;
        public JsonElement Summary { get; } = JsonSerializer.SerializeToElement(ReportMath.Summary(tickets), Json);
        public string Period => $"Период: {filter.From:dd.MM.yyyy} - {filter.To:dd.MM.yyyy}; отбор по дате {Basis(filter.Basis)}; Asia/Qyzylorda";
        public string Snapshot => "Срез данных: " + (data.TryGetProperty("syncedAt", out var s) && s.ValueKind == JsonValueKind.String && s.TryGetDateTime(out var d) ? Date(d) : "Нет данных");
        public Metric[] Metrics => [
            new("Заявки в выборке", tickets.Length, 9, tickets.Length.ToString(), "Соответствуют периоду и фильтрам"),
            new("Закрыты сейчас", Number(Summary,"closed"), 9, Text(Summary,"closed"), "Статус «Закрыта» на момент среза"),
            new("Среднее исполнения", Number(Summary,"meanSeconds") / 86400, 10, Time(Number(Summary,"meanSeconds")), "Наблюдений: " + Text(Summary,"executionSamples")),
            new("Медиана / P90", Time(Number(Summary,"medianSeconds")) + " / " + Time(Number(Summary,"p90Seconds")), 9, Time(Number(Summary,"medianSeconds")) + " / " + Time(Number(Summary,"p90Seconds")), "Календарное время исполнения"),
            new("Оценка результата", Score(Number(Summary,"meanScore")), 9, Score(Number(Summary,"meanScore")), "Ответов: " + Text(Summary,"rated")),
            new("Трудозатраты", Number(Summary,"effortSeconds") / 86400, 10, Time(Number(Summary,"effortSeconds")), "Заполнено: " + Text(Summary,"effortSamples"))];
        public Chart Timeline()
        {
            var points = data.TryGetProperty("timeline", out var v) ? v.EnumerateArray().OrderBy(p => Text(p,"day")).ToArray() : [];
            return new("Динамика обращений", points.Length == 0 ? ["Нет событий"] : points.Select(p => Text(p,"day")).ToArray(),
                new[] { ("created","Созданы","4169E1"), ("solved","Решены","D99524"), ("closed","Закрыты","249677") }.Select(s => new ChartSeries(s.Item2, points.Length == 0 ? [0] : points.Select(p => Number(p,s.Item1) ?? 0).ToArray(), s.Item3)).ToArray());
        }
        public Chart Ratings() => new("Оценки пользователей", ["5", "4", "3", "2", "1"], [new("Ответов", new[] {5,4,3,2,1}.Select(n => (double)tickets.Count(t => t.Status == 6 && t.CurrentScore.HasValue && Math.Floor(t.CurrentScore.Value + .5) == n)).ToArray(), "4169E1")], true);
        public Chart Reaction()
        {
            var buckets = data.TryGetProperty("reaction", out var r) ? r : JsonSerializer.SerializeToElement(GlpiReportReaction.Distribution(tickets), Json);
            return new("Время реакции на заявку", GlpiReportReaction.Labels, [new("Заявок", buckets.EnumerateArray().Select(b => Number(b,"count") ?? 0).ToArray(), "4169E1")]);
        }
        public string ReactionNote => GlpiReportReaction.Note + " Без корректных дат: " + (Number(data,"reactionMissing") ?? tickets.Count(t => t.AcceptanceSeconds == null)) + "." + (filter.Reaction.HasValue ? " Распределение показано до отбора по времени реакции; сводка и таблицы учитывают выбранный интервал." : "");
        public Section[] Sections() => [Surveys(), People("technicians", "Специалисты"), People("requesters", "Заявители"),
            new("Заявки", ["Заявка", "Заявитель", "Ответственный", "Статус", "Время исполнения", "Время жизни", "Работа", "Оценка"], tickets.Select(t => new object?[] {
                $"#{t.Id} · {t.Title}\nСоздана: {Date(t.CreatedAt)}", t.Requesters.Length == 0 ? "Не указан" : string.Join("; ", t.Requesters.Select(a => a.Name)), t.Responsible?.Name ?? "Не определён", Status(t.Status),
                Duration(t.ExecutionSeconds), Duration(t.LifetimeSeconds), Duration(t.EffortSeconds), t.Status == 6 ? (object?)t.CurrentScore ?? "Нет оценок" : "—" }).ToArray())];
        Section Surveys()
        {
            if (!data.TryGetProperty("surveyReport",out var report)) return new("Контроль обязательных оценок",["Состояние"],[]);
            var rows = report.GetProperty("rows").EnumerateArray().Where(i=>i.GetProperty("mandatory").GetBoolean()).Select(i=>new object?[] {
                "#"+Text(i,"ticketId")+" · "+Text(i,"title"),Text(i,"requester"),Text(i,"specialist"),SurveyLabel(Text(i,"state")),
                Text(i,"dueAt"),Text(i,"expiresAt"),Text(i,"error") }).ToArray();
            return new("Обязательные оценки: "+Text(report,"answered")+" из "+Text(report,"eligible")+"; просрочено: "+Text(report,"overdue")+"; срок истёк: "+Text(report,"expired"),
                ["Заявка","Заявитель","Исполнитель","Состояние","Срок ответа (UTC)","Оценить до (UTC)","Примечание"],rows);
        }
        static string SurveyLabel(string state) => state switch { "Pending"=>"Ожидает оценки", "Writing"=>"Ожидает записи GLPI", "Answered"=>"Оценена", "Expired"=>"Истекли 14 дней", "Blocked"=>"Ошибка записи", "Cancelled"=>"Отменена", "Exempt"=>"Исключена", _=>state };
        Section People(string key, string title) => new(title, ["Сотрудник", key == "technicians" ? "Участие / результат" : "Создано", "Закрыто", "Среднее исполнения", "Оценка", "Ответов"],
            data.GetProperty(key).EnumerateArray().Select(p => new object?[] {
                Text(p,"name") + "\n" + Text(p,"login") + " · " + Text(p,"profile"), key == "technicians" ? Text(p,"total") + " / " + Text(p,"responsible") : Number(p,"total"),
                Number(p,"closed"), Duration(Number(p,"meanSeconds")), Score(Number(p,"meanScore")) + "\n" + (Number(p,"rated") >= 5 ? "Достаточная выборка" : "Мало данных"),
                Text(p,"rated") + "\n" + (Number(p,"responsePercent")?.ToString("0", Ru) ?? "—") + "% от закрытых" }).ToArray());
    }
    record DurationValue(double Seconds);
    static object Duration(double? s) => s.HasValue ? new DurationValue(s.Value) : "Нет данных";
    static string Display(object? v) => v switch { null => "Нет данных", DurationValue d => Time(d.Seconds), double n => n.ToString("0.##", Ru), _ => v.ToString() ?? "" };
    static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    static readonly XNamespace P = "http://schemas.openxmlformats.org/package/2006/relationships";
    static readonly XNamespace C = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    static readonly XNamespace D = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    static string Col(int n) { string s = ""; do { s = (char)('A' + n % 26) + s; n = n / 26 - 1; } while(n >= 0); return s; }
    record Cell(object? Value, int Style = 0);
    sealed class Sheet(string name, double[] widths)
    {
        public string Name = name;
        public double[] Widths = widths;
        public SortedDictionary<int, Cell[]> Rows = new();
        public Dictionary<int, double> Heights = new();
        public List<string> Merges = new();
        public void Row(int row, int style, params object?[] values) => Rows[row] = values.Select(v => v is Cell c ? c : new Cell(v,style)).ToArray();
        public void Band(int row, string text, int style = 2) { Row(row,style,text); Merges.Add($"A{row}:H{row}"); Heights[row] = style == 1 ? 30 : 26; }
        public void TableRow(int row, int style, object?[] values)
        {
            if(values.Length != 6) { Row(row,style,values); return; }
            Row(row,style,values[0],"",values[1],values[2],values[3],values[4],"",values[5]);
            Merges.Add($"A{row}:B{row}"); Merges.Add($"F{row}:G{row}");
        }
        public XElement Xml(bool drawing = false)
        {
            var root = new XElement(S+"worksheet", new XAttribute(XNamespace.Xmlns+"r",R),new XElement(S+"sheetPr",new XElement(S+"pageSetUpPr",new XAttribute("fitToPage",1))),
                new XElement(S+"sheetViews",new XElement(S+"sheetView",new XAttribute("workbookViewId",0),new XAttribute("showGridLines",0))),
                new XElement(S+"cols", Widths.Select((w,i) => new XElement(S+"col",new XAttribute("min",i+1),new XAttribute("max",i+1),new XAttribute("width",w),new XAttribute("customWidth",1)))),
                new XElement(S+"sheetData",Rows.Select(r => new XElement(S+"row",new XAttribute("r",r.Key),new XAttribute("ht",Heights.GetValueOrDefault(r.Key,24)),new XAttribute("customHeight",1),r.Value.Select((c,i) => CellXml(c,Col(i)+r.Key))))));
            if(Merges.Count > 0) root.Add(new XElement(S+"mergeCells",new XAttribute("count",Merges.Count),Merges.Select(m => new XElement(S+"mergeCell",new XAttribute("ref",m)))));
            root.Add(new XElement(S+"printOptions",new XAttribute("horizontalCentered",1)),new XElement(S+"pageMargins",new XAttribute("left",.25),new XAttribute("right",.25),new XAttribute("top",.4),new XAttribute("bottom",.4),new XAttribute("header",.15),new XAttribute("footer",.15)),new XElement(S+"pageSetup",new XAttribute("paperSize",8),new XAttribute("orientation","landscape"),new XAttribute("fitToWidth",1),new XAttribute("fitToHeight",0)),new XElement(S+"headerFooter",new XElement(S+"oddFooter","&LСтатистика и оценки&R&P / &N")));
            if(drawing) root.Add(new XElement(S+"drawing",new XAttribute(R+"id","rId1")));
            return root;
        }
    }
    static XElement CellXml(Cell cell,string address)
    {
        object? v = cell.Value; int style = cell.Style;
        if(v is DurationValue d) { v = d.Seconds / 86400; style=6; }
        var c = new XElement(S+"c",new XAttribute("r",address),new XAttribute("s",style));
        if(v == null) v="Нет данных";
        if(v is double or int or long or float or decimal) c.Add(new XElement(S+"v",Convert.ToString(v,CultureInfo.InvariantCulture)));
        else c.Add(new XAttribute("t","inlineStr"),new XElement(S+"is",new XElement(S+"t",new XAttribute(XNamespace.Xml+"space","preserve"),Clean(v.ToString()!))));
        return c;
    }
    static XElement Styles()
    {
        XElement Font(int size,bool bold=false,string color="203751") => new(S+"font",new XElement(S+"sz",new XAttribute("val",size)),new XElement(S+"name",new XAttribute("val","Arial")),new XElement(S+"color",new XAttribute("rgb","FF"+color)),bold?new XElement(S+"b"):null);
        XElement Fill(string color) => new(S+"fill",new XElement(S+"patternFill",new XAttribute("patternType","solid"),new XElement(S+"fgColor",new XAttribute("rgb","FF"+color)),new XElement(S+"bgColor",new XAttribute("indexed",64))));
        XElement Xf(int font=0,int fill=0,int number=0) => new(S+"xf",new XAttribute("numFmtId",number),new XAttribute("fontId",font),new XAttribute("fillId",fill),new XAttribute("borderId",0),new XAttribute("xfId",0),new XAttribute("applyAlignment",1),new XAttribute("applyNumberFormat",1),new XElement(S+"alignment",new XAttribute("vertical","center"),new XAttribute("wrapText",1)));
        return new(S+"styleSheet",new XElement(S+"numFmts",new XAttribute("count",1),new XElement(S+"numFmt",new XAttribute("numFmtId",164),new XAttribute("formatCode","[h]:mm:ss"))),
            new XElement(S+"fonts",new XAttribute("count",5),Font(10),Font(16,true),Font(11,true),Font(10,true,"FFFFFF"),Font(9,false,"63758A")),
            new XElement(S+"fills",new XAttribute("count",4),new XElement(S+"fill",new XElement(S+"patternFill",new XAttribute("patternType","none"))),new XElement(S+"fill",new XElement(S+"patternFill",new XAttribute("patternType","gray125"))),Fill("E9EFFB"),Fill("244A78")),
            new XElement(S+"borders",new XAttribute("count",1),new XElement(S+"border",new XElement(S+"left"),new XElement(S+"right"),new XElement(S+"top"),new XElement(S+"bottom"),new XElement(S+"diagonal"))),
            new XElement(S+"cellStyleXfs",new XAttribute("count",1),Xf()),new XElement(S+"cellXfs",new XAttribute("count",11),Xf(),Xf(1),Xf(2,2),Xf(3,3),Xf(0,2),Xf(number:2),Xf(number:164),Xf(number:10),Xf(4),Xf(1,2),Xf(1,2,164)),new XElement(S+"cellStyles",new XAttribute("count",1),new XElement(S+"cellStyle",new XAttribute("name","Normal"),new XAttribute("xfId",0),new XAttribute("builtinId",0))));
    }
    public static byte[] Xlsx(JsonElement data,ReportTicket[] rows,ReportFilter filter)
    {
        var m=new Model(data,rows,filter); var report=new Sheet("Отчёт",[40,30,30,22,26,26,23,22]);
        report.Band(2,"Статистика и оценки",1); report.Band(3,"Сформирован: "+Date(m.Generated)+" · Asia/Qyzylorda",8); report.Band(4,m.Period,8); report.Band(5,Filters(filter),8); report.Heights[5]=36; report.Band(6,m.Snapshot,8);
        var starts=new[]{0,3,6}; var ends=new[]{2,5,7};
        for(int i=0;i<6;i++) { var metric=m.Metrics[i]; int r=i<3?8:12,c=starts[i%3],end=ends[i%3];
            foreach(var (offset,v,style) in new[]{(0,(object?)metric.Label,2),(1,metric.Value??"Нет данных",metric.Style),(2,(object?)metric.Note,8)}) {
                var cells=report.Rows.GetValueOrDefault(r+offset,new Cell[8]); for(int j=0;j<cells.Length;j++) cells[j]??=new Cell(""); cells[c]=new(v,style); report.Rows[r+offset]=cells;
                report.Merges.Add($"{Col(c)}{r+offset}:{Col(end)}{r+offset}"); report.Heights[r+offset]=offset==1?36:24;
            }
        }
        report.Row(16,2,"Динамика обращений",null,null,null,"Оценки пользователей");report.Merges.Add("A16:D16");report.Merges.Add("E16:H16");
        for(int r=17;r<=32;r++) {report.Row(r,0,"");report.Heights[r]=22;}
        report.Band(34,"Время реакции на заявку");
        for(int r=35;r<=46;r++) {report.Row(r,0,"");report.Heights[r]=22;}
        report.Band(48,m.ReactionNote,8);report.Heights[48]=42;
        report.Band(50,Method,8);report.Heights[50]=42;
        int next=52;
        foreach(var section in m.Sections()) {
            report.Band(next++,section.Title);report.TableRow(next,3,section.Headers.Cast<object?>().ToArray());report.Heights[next++]=32;
            if(section.Rows.Length==0)report.Band(next++,"Нет данных в выбранной выборке",8);
            foreach(var row in section.Rows){report.TableRow(next,0,row); var widths=row.Length==6?new[]{70d,30,22,26,49,22}:report.Widths; double lines=row.Select((v,i)=>Display(v).Split('\n').Sum(t=>Math.Max(1,Math.Ceiling(t.Length/(widths[i]*.6))))).Max();report.Heights[next++]=Math.Min(409,Math.Max(40,lines*16+12));}
            next+=2;
        }
        var charts=new[]{m.Timeline(),m.Ratings(),m.Reaction()};var chartData=new Sheet("Данные графиков",[24,22,22,22,5,20,22,5,24,22]);chartData.Row(1,3,"Дата","Созданы","Решены","Закрыты",null,"Оценка","Ответов",null,"Время реакции","Заявок");
        for(int i=0;i<Math.Max(charts[0].Categories.Length,6);i++)chartData.Row(i+2,0,i<charts[0].Categories.Length?charts[0].Categories[i]:"",i<charts[0].Categories.Length?charts[0].Series[0].Values[i]:null,i<charts[0].Categories.Length?charts[0].Series[1].Values[i]:null,i<charts[0].Categories.Length?charts[0].Series[2].Values[i]:null,"",i<5?charts[1].Categories[i]:"",i<5?charts[1].Series[0].Values[i]:null,"",i<6?charts[2].Categories[i]:"",i<6?charts[2].Series[0].Values[i]:null);
        var sheets=new[]{report,chartData};using var stream=new MemoryStream();using(var zip=new ZipArchive(stream,ZipArchiveMode.Create,true)) {
            void Write(string path,XElement xml){using var w=XmlWriter.Create(zip.CreateEntry(path).Open(),new XmlWriterSettings{Encoding=new UTF8Encoding(false),CloseOutput=true});xml.Save(w);}
            XNamespace t="http://schemas.openxmlformats.org/package/2006/content-types";
            var types=new XElement(t+"Types",new XElement(t+"Default",new XAttribute("Extension","rels"),new XAttribute("ContentType","application/vnd.openxmlformats-package.relationships+xml")),new XElement(t+"Default",new XAttribute("Extension","xml"),new XAttribute("ContentType","application/xml")));
            void Type(string part,string kind)=>types.Add(new XElement(t+"Override",new XAttribute("PartName",part),new XAttribute("ContentType","application/vnd.openxmlformats-officedocument."+kind+"+xml")));
            Type("/xl/workbook.xml","spreadsheetml.sheet.main");Type("/xl/styles.xml","spreadsheetml.styles");for(int i=1;i<=2;i++)Type($"/xl/worksheets/sheet{i}.xml","spreadsheetml.worksheet");for(int i=1;i<=3;i++)Type($"/xl/charts/chart{i}.xml","drawingml.chart");Type("/xl/drawings/drawing1.xml","drawing");Write("[Content_Types].xml",types);
            XElement Rel(string id,string kind,string target)=>new(P+"Relationship",new XAttribute("Id",id),new XAttribute("Type",R.NamespaceName+"/"+kind),new XAttribute("Target",target));
            Write("_rels/.rels",new(P+"Relationships",Rel("rId1","officeDocument","xl/workbook.xml")));
            Write("xl/_rels/workbook.xml.rels",new(P+"Relationships",Rel("rId1","worksheet","worksheets/sheet1.xml"),Rel("rId2","worksheet","worksheets/sheet2.xml"),Rel("rId3","styles","styles.xml")));
            Write("xl/workbook.xml",new(S+"workbook",new XAttribute(XNamespace.Xmlns+"r",R),new XElement(S+"sheets",sheets.Select((s,i)=>new XElement(S+"sheet",new XAttribute("name",s.Name),new XAttribute("sheetId",i+1),new XAttribute(R+"id","rId"+(i+1)))))));
            Write("xl/styles.xml",Styles());for(int i=0;i<2;i++)Write($"xl/worksheets/sheet{i+1}.xml",sheets[i].Xml(i==0));
            Write("xl/worksheets/_rels/sheet1.xml.rels",new(P+"Relationships",Rel("rId1","drawing","../drawings/drawing1.xml")));
            Write("xl/drawings/_rels/drawing1.xml.rels",new(P+"Relationships",Enumerable.Range(1,3).Select(i=>Rel("rId"+i,"chart",$"../charts/chart{i}.xml"))));
            Write("xl/drawings/drawing1.xml",new(D+"wsDr",new XAttribute(XNamespace.Xmlns+"a",A),Enumerable.Range(0,3).Select(i=>new XElement(D+"twoCellAnchor",Marker("from",i==2?0:i*4,i==2?34:16),Marker("to",i==2?8:(i+1)*4,i==2?46:32),new XElement(D+"graphicFrame",new XAttribute("macro",""),new XElement(D+"nvGraphicFramePr",new XElement(D+"cNvPr",new XAttribute("id",i+1),new XAttribute("name",charts[i].Title)),new XElement(D+"cNvGraphicFramePr")),new XElement(D+"xfrm",new XElement(A+"off",new XAttribute("x",0),new XAttribute("y",0)),new XElement(A+"ext",new XAttribute("cx",0),new XAttribute("cy",0))),new XElement(A+"graphic",new XElement(A+"graphicData",new XAttribute("uri",C),new XElement(C+"chart",new XAttribute(XNamespace.Xmlns+"r",R),new XAttribute(R+"id","rId"+(i+1)))))),new XElement(D+"clientData")))));
            for(int i=0;i<3;i++)Write($"xl/charts/chart{i+1}.xml",ChartXml(charts[i],i));
        }return stream.ToArray();
    }
    static XElement Marker(string name,int col,int row)=>new(D+name,new XElement(D+"col",col),new XElement(D+"colOff",0),new XElement(D+"row",row),new XElement(D+"rowOff",0));
    static XElement ChartXml(Chart chart,int chartIndex)
    {
        XElement V(string n,object v)=>new(C+n,new XAttribute("val",v));
        var series=chart.Series.Select((s,i)=>new XElement(C+"ser",V("idx",i),V("order",i),new XElement(C+"tx",new XElement(C+"v",s.Name)),new XElement(C+"spPr",new XElement(A+"solidFill",new XElement(A+"srgbClr",new XAttribute("val",s.Color)))),
            new XElement(C+"cat",new XElement(C+"strRef",new XElement(C+"f",$"'Данные графиков'!${(chartIndex==0?"A":chartIndex==1?"F":"I")}$2:${(chartIndex==0?"A":chartIndex==1?"F":"I")}${chart.Categories.Length+1}"),new XElement(C+"strCache",V("ptCount",chart.Categories.Length),chart.Categories.Select((s,j)=>new XElement(C+"pt",new XAttribute("idx",j),new XElement(C+"v",s)))))),
            new XElement(C+"val",new XElement(C+"numRef",new XElement(C+"f",$"'Данные графиков'!${(chartIndex==0?Col(i+1):chartIndex==1?"G":"J")}$2:${(chartIndex==0?Col(i+1):chartIndex==1?"G":"J")}${s.Values.Length+1}"),new XElement(C+"numCache",new XElement(C+"formatCode","0"),V("ptCount",s.Values.Length),s.Values.Select((n,j)=>new XElement(C+"pt",new XAttribute("idx",j),new XElement(C+"v",n.ToString(CultureInfo.InvariantCulture)))))))));
        return new(C+"chartSpace",new XAttribute(XNamespace.Xmlns+"a",A),V("lang","ru-RU"),new XElement(C+"chart",new XElement(C+"autoTitleDeleted",new XAttribute("val",1)),new XElement(C+"plotArea",new XElement(C+"layout"),new XElement(C+"barChart",V("barDir",chart.Horizontal?"bar":"col"),V("grouping","clustered"),V("varyColors",0),series,V("gapWidth",80),V("axId",1),V("axId",2)),new XElement(C+"catAx",V("axId",1),new XElement(C+"scaling",V("orientation",chart.Horizontal?"maxMin":"minMax")),V("axPos",chart.Horizontal?"l":"b"),V("tickLblPos","nextTo"),V("crossAx",2),V("crosses","autoZero")),new XElement(C+"valAx",V("axId",2),new XElement(C+"scaling",V("orientation","minMax"),V("min",0)),V("axPos",chart.Horizontal?"b":"l"),new XElement(C+"majorGridlines"),new XElement(C+"numFmt",new XAttribute("formatCode","0"),new XAttribute("sourceLinked",0)),V("tickLblPos","nextTo"),V("crossAx",1),V("crosses","autoZero"))),new XElement(C+"legend",V("legendPos","b")),V("plotVisOnly",1)));
    }
    static readonly object FontGate=new(); static bool fontsReady;
    public static byte[] Pdf(JsonElement data,ReportTicket[] rows,ReportFilter filter)
    {
        lock(FontGate){if(!fontsReady){GlobalFontSettings.FontResolver=new ReportFontResolver();fontsReady=true;}}
        var m=new Model(data,rows,filter);using var doc=new PdfDocument();doc.Info.Title="Статистика и оценки";doc.Info.CreationDate=m.Generated;
        using(var layout=new PdfLayout(doc,m,filter)){layout.Summary();foreach(var section in m.Sections())layout.Table(section);}
        var footerFont=new XFont("ReportSans",8);for(int i=0;i<doc.Pages.Count;i++){using var g=XGraphics.FromPdfPage(doc.Pages[i],XGraphicsPdfPageOptions.Append);g.DrawString($"Сформирован: {Date(m.Generated)} · Asia/Qyzylorda",footerFont,XBrushes.Gray,28,575);g.DrawString($"{i+1} / {doc.Pages.Count}",footerFont,XBrushes.Gray,760,575);}
        using var stream=new MemoryStream();doc.Save(stream,false);return stream.ToArray();
    }
    sealed class PdfLayout:IDisposable
    {
        readonly PdfDocument doc;readonly Model model;readonly ReportFilter filter;XGraphics g=null!;double y;
        readonly XFont body=new("ReportSans",8.5),small=new("ReportSans",7.5),bold=new("ReportSans",9,XFontStyle.Bold),heading=new("ReportSans",13,XFontStyle.Bold);
        static readonly XBrush Ink=new XSolidBrush(XColor.FromArgb(32,55,81)),Blue=new XSolidBrush(XColor.FromArgb(36,74,120)),Pale=new XSolidBrush(XColor.FromArgb(235,241,252));
        const double Left=28,Width=785,Bottom=548;
        public PdfLayout(PdfDocument d,Model m,ReportFilter f){doc=d;model=m;filter=f;Page();}
        void Page(){g?.Dispose();var page=doc.AddPage();page.Size=PdfSharpCore.PageSize.A4;page.Orientation=PdfSharpCore.PageOrientation.Landscape;g=XGraphics.FromPdfPage(page);g.DrawString("Статистика и оценки",heading,Ink,Left,30);g.DrawString(model.Period,small,Ink,Left,46);y=61;}
        void Ensure(double height){if(y+height>Bottom)Page();}
        List<string> Wrap(string text,XFont font,double width)
        {
            var lines=new List<string>();foreach(var paragraph in Clean(text).Replace("\r","").Split('\n')){string line="";foreach(var word0 in paragraph.Split(' ')){string word=word0;while(word.Length>0){string next=line.Length==0?word:line+" "+word;if(g.MeasureString(next,font).Width<=width){line=next;break;}if(line.Length>0){lines.Add(line);line="";continue;}int cut=word.Length;while(cut>1&&g.MeasureString(word[..cut],font).Width>width)cut--;lines.Add(word[..cut]);word=word[cut..];}}lines.Add(line);}return lines;
        }
        void Text(string text,XFont font,double x,double top,double width,XBrush? brush=null){var lines=Wrap(text,font,width);for(int i=0;i<lines.Count;i++)g.DrawString(lines[i],font,brush??Ink,x,top+font.Size+2+i*12);}
        void Note(string text){var lines=Wrap(text,small,Width);Ensure(lines.Count*12+8);Text(text,small,Left,y,Width);y+=lines.Count*12+8;}
        public void Summary()
        {
            Note("Сформирован: "+Date(model.Generated)+" · Asia/Qyzylorda. "+model.Snapshot);Note(Filters(filter));
            var metrics=model.Metrics;for(int i=0;i<6;i++){int col=i%3;if(col==0)Ensure(74);double x=Left+col*265;g.DrawRectangle(Pale,x,y,255,66);Text(metrics[i].Label,bold,x+10,y+6,235);Text(metrics[i].Display,new XFont("ReportSans",14,XFontStyle.Bold),x+10,y+25,235);Text(metrics[i].Note,small,x+10,y+47,235);if(col==2)y+=74;}
            Ensure(180);DrawChart(model.Timeline(),Left,y,385,165);DrawChart(model.Ratings(),Left+400,y,385,165);y+=176;
            Ensure(220);DrawChart(model.Reaction(),Left,y,Width,175);y+=187;Note(model.ReactionNote);Note(Method);
        }
        void DrawChart(Chart chart,double x,double top,double width,double height)
        {
            Text(chart.Title,bold,x,top,width);double plotTop=top+27,plotHeight=height-56,plotLeft=x+28,plotWidth=width-38;
            double max=Math.Max(1,chart.Series.SelectMany(s=>s.Values).DefaultIfEmpty().Max());
            if(chart.Horizontal){for(int i=0;i<chart.Categories.Length;i++){double yy=plotTop+i*22;g.DrawString(chart.Categories[i]+" ★",small,Ink,x,yy+10);double w=chart.Series[0].Values[i]/max*(plotWidth-30);if(w>0)g.DrawRectangle(new XSolidBrush(XColor.FromArgb(65,105,225)),plotLeft+5,yy,w,13);g.DrawString(chart.Series[0].Values[i].ToString("0"),small,Ink,plotLeft+10+w,yy+10);}}
            else {
                // Bin only for PDF readability; sums retain every day and event type.
                int group=Math.Max(1,(int)Math.Ceiling(chart.Categories.Length/28d));int count=(int)Math.Ceiling(chart.Categories.Length/(double)group);
                var values=chart.Series.Select(s=>Enumerable.Range(0,count).Select(i=>s.Values.Skip(i*group).Take(group).Sum()).ToArray()).ToArray();max=Math.Max(2,Math.Ceiling(values.SelectMany(v=>v).DefaultIfEmpty().Max()/2)*2);
                for(int k=0;k<=2;k++){double yy=plotTop+plotHeight-k*plotHeight/2;g.DrawLine(new XPen(XColor.FromArgb(215,224,235)),plotLeft,yy,x+width-5,yy);g.DrawString((max*k/2).ToString("0.#",Ru),small,Ink,x,yy+3);}
                double slot=plotWidth/count,bw=Math.Max(.3,slot/(chart.Series.Length+1));
                for(int i=0;i<count;i++){for(int j=0;j<chart.Series.Length;j++){double h=values[j][i]/max*plotHeight;g.DrawRectangle(new XSolidBrush(XColor.FromArgb(Convert.ToInt32(chart.Series[j].Color[..2],16),Convert.ToInt32(chart.Series[j].Color.Substring(2,2),16),Convert.ToInt32(chart.Series[j].Color.Substring(4,2),16))),plotLeft+i*slot+j*bw,plotTop+plotHeight-h,bw,h);}if(chart.Series.Length==1||i==0||i==count-1||i==count/2)Text(DateOnly.TryParseExact(chart.Categories[i*group],"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var day)?day.ToString("dd.MM",Ru):chart.Categories[i*group],small,Math.Min(plotLeft+i*slot,x+width-55),plotTop+plotHeight+2,70);}
                Text(chart.Series.Length==1?"Количество заявок":"Созданы / Решены / Закрыты"+(group>1?$" · суммы по {group} дат":""),small,x,top+height-12,width);
            }
        }
        public void Table(Section section)
        {
            double titleHeight=Wrap(section.Title,heading,Width).Count*12+13;
            Ensure(titleHeight+53);Text(section.Title,heading,Left,y,Width);y+=titleHeight;
            double[] shares=section.Headers.Length switch { 8=>[.25,.15,.15,.09,.1,.1,.08,.08],7=>[.23,.13,.13,.11,.12,.12,.16],6=>[.31,.14,.1,.15,.18,.12],_=>Enumerable.Repeat(1d/section.Headers.Length,section.Headers.Length).ToArray() };var widths=shares.Select(s=>s*Width).ToArray();
            void Header(){double x=Left;double h=section.Headers.Select((s,i)=>Wrap(s,bold,widths[i]-12).Count*12+12).Max();g.DrawRectangle(Blue,Left,y,Width,h);for(int i=0;i<widths.Length;i++){Text(section.Headers[i],bold,x+6,y+4,widths[i]-12,XBrushes.White);x+=widths[i];}y+=h;}
            void Continue(){Page();Text(section.Title+" · продолжение",heading,Left,y,Width);y+=24;Header();}
            Header();if(section.Rows.Length==0){Note("Нет данных в выбранной выборке");y+=12;return;}
            int index=0;foreach(var row in section.Rows){var lines=row.Select((v,i)=>Wrap(Display(v),body,widths[i]-12)).ToArray();int lineCount=lines.Max(l=>l.Count),offset=0;
                while(offset<lineCount){int available=(int)Math.Floor((Bottom-y-10)/12);if(available<1){Continue();available=(int)Math.Floor((Bottom-y-10)/12);}int take=Math.Min(lineCount-offset,available);double h=take*12+10;
                    if(index%2==0)g.DrawRectangle(Pale,Left,y,Width,h);double x=Left;for(int i=0;i<widths.Length;i++){for(int j=0;j<take&&offset+j<lines[i].Count;j++)g.DrawString(lines[i][offset+j],body,Ink,x+6,y+12+j*12);x+=widths[i];}g.DrawLine(new XPen(XColor.FromArgb(221,229,239)),Left,y+h,Left+Width,y+h);y+=h;offset+=take;if(offset<lineCount)Continue();
                }index++;
            }y+=22;
        }
        public void Dispose()=>g.Dispose();
    }
    sealed class ReportFontResolver:IFontResolver
    {
        public string DefaultFontName=>"ReportSans";
        public byte[] GetFont(string name)=>File.ReadAllBytes(Path.Combine(Directory.Exists(Path.Combine(AppContext.BaseDirectory,"ReportFonts"))?Path.Combine(AppContext.BaseDirectory,"ReportFonts"):"/usr/share/fonts/truetype/dejavu",name=="ReportSansBold"?"DejaVuSans-Bold.ttf":"DejaVuSans.ttf"));
        public FontResolverInfo ResolveTypeface(string family,bool bold,bool italic)=>new(bold?"ReportSansBold":"ReportSans");
    }
}
