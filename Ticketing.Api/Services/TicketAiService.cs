using System.Text;
using System.Text.Json;

namespace Ticketing.Api.Services;

/// <summary>
/// Calls an LLM to turn raw OTel log data into a concise human-friendly
/// title + description. Returns null on any failure — the caller falls
/// back to the raw exception text. Same pattern as FRELODYAPIs/SongAiService.
/// </summary>
public interface ITicketAiService
{
    Task<TicketSummary?> SummarizeAsync(
        string? serviceName,
        string? exceptionType,
        string message,
        string? stackTrace,
        CancellationToken ct = default);
}

public record TicketSummary(string Title, string Description);

public class TicketAiService : ITicketAiService
{
    public const string HttpClientName = "TicketAi";

    private const string Model = "meta/llama-3.1-8b-instruct";
    private const int MaxTitleChars = 120;

    private readonly IHttpClientFactory _factory;
    private readonly ILogger<TicketAiService> _logger;
    private readonly string? _apiKey;

    public TicketAiService(IHttpClientFactory factory, IConfiguration config, ILogger<TicketAiService> logger)
    {
        _factory = factory;
        _logger = logger;
        // Same env-var key path the FRELODY side uses, so a single secret covers both apps.
        _apiKey = config["API_KEYS:nvidiaApiKey"] ?? config["NvidiaApi:Key"];
    }

    public async Task<TicketSummary?> SummarizeAsync(
        string? serviceName,
        string? exceptionType,
        string message,
        string? stackTrace,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey)) return null;

        try
        {
            var prompt = BuildPrompt(serviceName, exceptionType, message, stackTrace);
            var raw = await CallAsync(prompt, ct);
            return Parse(raw);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI summarization failed; falling back to raw message");
            return null;
        }
    }

    private static string BuildPrompt(string? service, string? exType, string message, string? stack)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are an engineering triage assistant. Convert a raw error log into a concise, human-friendly ticket.");
        sb.AppendLine();
        sb.AppendLine("Return ONLY a JSON object with exactly two fields:");
        sb.AppendLine("  \"title\":       a single line, max 100 chars, present tense, no exception class names if avoidable");
        sb.AppendLine("  \"description\": 1-3 sentences explaining what went wrong and where, in plain English");
        sb.AppendLine();
        sb.AppendLine("Do NOT include the JSON in a code block, markdown, or any prose around it.");
        sb.AppendLine();
        sb.AppendLine("─── Raw log ───");
        if (!string.IsNullOrWhiteSpace(service))    sb.AppendLine($"service:   {service}");
        if (!string.IsNullOrWhiteSpace(exType))     sb.AppendLine($"exception: {exType}");
        sb.AppendLine($"message:   {Trim(message, 800)}");
        if (!string.IsNullOrWhiteSpace(stack))      sb.AppendLine($"stack:\n{Trim(stack!, 1200)}");
        return sb.ToString();
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    private async Task<string> CallAsync(string prompt, CancellationToken ct)
    {
        var client = _factory.CreateClient(HttpClientName);
        if (client.BaseAddress is null)
            client.BaseAddress = new Uri("https://integrate.api.nvidia.com/v1/");
        if (client.DefaultRequestHeaders.Authorization is null)
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);

        var body = new
        {
            model = Model,
            messages = new[]
            {
                new { role = "system", content = "You output only valid minified JSON. No prose, no code fences." },
                new { role = "user",   content = prompt },
            },
            temperature = 0.2,
            max_tokens = 400,
        };

        var req = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        var resp = await client.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct);

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? string.Empty;
    }

    private static TicketSummary? Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        // The model sometimes wraps JSON in ```json ... ``` despite instructions.
        var trimmed = raw.Trim();
        var firstBrace = trimmed.IndexOf('{');
        var lastBrace = trimmed.LastIndexOf('}');
        if (firstBrace < 0 || lastBrace <= firstBrace) return null;
        trimmed = trimmed[firstBrace..(lastBrace + 1)];

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;
            if (!root.TryGetProperty("title", out var t)) return null;
            if (!root.TryGetProperty("description", out var d)) return null;

            var title = t.GetString()?.Trim();
            var desc = d.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(desc)) return null;

            if (title!.Length > MaxTitleChars) title = title[..MaxTitleChars].TrimEnd() + "…";
            return new TicketSummary(title, desc!);
        }
        catch
        {
            return null;
        }
    }
}
