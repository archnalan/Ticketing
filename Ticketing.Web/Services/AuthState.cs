using Ticketing.Web.Models;
using Microsoft.AspNetCore.Components.Authorization;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;

namespace Ticketing.Web.Services;

/// <summary>
/// Server-side auth state for Ticketing. Session is held in an
/// HttpContext-scoped store (in-memory cookie holder) so the JWT is never
/// exposed to the browser. Drag/drop and ticket calls all run server-side
/// in interactive Blazor Server mode.
/// </summary>
public class AuthState : AuthenticationStateProvider
{
    private readonly SessionStore _session;
    private readonly ILogger<AuthState> _logger;

    public AuthState(SessionStore session, ILogger<AuthState> logger)
    {
        _session = session;
        _logger = logger;
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var s = _session.Current;
        if (s is null || string.IsNullOrEmpty(s.Token) || IsExpired(s.Token))
            return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));

        var identity = ParseToken(s.Token);
        return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
    }

    public void SignIn(LoginSession session)
    {
        _session.Current = session;
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    public void SignOut()
    {
        _session.Current = null;
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    private static ClaimsIdentity ParseToken(string jwt)
    {
        try
        {
            var parts = jwt.Split('.');
            if (parts.Length != 3) return new ClaimsIdentity();
            var payload = Pad(parts[1]);
            var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            using var doc = JsonDocument.Parse(json);
            var claims = new List<Claim>();
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                if (p.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var v in p.Value.EnumerateArray())
                        claims.Add(MapClaim(p.Name, v.ToString()));
                }
                else
                {
                    claims.Add(MapClaim(p.Name, p.Value.ToString()));
                }
            }
            return new ClaimsIdentity(claims, "jwt", ClaimTypes.Email, ClaimTypes.Role);
        }
        catch { return new ClaimsIdentity(); }
    }

    // Map common short-form JWT claim names to the .NET full URIs that
    // [Authorize(Roles=...)] and ClaimsPrincipal.IsInRole expect.
    private static Claim MapClaim(string name, string value) => name switch
    {
        "role" or "roles" => new Claim(ClaimTypes.Role, value),
        "http://schemas.microsoft.com/ws/2008/06/identity/claims/role" => new Claim(ClaimTypes.Role, value),
        "email" or "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress" => new Claim(ClaimTypes.Email, value),
        "sub" or "nameid" or "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier" => new Claim(ClaimTypes.NameIdentifier, value),
        _ => new Claim(name, value),
    };

    private static bool IsExpired(string jwt)
    {
        try
        {
            var parts = jwt.Split('.');
            if (parts.Length != 3) return true;
            using var doc = JsonDocument.Parse(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(Pad(parts[1]))));
            if (!doc.RootElement.TryGetProperty("exp", out var exp)) return true;
            return DateTimeOffset.FromUnixTimeSeconds(exp.GetInt64()) <= DateTimeOffset.UtcNow.AddSeconds(30);
        }
        catch { return true; }
    }

    private static string Pad(string s) => (s.Length % 4) switch { 2 => s + "==", 3 => s + "=", _ => s };
}

/// <summary>Scoped per-circuit holder of the current session.</summary>
public class SessionStore { public LoginSession? Current { get; set; } }

/// <summary>Injects the JWT held in SessionStore into outgoing tickets-API calls.</summary>
public class BearerHandler : DelegatingHandler
{
    private readonly SessionStore _session;
    public BearerHandler(SessionStore session) { _session = session; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var token = _session.Current?.Token;
        if (!string.IsNullOrEmpty(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return base.SendAsync(request, ct);
    }
}
