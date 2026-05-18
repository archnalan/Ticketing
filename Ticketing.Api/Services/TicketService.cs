using Ticketing.Api.Data;
using Ticketing.Api.Dtos;
using Ticketing.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace Ticketing.Api.Services;

public interface ITicketService
{
    Task<List<TicketDto>> ListAsync(TicketSource? source = null, CancellationToken ct = default);
    Task<TicketDto?> GetAsync(string id, CancellationToken ct = default);
    Task<TicketDto> CreateManualAsync(CreateTicketDto dto, string? createdBy, CancellationToken ct = default);
    Task<TicketDto?> UpdateStatusAsync(string id, TicketStatus status, string? modifiedBy, CancellationToken ct = default);
    Task<bool> DeleteAsync(string id, CancellationToken ct = default);

    /// <summary>Create-or-bump an error-source ticket from an OTLP log record.</summary>
    Task<TicketDto> UpsertErrorAsync(ErrorTicketSignal signal, CancellationToken ct = default);

    /// <summary>Create a feedback-source ticket from an incoming feedback record.</summary>
    Task<TicketDto> CreateFromFeedbackAsync(FeedbackTicketDto dto, CancellationToken ct = default);
}

public record ErrorTicketSignal(
    string? ServiceName,
    string? ExceptionType,
    string Message,
    string? StackTrace,
    string? TraceId,
    string? SpanId,
    TicketSeverity Severity);

public class TicketService : ITicketService
{
    private readonly TicketsDbContext _db;
    private readonly ITicketAiService _ai;
    private readonly IDigestService _digest;
    private readonly ILogger<TicketService> _logger;

    public TicketService(
        TicketsDbContext db,
        ITicketAiService ai,
        IDigestService digest,
        ILogger<TicketService> logger)
    {
        _db = db;
        _ai = ai;
        _digest = digest;
        _logger = logger;
    }

    public async Task<List<TicketDto>> ListAsync(TicketSource? source = null, CancellationToken ct = default)
    {
        var q = _db.Tickets.AsNoTracking().AsQueryable();
        if (source is not null) q = q.Where(t => t.Source == source);

        var rows = await q.OrderByDescending(t => t.DateCreated).Take(500).ToListAsync(ct);
        return rows.Select(Map).ToList();
    }

    public async Task<TicketDto?> GetAsync(string id, CancellationToken ct = default)
    {
        var t = await _db.Tickets.FindAsync(new object?[] { id }, ct);
        return t is null ? null : Map(t);
    }

    public async Task<TicketDto> CreateManualAsync(CreateTicketDto dto, string? createdBy, CancellationToken ct = default)
    {
        var t = new Ticket
        {
            Title = dto.Title,
            Description = dto.Description,
            Severity = dto.Severity,
            Source = TicketSource.Manual,
            Status = TicketStatus.Backlog,
            CreatedBy = createdBy,
            ModifiedBy = createdBy,
        };
        _db.Tickets.Add(t);
        await _db.SaveChangesAsync(ct);
        await _digest.NotifyTicketCreatedAsync(TicketSource.Manual, ct);
        return Map(t);
    }

    public async Task<TicketDto?> UpdateStatusAsync(string id, TicketStatus status, string? modifiedBy, CancellationToken ct = default)
    {
        var t = await _db.Tickets.FindAsync(new object?[] { id }, ct);
        if (t is null) return null;

        t.Status = status;
        t.ModifiedBy = modifiedBy;
        t.DateModified = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Map(t);
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        var t = await _db.Tickets.FindAsync(new object?[] { id }, ct);
        if (t is null) return false;
        t.IsDeleted = true;
        t.DateModified = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<TicketDto> UpsertErrorAsync(ErrorTicketSignal signal, CancellationToken ct = default)
    {
        var fingerprint = Fingerprint(signal.ServiceName, signal.ExceptionType, signal.Message);

        var existing = await _db.Tickets
            .FirstOrDefaultAsync(t => t.Fingerprint == fingerprint && t.Source == TicketSource.Error, ct);

        if (existing is not null)
        {
            existing.OccurrenceCount += 1;
            existing.LastSeen = DateTimeOffset.UtcNow;
            existing.DateModified = DateTimeOffset.UtcNow;
            // Severity can ratchet up but not down — a single critical sets the floor.
            if (signal.Severity > existing.Severity) existing.Severity = signal.Severity;
            await _db.SaveChangesAsync(ct);
            return Map(existing);
        }

        // First-occurrence only: ask the LLM to rewrite the raw exception text
        // into a human-friendly title + description. Falls back to raw text
        // when AI is unconfigured or fails. Dedupe bumps (above) skip this.
        var summary = await _ai.SummarizeAsync(
            signal.ServiceName, signal.ExceptionType, signal.Message, signal.StackTrace, ct);

        var t = new Ticket
        {
            Title = summary?.Title ?? TrimTitle(signal.ExceptionType, signal.Message),
            Description = summary?.Description ?? signal.Message,
            Source = TicketSource.Error,
            Status = TicketStatus.Backlog,
            Severity = signal.Severity,
            Fingerprint = fingerprint,
            ServiceName = signal.ServiceName,
            TraceId = signal.TraceId,
            SpanId = signal.SpanId,
            ExceptionType = signal.ExceptionType,
            StackTrace = signal.StackTrace,
            LastSeen = DateTimeOffset.UtcNow,
        };
        _db.Tickets.Add(t);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Created error ticket {Id} for {Service}/{Exception} (ai={AiUsed})",
            t.Id, signal.ServiceName, signal.ExceptionType, summary is not null);

        await _digest.NotifyTicketCreatedAsync(TicketSource.Error, ct);
        return Map(t);
    }

    public async Task<TicketDto> CreateFromFeedbackAsync(FeedbackTicketDto dto, CancellationToken ct = default)
    {
        // Dedupe by FeedbackId so a retry won't create duplicates.
        var existing = await _db.Tickets.FirstOrDefaultAsync(t => t.FeedbackId == dto.FeedbackId, ct);
        if (existing is not null) return Map(existing);

        var t = new Ticket
        {
            Title = string.IsNullOrWhiteSpace(dto.Subject) ? "Feedback" : dto.Subject,
            Description = dto.Comment,
            Source = TicketSource.Feedback,
            Status = TicketStatus.Backlog,
            Severity = TicketSeverity.Medium,
            FeedbackId = dto.FeedbackId,
            ReporterEmail = dto.Email,
            ReporterName = dto.FullName,
            CreatedBy = dto.UserId,
        };
        _db.Tickets.Add(t);
        await _db.SaveChangesAsync(ct);
        await _digest.NotifyTicketCreatedAsync(TicketSource.Feedback, ct);
        return Map(t);
    }

    private static string Fingerprint(string? service, string? exception, string message)
    {
        var src = $"{service}|{exception}|{Normalize(message)}";
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(src));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string Normalize(string s)
    {
        // Strip line numbers / addresses / GUIDs that vary between identical errors.
        return System.Text.RegularExpressions.Regex.Replace(s ?? "", @"\b0x[0-9a-fA-F]+|\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b|line \d+", "");
    }

    private static string TrimTitle(string? type, string message)
    {
        var title = !string.IsNullOrWhiteSpace(type) ? $"{type}: {message}" : message;
        return title.Length > 180 ? title[..180] + "…" : title;
    }

    private static TicketDto Map(Ticket t) => new(
        t.Id, t.Title, t.Description, t.Source, t.Status, t.Severity,
        t.ServiceName, t.TraceId, t.ExceptionType, t.StackTrace,
        t.FeedbackId, t.ReporterEmail, t.ReporterName,
        t.OccurrenceCount, t.DateCreated, t.LastSeen);
}
