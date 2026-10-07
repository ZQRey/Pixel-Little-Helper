using System.Text.Json;
namespace LitleHelperServer;
public partial class GlpiService
{
    internal string ImportSource => settings.BaseUrl.TrimEnd('/').ToLowerInvariant();
    internal async Task<int[]> ImportIdsAsync(long? after, CancellationToken token)
    {
        var collected=new List<int>();int? previous=null;
        for(int offset=0;offset<5000;offset+=100)
        {
            var json=await CallAsync(HttpMethod.Get,$"search/Ticket?forcedisplay[0]=2&sort=2&order=DESC&range={offset}-{(after==null?offset:offset+99)}",null,token);
            if (!json.TryGetProperty("data",out var rows) || rows.ValueKind!=JsonValueKind.Array) throw new InvalidOperationException("GLPI не вернул список заявок. Проверьте права поиска API.");
            var ids=rows.EnumerateArray().Select(r=>Number(r.GetProperty("2"))).ToArray();
            foreach(int id in ids)
            {
                if(id<=0 || previous!=null&&id>=previous)throw new InvalidOperationException("GLPI не подтвердил порядок номеров заявок.");previous=id;
                if(after==null)return [id];if(id<=after)return collected.Order().Take(50).ToArray();collected.Add(id);
            }
            if(ids.Length<100)return collected.Order().Take(50).ToArray();
        }
        throw new InvalidOperationException("Более 5000 необработанных заявок GLPI. Требуется проверка очереди.");
    }
    public async Task<List<GlpiLocation>> ImportLocationsAsync(CancellationToken token)
    {
        var locations=new List<GlpiLocation>();
        for(int offset=0;offset<5000;offset+=100)
        {
            var rows=await CallAsync(HttpMethod.Get,$"Location?range={offset}-{offset+99}&get_hateoas=false",null,token);
            if(rows.ValueKind!=JsonValueKind.Array)throw new InvalidOperationException("GLPI не вернул местоположения.");
            foreach(var row in rows.EnumerateArray()) locations.Add(new(Number(row.GetProperty("id")),Text(row,"completename",Text(row,"name")),Text(row,"name"),row.TryGetProperty("locations_id",out var parent)?Number(parent):0));
            if(rows.GetArrayLength()<100)return locations;
        }
        throw new InvalidOperationException("В GLPI больше 5000 местоположений; уточните область API.");
    }
    internal async Task<string> ImportRequesterAsync(int id,CancellationToken token)
    {
        var rows=await CallAsync(HttpMethod.Get,$"Ticket/{id}/Ticket_User?range=0-999",null,token);
        foreach(var row in rows.EnumerateArray().Where(r=>Number(r.GetProperty("type"))==1))
        {
            int user=Number(row.GetProperty("users_id"));if(user==0)continue;
            var data=await CallAsync(HttpMethod.Get,"User/"+user,null,token);return Text(data,"name","GLPI #"+user);
        }
        return "Пользователь GLPI";
    }
    internal static string Text(JsonElement row,string key,string fallback="")=>row.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString()??fallback:fallback;
}
public record GlpiLocation(int Id,string FullName,string Name,int ParentId);
