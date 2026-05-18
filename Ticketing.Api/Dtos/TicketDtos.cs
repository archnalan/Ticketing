using Ticketing.Api.Models;

namespace Ticketing.Api.Dtos;

public record TicketDto(
    string Id,
    string Title,
    string? Description,
    TicketSource Source,
    TicketStatus Status,
    TicketSeverity Severity,
    string? ServiceName,
    string? TraceId,
    string? ExceptionType,
    string? StackTrace,
    string? FeedbackId,
    string? ReporterEmail,
    string? ReporterName,
    int OccurrenceCount,
    DateTimeOffset DateCreated,
    DateTimeOffset? LastSeen);

public record CreateTicketDto(
    string Title,
    string? Description,
    TicketSeverity Severity = TicketSeverity.Medium);

public record UpdateStatusDto(TicketStatus Status);

public record FeedbackTicketDto(
    string FeedbackId,
    string Subject,
    string Comment,
    string? Email,
    string? FullName,
    string? UserId);
