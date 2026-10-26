using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace LitleHelperServer;

public class LiveKitService
{
    private readonly IConfiguration _config;

    public LiveKitService(IConfiguration config)
    {
        _config = config;
    }

    public string Url => _config["LiveKit:Url"] ?? "ws://172.16.16.61:7880";
    public string ApiKey => _config["LiveKit:ApiKey"] ?? "devkey";
    public string ApiSecret => _config["LiveKit:ApiSecret"] ?? "secret_development_livekit_key_32bytes_min";
    public bool IsEnabled => !string.IsNullOrWhiteSpace(Url) && !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(ApiSecret);

    public string GenerateToken(string room, PanelUser user, bool isHost = false)
    {
        var now = DateTimeOffset.UtcNow;
        var payload = new JwtPayload
        {
            { "iss", ApiKey },
            { "sub", user.Username },
            { "name", user.FullName },
            { "nbf", now.AddSeconds(-5).ToUnixTimeSeconds() },
            { "exp", now.AddHours(6).ToUnixTimeSeconds() },
            { "video", new Dictionary<string, object>
                {
                    { "room", room },
                    { "roomJoin", true },
                    { "canPublish", true },
                    { "canSubscribe", true },
                    { "canPublishData", true },
                    { "roomAdmin", isHost },
                    { "roomRecord", isHost }
                }
            },
            { "metadata", JsonSerializer.Serialize(new { userId = user.Id, fullName = user.FullName, isHost }) }
        };

        var secretBytes = Encoding.UTF8.GetBytes(ApiSecret);
        if (secretBytes.Length < 32)
        {
            Array.Resize(ref secretBytes, 32);
        }
        var key = new SymmetricSecurityKey(secretBytes);
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var header = new JwtHeader(creds);
        var secToken = new JwtSecurityToken(header, payload);
        return new JwtSecurityTokenHandler().WriteToken(secToken);
    }
}
