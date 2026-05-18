using Ticketing.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace Ticketing.Api.Controllers;

/// <summary>
/// Accepts OTLP/HTTP exports from the otel-collector and turns severe log
/// records into error tickets. Logs only — traces and metrics are not ingested.
/// </summary>
[ApiController]
[AllowAnonymous]
public class OtlpController : ControllerBase
{
    private readonly OtlpLogsReceiver _receiver;
    private readonly ILogger<OtlpController> _logger;
    private readonly IConfiguration _config;

    public OtlpController(OtlpLogsReceiver receiver, ILogger<OtlpController> logger, IConfiguration config)
    {
        _receiver = receiver;
        _logger = logger;
        _config = config;
    }

    [HttpPost("/v1/logs")]
    [Consumes("application/json")]
    public async Task<IActionResult> Logs(CancellationToken ct)
    {
        // Optional shared secret so a public hostname can't blast junk in.
        var expected = _config["OtlpReceiver:Secret"];
        if (!string.IsNullOrEmpty(expected))
        {
            var got = Request.Headers["X-Otel-Secret"].ToString();
            if (got != expected) return Unauthorized();
        }

        try
        {
            using var doc = await JsonDocument.ParseAsync(Request.Body, cancellationToken: ct);
            var (processed, ticketed) = await _receiver.ProcessAsync(doc.RootElement, ct);
            _logger.LogDebug("OTLP logs: processed={Processed} ticketed={Ticketed}", processed, ticketed);
            // OTLP partial-success response shape (empty body means full success).
            return Ok(new { partialSuccess = new { rejectedLogRecords = 0 } });
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid OTLP/JSON log payload");
            return BadRequest();
        }
    }

    /// <summary>Stubbed accept-and-discard for traces/metrics to keep the collector happy.</summary>
    [HttpPost("/v1/traces")]
    [HttpPost("/v1/metrics")]
    public IActionResult DiscardOther() => Ok(new { partialSuccess = new { } });
}
