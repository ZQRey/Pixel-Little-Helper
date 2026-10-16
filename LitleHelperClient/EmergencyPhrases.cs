namespace PixelHelper;
internal static class EmergencyPhrases
{
    internal static readonly Dictionary<string,string[]> Names = new()
    {
        ["CODE_RED"]=["Красный код","Қызыл код","Code Red","红色代码"],
        ["CODE_YELLOW"]=["Жёлтый код","Сары код","Code Yellow","黄色代码"],
        ["CODE_BLACK"]=["Чёрный код","Қара код","Code Black","黑色代码"],
        ["CODE_PINK"]=["Розовый код","Қызғылт код","Code Pink","粉色代码"],
        ["CODE_ORANGE"]=["Оранжевый код","Қызғылт сары код","Code Orange","橙色代码"],
        ["CODE_WHITE"]=["Белый код","Ақ код","Code White","白色代码"],
        ["CODE_BLUE"]=["Синий код","Көк код","Code Blue","蓝色代码"]
    };
    internal static List<string> Languages(EmergencyAlertNotice notice) =>
        (notice.Languages ?? (notice.Code is "CODE_WHITE" or "CODE_BLUE" ? ["ru","kk"] : ["ru","kk","en","zh"]))
        .Where(l=>l is "ru" or "kk" or "en" or "zh").Distinct().ToList();
    internal static string Text(EmergencyAlertNotice notice,string language)
    {
        int index=language switch {"kk"=>1,"en"=>2,"zh"=>3,_=>0};
        string title=Names.TryGetValue(notice.Code.ToUpperInvariant(),out var names)?names[index]:notice.Title;
        string room=notice.Cabinet??"";
        return language switch
        {
            "kk"=>$"Назар аударыңыз! {title}! {room} кабинеті.",
            "en"=>$"Attention! {title}! Room {room}.",
            "zh"=>$"请注意！{title}！房间 {room}。",
            _=>$"Внимание! {title}! Кабинет {room}."
        };
    }
}