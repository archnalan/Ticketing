using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Ticketing.Api.Dtos;
using Ticketing.Api.Models;

namespace Ticketing.Api.Services;

public interface IAuthService
{
    Task<LoginResponse?> LoginAsync(string email, string password, CancellationToken ct = default);
}

public class JwtOptions
{
    public string Issuer { get; set; } = "ticketing";
    public string Audience { get; set; } = "ticketing";
    public string Key { get; set; } = "";
    public int LifetimeHours { get; set; } = 168; // 7 days
}

public class AuthService : IAuthService
{
    private readonly UserManager<TicketingUser> _users;
    private readonly SignInManager<TicketingUser> _signIn;
    private readonly JwtOptions _jwt;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        UserManager<TicketingUser> users,
        SignInManager<TicketingUser> signIn,
        IConfiguration config,
        ILogger<AuthService> logger)
    {
        _users = users;
        _signIn = signIn;
        _jwt = config.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
        _logger = logger;
    }

    public async Task<LoginResponse?> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        var user = await _users.FindByEmailAsync(email);
        if (user is null) return null;

        var pwOk = await _users.CheckPasswordAsync(user, password);
        if (!pwOk)
        {
            _logger.LogInformation("Bad password for {Email}", email);
            return null;
        }

        var roles = await _users.GetRolesAsync(user);
        user.LastLoginAt = DateTimeOffset.UtcNow;
        await _users.UpdateAsync(user);

        var (token, expires) = BuildToken(user, roles);
        return new LoginResponse(token, expires, user.Email!, roles.ToArray());
    }

    private (string token, DateTimeOffset expires) BuildToken(TicketingUser user, IList<string> roles)
    {
        if (string.IsNullOrEmpty(_jwt.Key) || _jwt.Key.Length < 32)
            throw new InvalidOperationException("Jwt:Key is missing or too short (need >= 32 chars).");

        var now = DateTime.UtcNow;
        var expires = now.AddHours(_jwt.LifetimeHours);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Email, user.Email ?? ""),
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Email, user.Email ?? ""),
            new(ClaimTypes.Name, user.DisplayName ?? user.UserName ?? user.Email ?? ""),
        };
        foreach (var r in roles)
            claims.Add(new Claim(ClaimTypes.Role, r));

        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.Key)),
            SecurityAlgorithms.HmacSha256);

        var jwt = new JwtSecurityToken(
            issuer: _jwt.Issuer,
            audience: _jwt.Audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(jwt), expires);
    }
}
