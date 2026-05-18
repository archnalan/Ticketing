using System.ComponentModel.DataAnnotations;

namespace Ticketing.Api.Models;

public class Ticket
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required, StringLength(200)]
    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public TicketSource Source { get; set; }

    public TicketStatus Status { get; set; } = TicketStatus.Backlog;

    public TicketSeverity Severity { get; set; } = TicketSeverity.Medium;

    /// <summary>
    /// Stable hash of the originating signal (exception type + message + service)
    /// used to dedupe repeated errors into a single ticket.
    /// </summary>
    [StringLength(64)]
    public string? Fingerprint { get; set; }

    [StringLength(100)]
    public string? ServiceName { get; set; }

    [StringLength(64)]
    public string? TraceId { get; set; }

    [StringLength(32)]
    public string? SpanId { get; set; }

    public string? ExceptionType { get; set; }

    public string? StackTrace { get; set; }

    /// <summary>
    /// For Feedback-source tickets: the originating UserFeedback.Id in SongData.
    /// </summary>
    [StringLength(64)]
    public string? FeedbackId { get; set; }

    [StringLength(255)]
    public string? ReporterEmail { get; set; }

    [StringLength(255)]
    public string? ReporterName { get; set; }

    /// <summary>How many times this signal has been observed (incremented on dedupe).</summary>
    public int OccurrenceCount { get; set; } = 1;

    public DateTimeOffset DateCreated { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset DateModified { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSeen { get; set; }

    [StringLength(255)]
    public string? CreatedBy { get; set; }

    [StringLength(255)]
    public string? ModifiedBy { get; set; }

    public bool IsDeleted { get; set; }
}

public enum TicketSource
{
    Error = 0,
    Feedback = 1,
    Manual = 2,
}

public enum TicketStatus
{
    Backlog = 0,
    Todo = 1,
    InProgress = 2,
    Review = 3,
    Deployed = 4,
}

public enum TicketSeverity
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3,
}
