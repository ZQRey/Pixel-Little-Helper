namespace LitleHelperServer;
public static class EmergencyLanguages
{
    public static readonly string[] Codes=["CODE_RED","CODE_YELLOW","CODE_BLACK","CODE_PINK","CODE_ORANGE","CODE_WHITE","CODE_BLUE"];
    public static List<string> ForCode(EmergencyOptions options,string code)
    {
        var permitted=code is "CODE_WHITE" or "CODE_BLUE" or "SPECIALIST_CALL"?new[] {"ru","kk"}:new[] {"ru","kk","en","zh"};
        if(options.EmergencyCodeLanguages?.TryGetValue(code,out var languages)==true)
            return languages.Where(permitted.Contains).Distinct().ToList();
        return permitted.ToList();
    }
    public static List<string> AllowedForRole(EmergencyOptions options,string role) =>
        (options.AllowedCodes ?? Codes.Concat(["SPECIALIST_CALL"]).ToList())
        .Where(code => options.CodeLaunchRoles?.TryGetValue(code,out var roles)==true ? roles.Contains(role) :
            options.AllowedRoles==null || options.AllowedRoles.Contains(role)).ToList();
    public static void Validate(EmergencyOptions options)
    {
        if(options.AllowedCodes?.Any(c=>!Codes.Contains(c) && c!="SPECIALIST_CALL")==true) throw new ArgumentException("Неизвестный экстренный код.");
        foreach(var entry in options.CodeLaunchRoles??[])
            if(!Codes.Contains(entry.Key) || entry.Value.Any(role=>!Roles.All.Contains(role))) throw new ArgumentException("Проверьте роли запуска кода.");
        foreach(var entry in options.EmergencyCodeLanguages??[])
        {
            var valid=entry.Key is "CODE_WHITE" or "CODE_BLUE" or "SPECIALIST_CALL"?new[] {"ru","kk"}:new[] {"ru","kk","en","zh"};
            if(!Codes.Contains(entry.Key) || entry.Value.Count==0 || entry.Value.Any(l=>!valid.Contains(l)))
                throw new ArgumentException("Проверьте языки экстренного кода.");
        }
    }
}