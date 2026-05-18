using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Ticketing.Api.Data;
using Ticketing.Api.Models;

namespace Ticketing.Api.Services;

/// <summary>
/// Per-source rolling 5-ticket digest. Increment on each newly-created
/// ticket; once a source reaches the threshold, compose a summary email
/// of those last N tickets and reset the counter. Failures are logged but
/// must not break the ticket-creation path.
/// </summary>
public interface IDigestService
{
    Task NotifyTicketCreatedAsync(TicketSource source, CancellationToken ct = default);
}

public class DigestOptions
{
    public bool Enabled { get; set; } = true;
    public int ThresholdPerSource { get; set; } = 5;
    public string? SuperAdminEmail { get; set; }
}

public class DigestService : IDigestService
{
    private readonly TicketsDbContext _db;
    private readonly IEmailService _email;
    private readonly DigestOptions _opts;
    private readonly ILogger<DigestService> _logger;

    public DigestService(
        TicketsDbContext db,
        IEmailService email,
        IConfiguration config,
        ILogger<DigestService> logger)
    {
        _db = db;
        _email = email;
        _opts = config.GetSection("Digest").Get<DigestOptions>() ?? new DigestOptions();
        _logger = logger;
    }

    public async Task NotifyTicketCreatedAsync(TicketSource source, CancellationToken ct = default)
    {
        if (!_opts.Enabled) return;

        try
        {
            var counter = await _db.DigestCounters.FindAsync(new object?[] { source }, ct);
            if (counter is null)
            {
                counter = new DigestCounter { Source = source, UnsentCount = 0 };
                _db.DigestCounters.Add(counter);
            }
            counter.UnsentCount++;
            await _db.SaveChangesAsync(ct);

            if (counter.UnsentCount < _opts.ThresholdPerSource) return;

            // Threshold hit. Fetch the N most recent tickets of this source.
            var recent = await _db.Tickets.AsNoTracking()
                .Where(t => t.Source == source)
                .OrderByDescending(t => t.DateCreated)
                .Take(_opts.ThresholdPerSource)
                .ToListAsync(ct);

            if (string.IsNullOrWhiteSpace(_opts.SuperAdminEmail))
            {
                _logger.LogError(
                    "DIGEST_SUPERADMIN_EMAIL is not set — cannot send {Source} digest. Counter NOT reset; will retry on next ticket.",
                    source);
                return;
            }

            var sent = await SendDigestAsync(source, recent, _opts.SuperAdminEmail!, ct);
            if (sent)
            {
                counter.UnsentCount = 0;
                counter.LastSentAt = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync(ct);
                _logger.LogInformation("Sent {Source} digest of {Count} tickets to {Email}",
                    source, recent.Count, _opts.SuperAdminEmail);
            }
            // If send failed, leave counter at its bumped value — the next
            // ticket creation will retry. This deliberately over-sends rather
            // than silently dropping notifications.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Digest notify failed for {Source}", source);
        }
    }

    private Task<bool> SendDigestAsync(TicketSource source, List<Ticket> tickets, string toAddress, CancellationToken ct)
    {
        var subject = $"[Tickets] {tickets.Count} new {source.ToString().ToLowerInvariant()} ticket{(tickets.Count == 1 ? "" : "s")}";
        var body = BuildHtml(source, tickets);
        return _email.SendHtmlAsync(toAddress, subject, body, ct);
    }

    private static string BuildHtml(TicketSource source, List<Ticket> tickets)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!doctype html><html><body style=\"font-family:system-ui,-apple-system,Segoe UI,sans-serif;color:#1f2937;\">");
        sb.AppendLine($"<h2 style=\"margin:0 0 8px;\">New {source} tickets</h2>");
        sb.AppendLine($"<p style=\"margin:0 0 16px;color:#6b7280;\">{tickets.Count} ticket(s) created since the last digest.</p>");
        sb.AppendLine("<table cellpadding=\"8\" cellspacing=\"0\" style=\"border-collapse:collapse;width:100%;border:1px solid #e5e7eb;border-radius:8px;overflow:hidden;\">");
        sb.AppendLine("<thead style=\"background:#f9fafb;\"><tr>");
        sb.AppendLine("<th align=\"left\" style=\"font-size:12px;color:#6b7280;\">Severity</th>");
        sb.AppendLine("<th align=\"left\" style=\"font-size:12px;color:#6b7280;\">Title</th>");
        sb.AppendLine("<th align=\"left\" style=\"font-size:12px;color:#6b7280;\">Service</th>");
        sb.AppendLine("<th align=\"left\" style=\"font-size:12px;color:#6b7280;\">Occurrences</th>");
        sb.AppendLine("<th align=\"left\" style=\"font-size:12px;color:#6b7280;\">When</th>");
        sb.AppendLine("</tr></thead><tbody>");

        foreach (var t in tickets)
        {
            sb.Append("<tr style=\"border-top:1px solid #e5e7eb;\">");
            sb.Append($"<td><span style=\"display:inline-block;font-size:11px;font-weight:700;padding:2px 6px;border-radius:4px;{SeverityStyle(t.Severity)}\">{t.Severity.ToString().ToUpperInvariant()}</span></td>");
            sb.Append($"<td><div style=\"font-weight:600;\">{WebUtility.HtmlEncode(t.Title)}</div>");
            if (!string.IsNullOrWhiteSpace(t.Description))
                sb.Append($"<div style=\"font-size:12px;color:#6b7280;margin-top:2px;\">{WebUtility.HtmlEncode(Trim(t.Description!, 220))}</div>");
            sb.Append("</td>");
            sb.Append($"<td style=\"font-size:13px;color:#374151;\">{WebUtility.HtmlEncode(t.ServiceName ?? "-")}</td>");
            sb.Append($"<td style=\"font-size:13px;color:#374151;\">×{t.OccurrenceCount}</td>");
            sb.Append($"<td style=\"font-size:13px;color:#6b7280;\">{t.DateCreated.UtcDateTime:yyyy-MM-dd HH:mm} UTC</td>");
            sb.AppendLine("</tr>");
        }

        sb.AppendLine("</tbody></table>");
        sb.AppendLine("<p style=\"margin-top:16px;font-size:12px;color:#9ca3af;\">Visit tickets.frelody.com to triage.</p>");
        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static string SeverityStyle(TicketSeverity s) => s switch
    {
        TicketSeverity.Critical => "background:#fee2e2;color:#b91c1c;",
        TicketSeverity.High     => "background:#fef3c7;color:#92400e;",
        TicketSeverity.Medium   => "background:#dbeafe;color:#1e40af;",
        _                        => "background:#f3f4f6;color:#374151;",
    };

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
