using Ticketing.Api.Models;
using System.Text.Json;

namespace Ticketing.Api.Services;

/// <summary>
/// Parses OTLP/HTTP JSON log payloads and converts severe records into
/// error tickets. OTLP JSON shape:
///   { "resourceLogs": [ { "resource": {...}, "scopeLogs": [ { "logRecords": [...] } ] } ] }
/// Severity numbers: 1-8 trace/debug, 9-12 info, 13-16 warn, 17-20 error, 21-24 fatal.
/// </summary>
public class OtlpLogsReceiver
{
    private readonly ITicketService _tickets;
    private readonly ILogger<OtlpLogsReceiver> _logger;
    private const int ErrorThreshold = 17;

    public OtlpLogsReceiver(ITicketService tickets, ILogger<OtlpLogsReceiver> logger)
    {
        _tickets = tickets;
        _logger = logger;
    }

    public async Task<(int processed, int ticketed)> ProcessAsync(JsonElement root, CancellationToken ct = default)
    {
        int processed = 0, ticketed = 0;
        if (root.ValueKind != JsonValueKind.Object) return (0, 0);
        if (!root.TryGetProperty("resourceLogs", out var resourceLogs) || resourceLogs.ValueKind != JsonValueKind.Array)
            return (0, 0);

        foreach (var rl in resourceLogs.EnumerateArray())
        {
            var serviceName = ExtractServiceName(rl);

            if (!rl.TryGetProperty("scopeLogs", out var scopeLogs) || scopeLogs.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var sl in scopeLogs.EnumerateArray())
            {
                if (!sl.TryGetProperty("logRecords", out var records) || records.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var rec in records.EnumerateArray())
                {
                    processed++;
                    var sev = ReadInt(rec, "severityNumber") ?? 0;
                    if (sev < ErrorThreshold) continue;

                    var message = ReadBody(rec);
                    var attrs = ReadAttributes(rec);
                    attrs.TryGetValue("exception.type", out var exType);
                    attrs.TryGetValue("exception.stacktrace", out var stack);
                    attrs.TryGetValue("exception.message", out var exMsg);

                    var finalMessage = !string.IsNullOrWhiteSpace(exMsg) ? exMsg! : message;
                    if (string.IsNullOrWhiteSpace(finalMessage)) continue;

                    var traceId = ReadString(rec, "traceId");
                    var spanId = ReadString(rec, "spanId");

                    var signal = new ErrorTicketSignal(
                        ServiceName: serviceName,
                        ExceptionType: exType,
                        Message: finalMessage,
                        StackTrace: stack,
                        TraceId: traceId,
                        SpanId: spanId,
                        Severity: sev >= 21 ? TicketSeverity.Critical : TicketSeverity.High);

                    try
                    {
                        await _tickets.UpsertErrorAsync(signal, ct);
                        ticketed++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to upsert error ticket from OTLP record");
                    }
                }
            }
        }

        return (processed, ticketed);
    }

    // ── OTLP JSON helpers ────────────────────────────────────────────────────

    private static string? ExtractServiceName(JsonElement resourceLog)
    {
        if (!resourceLog.TryGetProperty("resource", out var resource)) return null;
        if (!resource.TryGetProperty("attributes", out var attrs) || attrs.ValueKind != JsonValueKind.Array) return null;
        foreach (var a in attrs.EnumerateArray())
        {
            if (a.TryGetProperty("key", out var k) && k.GetString() == "service.name"
                && a.TryGetProperty("value", out var v))
            {
                return ReadAnyValue(v);
            }
        }
        return null;
    }

    private static Dictionary<string, string?> ReadAttributes(JsonElement rec)
    {
        var dict = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (!rec.TryGetProperty("attributes", out var attrs) || attrs.ValueKind != JsonValueKind.Array) return dict;
        foreach (var a in attrs.EnumerateArray())
        {
            if (!a.TryGetProperty("key", out var k)) continue;
            var key = k.GetString();
            if (string.IsNullOrEmpty(key)) continue;
            string? value = null;
            if (a.TryGetProperty("value", out var v)) value = ReadAnyValue(v);
            dict[key] = value;
        }
        return dict;
    }

    private static string? ReadBody(JsonElement rec)
    {
        if (!rec.TryGetProperty("body", out var body)) return null;
        return ReadAnyValue(body);
    }

    private static string? ReadAnyValue(JsonElement v)
    {
        // OTLP AnyValue: { stringValue | intValue | boolValue | doubleValue | arrayValue | kvlistValue | bytesValue }
        if (v.ValueKind != JsonValueKind.Object) return v.ToString();
        if (v.TryGetProperty("stringValue", out var s)) return s.GetString();
        if (v.TryGetProperty("intValue", out var i)) return i.ToString();
        if (v.TryGetProperty("boolValue", out var b)) return b.GetBoolean() ? "true" : "false";
        if (v.TryGetProperty("doubleValue", out var d)) return d.ToString();
        return v.ToString();
    }

    private static int? ReadInt(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var p)) return null;
        return p.ValueKind switch
        {
            JsonValueKind.Number => p.TryGetInt32(out var i) ? i : null,
            JsonValueKind.String => int.TryParse(p.GetString(), out var i) ? i : null,
            _ => null,
        };
    }

    private static string? ReadString(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var p)) return null;
        return p.ValueKind == JsonValueKind.String ? p.GetString() : p.ToString();
    }
}
