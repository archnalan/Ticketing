using Ticketing.Api.Dtos;
using Ticketing.Api.Models;
using Ticketing.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ticketing.Api.Controllers;

[ApiController]
[Route("api/tickets")]
[Authorize(Roles = "SuperAdmin")]
public class TicketsController : ControllerBase
{
    private readonly ITicketService _service;

    public TicketsController(ITicketService service) { _service = service; }

    [HttpGet]
    public async Task<ActionResult<List<TicketDto>>> List([FromQuery] TicketSource? source, CancellationToken ct)
        => Ok(await _service.ListAsync(source, ct));

    [HttpGet("{id}")]
    public async Task<ActionResult<TicketDto>> Get(string id, CancellationToken ct)
    {
        var t = await _service.GetAsync(id, ct);
        return t is null ? NotFound() : Ok(t);
    }

    [HttpPost]
    public async Task<ActionResult<TicketDto>> Create([FromBody] CreateTicketDto dto, CancellationToken ct)
    {
        var created = await _service.CreateManualAsync(dto, User.Identity?.Name, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPatch("{id}/status")]
    public async Task<ActionResult<TicketDto>> UpdateStatus(string id, [FromBody] UpdateStatusDto dto, CancellationToken ct)
    {
        var updated = await _service.UpdateStatusAsync(id, dto.Status, User.Identity?.Name, ct);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var ok = await _service.DeleteAsync(id, ct);
        return ok ? NoContent() : NotFound();
    }

    /// <summary>
    /// Called by FRELODYAPIs FeedbackService after a feedback is persisted.
    /// Service-to-service, secured by a shared bearer secret (separate scheme).
    /// </summary>
    [AllowAnonymous]
    [HttpPost("from-feedback")]
    public async Task<ActionResult<TicketDto>> FromFeedback(
        [FromBody] FeedbackTicketDto dto,
        [FromHeader(Name = "X-Tickets-Secret")] string? secret,
        [FromServices] IConfiguration config,
        CancellationToken ct)
    {
        var expected = config["Tickets:IngestSecret"];
        if (string.IsNullOrEmpty(expected) || secret != expected) return Unauthorized();

        var created = await _service.CreateFromFeedbackAsync(dto, ct);
        return Ok(created);
    }
}
