namespace LitleHelperServer;

public static class ButtonTranslationService
{
    private static readonly Dictionary<string, (string kk, string en, string zh)> Dictionary = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Поиск файлов и папок"] = ("Файлдар мен бумаларды іздеу", "Search files and folders", "搜索文件和文件夹"),
        ["Написать программистам"] = ("Бағдарламашыларға жазу", "Contact IT support", "联系程序员"),
        ["Обращение в ИТ"] = ("АТ бөліміне жүгіну", "IT ticket request", "IT支持申请"),
        ["Чат с компаньоном"] = ("Серіктеспен сөйлесу", "Chat with companion", "与助手伙伴聊天"),
        ["Пиксельный компаньон"] = ("Пиксельді серіктес", "Pixel companion", "像素小伙伴"),
        ["Мой компьютер"] = ("Менің компьютерім", "My Computer", "我的电脑"),
        ["Калькулятор"] = ("Калькулятор", "Calculator", "计算器"),
        ["Папка документов"] = ("Құжаттар бумасы", "Documents folder", "文档文件夹"),
        ["Заявка на картридж"] = ("Картриджке өтінім", "Cartridge request", "硒鼓申请"),
        ["Техподдержка"] = ("Техникалық қолдау", "Tech support", "技术支持"),
        ["Справка"] = ("Анықтама", "Help", "帮助"),
        ["Настройки"] = ("Баптаулар", "Settings", "设置")
    };

    public static (Dictionary<string, string> titleTranslations, Dictionary<string, string> descriptionTranslations) Translate(
        string title,
        string? description = null,
        Dictionary<string, string>? existingTitle = null,
        Dictionary<string, string>? existingDesc = null)
    {
        var titles = existingTitle != null ? new Dictionary<string, string>(existingTitle, StringComparer.OrdinalIgnoreCase) : new(StringComparer.OrdinalIgnoreCase);
        var descs = existingDesc != null ? new Dictionary<string, string>(existingDesc, StringComparer.OrdinalIgnoreCase) : new(StringComparer.OrdinalIgnoreCase);

        // Ensure ru
        if (!titles.ContainsKey("ru") || string.IsNullOrWhiteSpace(titles["ru"])) titles["ru"] = title;
        if (!string.IsNullOrWhiteSpace(description) && (!descs.ContainsKey("ru") || string.IsNullOrWhiteSpace(descs["ru"]))) descs["ru"] = description;

        if (Dictionary.TryGetValue(title.Trim(), out var tMatch))
        {
            if (!titles.ContainsKey("kk") || string.IsNullOrWhiteSpace(titles["kk"])) titles["kk"] = tMatch.kk;
            if (!titles.ContainsKey("en") || string.IsNullOrWhiteSpace(titles["en"])) titles["en"] = tMatch.en;
            if (!titles.ContainsKey("zh") || string.IsNullOrWhiteSpace(titles["zh"])) titles["zh"] = tMatch.zh;
        }
        else
        {
            // Fallback default transliteration / translation defaults
            if (!titles.ContainsKey("kk") || string.IsNullOrWhiteSpace(titles["kk"])) titles["kk"] = title;
            if (!titles.ContainsKey("en") || string.IsNullOrWhiteSpace(titles["en"])) titles["en"] = title;
            if (!titles.ContainsKey("zh") || string.IsNullOrWhiteSpace(titles["zh"])) titles["zh"] = title;
        }

        if (!string.IsNullOrWhiteSpace(description))
        {
            if (Dictionary.TryGetValue(description.Trim(), out var dMatch))
            {
                if (!descs.ContainsKey("kk") || string.IsNullOrWhiteSpace(descs["kk"])) descs["kk"] = dMatch.kk;
                if (!descs.ContainsKey("en") || string.IsNullOrWhiteSpace(descs["en"])) descs["en"] = dMatch.en;
                if (!descs.ContainsKey("zh") || string.IsNullOrWhiteSpace(descs["zh"])) descs["zh"] = dMatch.zh;
            }
            else
            {
                if (!descs.ContainsKey("kk") || string.IsNullOrWhiteSpace(descs["kk"])) descs["kk"] = description;
                if (!descs.ContainsKey("en") || string.IsNullOrWhiteSpace(descs["en"])) descs["en"] = description;
                if (!descs.ContainsKey("zh") || string.IsNullOrWhiteSpace(descs["zh"])) descs["zh"] = description;
            }
        }

        return (titles, descs);
    }
}
