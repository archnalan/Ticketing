using Ticketing.Web.Models;
using Refit;

namespace Ticketing.Web.Services;

public interface ITicketsApi
{
    [Get("/api/tickets")]
    Task<IApiResponse<List<TicketDto>>> List([Query] TicketSource? source = null);

    [Get("/api/tickets/{id}")]
    Task<IApiResponse<TicketDto>> Get(string id);

    [Post("/api/tickets")]
    Task<IApiResponse<TicketDto>> Create([Body] CreateTicketDto dto);

    [Patch("/api/tickets/{id}/status")]
    Task<IApiResponse<TicketDto>> UpdateStatus(string id, [Body] UpdateStatusDto dto);

    [Delete("/api/tickets/{id}")]
    Task<IApiResponse<object?>> Delete(string id);
}

/// <summary>Login proxy against the existing FRELODYAPIs authorization endpoints.</summary>
public interface IFrelodyAuthApi
{
    [Post("/api/authorization/login")]
    Task<IApiResponse<FrelodyLoginResponse>> Login([Body] FrelodyLoginRequest body);
}

public class FrelodyLoginRequest
{
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
}

public class FrelodyLoginResponse
{
    public string? Token { get; set; }
    public string? RefreshToken { get; set; }
    public string? TenantId { get; set; }
}
