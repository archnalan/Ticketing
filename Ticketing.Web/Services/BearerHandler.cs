using System.Net.Http.Headers;

namespace Ticketing.Web.Services;

/// <summary>
/// Forwards the Ticketing JWT (stashed as an "access_token" claim by the cookie
/// auth on login) on every outbound API call. Reads from the current request's
/// principal via IHttpContextAccessor.
/// </summary>
public class BearerHandler : DelegatingHandler
{
    public const string AccessTokenClaim = "access_token";

    private readonly IHttpContextAccessor _accessor;
    public BearerHandler(IHttpContextAccessor accessor) { _accessor = accessor; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var token = _accessor.HttpContext?.User?.FindFirst(AccessTokenClaim)?.Value;
        if (!string.IsNullOrEmpty(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return base.SendAsync(request, ct);
    }
}
