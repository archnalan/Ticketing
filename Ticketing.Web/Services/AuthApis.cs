using Refit;

namespace Ticketing.Web.Services;

/// <summary>Login proxy against Ticketing.Api/api/auth/login.</summary>
public interface ITicketingAuthApi
{
    [Post("/api/auth/login")]
    Task<IApiResponse<TicketingLoginResponse>> Login([Body] TicketingLoginRequest body);
}

public class TicketingLoginRequest
{
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
}

public class TicketingLoginResponse
{
    public string Token { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public string Email { get; set; } = "";
    public string[] Roles { get; set; } = Array.Empty<string>();
}
