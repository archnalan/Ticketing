using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Ticketing.Web.Services;

namespace Ticketing.Web.Controllers;

/// <summary>
/// HTTP endpoints that drive cookie auth for the Blazor Server kanban.
/// The Login.razor page POSTs a form here; this controller calls
/// Ticketing.Api, parses the JWT claims, signs in via cookie, and
/// stashes the raw JWT as a claim so the API client can forward it.
/// </summary>
[ApiController]
[Route("_auth")]
[AllowAnonymous]
[IgnoreAntiforgeryToken]
public class AuthController : ControllerBase
{
    private readonly ITicketingAuthApi _api;
    private readonly ILogger<AuthController> _logger;
    private readonly IConfiguration _config;

    public AuthController(ITicketingAuthApi api, ILogger<AuthController> logger, IConfiguration config)
    {
        _api = api;
        _logger = logger;
        _config = config;
    }

    [HttpPost("signin")]
    public async Task<IActionResult> SignIn(
        [FromForm] string email,
        [FromForm] string password,
        [FromForm] string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return Redirect(BuildLoginUrl(returnUrl, "Email and password are required."));

        TicketingLoginResponse? body;
        try
        {
            var resp = await _api.Login(new TicketingLoginRequest { Email = email, Password = password });
            if (!resp.IsSuccessStatusCode || resp.Content is null)
                return Redirect(BuildLoginUrl(returnUrl, "Invalid email or password."));
            body = resp.Content;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Login API call failed");
            return Redirect(BuildLoginUrl(returnUrl, "Login service unavailable."));
        }

        // Roles must include SuperAdmin to use this app.
        if (!body.Roles.Contains("SuperAdmin", StringComparer.OrdinalIgnoreCase))
            return Redirect(BuildLoginUrl(returnUrl, "Your account does not have access to this app."));

        // Lift the API-issued JWT's claims into our cookie principal, and tuck
        // the raw token onto the principal so BearerHandler can forward it.
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(body.Token);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, jwt.Subject ?? body.Email),
            new(ClaimTypes.Name,           jwt.Subject ?? body.Email),
            new(ClaimTypes.Email,          body.Email),
            new(BearerHandler.AccessTokenClaim, body.Token),
        };
        foreach (var r in body.Roles)
            claims.Add(new Claim(ClaimTypes.Role, r));

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme,
            nameType: ClaimTypes.Name, roleType: ClaimTypes.Role);
        var principal = new ClaimsPrincipal(identity);

        var authProps = new AuthenticationProperties
        {
            // Cookie expiry follows the JWT's expiry so a stale token doesn't keep
            // an expired session looking authenticated.
            ExpiresUtc = body.ExpiresAt,
            IsPersistent = true,
            AllowRefresh = false,
        };

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, authProps);

        var target = string.IsNullOrEmpty(returnUrl) || !Url.IsLocalUrl(returnUrl) ? "/pipeline" : returnUrl;
        return Redirect(target);
    }

    /// <summary>
    /// Hands the signed-in user off to the FRELODY documentation site (a separate
    /// WebAssembly SPA on another origin) carrying their current session.
    ///
    /// The docs site is not its own identity provider; it picks up a bridged session
    /// from a URL fragment (<c>#session=&lt;base64url-json&gt;</c>) shaped like FRELODY's
    /// <c>LoginResponseDto</c>, extracts the JWT and derives the user from its claims.
    /// We reuse the exact base64url encoding FRELODY's web app uses so the docs
    /// AuthService decodes it unchanged. A fragment never reaches a server and the docs
    /// site strips it from the address bar on load, so the token isn't left visible.
    ///
    /// If no token is present (e.g. the anonymous landing page links here) we just open
    /// the docs site without a session.
    /// </summary>
    [HttpGet("docs")]
    public IActionResult Docs()
    {
        var docsBase = (_config["Urls:Docs"] ?? string.Empty).TrimEnd('/');
        if (string.IsNullOrEmpty(docsBase))
        {
            _logger.LogWarning("Urls:Docs is not configured; cannot open the documentation site.");
            return Redirect("/");
        }

        var token = User.FindFirst(BearerHandler.AccessTokenClaim)?.Value;
        if (string.IsNullOrEmpty(token))
            return Redirect(docsBase);

        // LoginResponseDto-shaped payload — the docs AuthService only needs `token`;
        // it derives name/roles/tier from the JWT's own claims.
        var json = System.Text.Json.JsonSerializer.Serialize(new { token });
        var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        return Redirect($"{docsBase}/#session={encoded}");
    }

    [HttpGet("signout")]
    [HttpPost("signout")]
    public async Task<IActionResult> SignOutUser()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Redirect("/login");
    }

    private static string BuildLoginUrl(string? returnUrl, string error)
    {
        var qs = new List<string>();
        if (!string.IsNullOrEmpty(returnUrl)) qs.Add($"returnUrl={Uri.EscapeDataString(returnUrl)}");
        qs.Add($"error={Uri.EscapeDataString(error)}");
        return "/login?" + string.Join('&', qs);
    }
}
