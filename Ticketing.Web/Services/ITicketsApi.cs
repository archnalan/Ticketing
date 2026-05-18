using Refit;
using Ticketing.Web.Models;

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
