using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace LitleHelperServer;

public static class Security
{
    public static string KeyHash(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    public static string Canonical(string value) => value.Trim().ToUpperInvariant();
    public static string Login(string value) => value.Trim().ToLowerInvariant();
    public static string Token(PanelUser user, IConfiguration config)
    {
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Role, user.Role), new Claim("version", user.SecurityVersion.ToString()) };
        var token = new JwtSecurityToken(config["Jwt:Issuer"], config["Jwt:Audience"], claims,
            expires: DateTime.UtcNow.AddMinutes(config.GetValue("Jwt:Minutes", 60)),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:SigningKey"]!)), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
    public static bool PasswordValid(string? value) => value is { Length: >= 8 and <= 72 } && Encoding.UTF8.GetByteCount(value) <= 72;
    public static bool MachineValid(string value) => value.Length is >= 1 and <= 63 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');
}
public class AgentAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, HelperDb db)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string key = Request.Headers["X-Client-Key"].ToString();
        string machine = Security.Canonical(Request.Headers["X-Machine-Name"].ToString());
        if (string.IsNullOrEmpty(key)) return AuthenticateResult.NoResult();
        if (!Security.MachineValid(machine) || key.Length > 200) return AuthenticateResult.Fail("Invalid agent credentials");
        var computer = await db.Computers.AsNoTracking().SingleOrDefaultAsync(x => x.MachineName == machine);
        if (computer == null || string.IsNullOrEmpty(computer.AgentKeyHash) ||
            !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(computer.AgentKeyHash), Encoding.ASCII.GetBytes(Security.KeyHash(key))))
            return AuthenticateResult.Fail("Invalid agent credentials");
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, machine), new Claim(ClaimTypes.Role, "Agent")], "Agent");
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }
}
