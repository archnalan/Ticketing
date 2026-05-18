namespace Ticketing.Web.Models;

public enum TicketSource { Error = 0, Feedback = 1, Manual = 2 }
public enum TicketStatus { Backlog = 0, Todo = 1, InProgress = 2, Review = 3, Deployed = 4 }
public enum TicketSeverity { Low = 0, Medium = 1, High = 2, Critical = 3 }

public class TicketDto
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public TicketSource Source { get; set; }
    public TicketStatus Status { get; set; }
    public TicketSeverity Severity { get; set; }
    public string? ServiceName { get; set; }
    public string? TraceId { get; set; }
    public string? ExceptionType { get; set; }
    public string? StackTrace { get; set; }
    public string? FeedbackId { get; set; }
    public string? ReporterEmail { get; set; }
    public string? ReporterName { get; set; }
    public int OccurrenceCount { get; set; }
    public DateTimeOffset DateCreated { get; set; }
    public DateTimeOffset? LastSeen { get; set; }
}

public class CreateTicketDto
{
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public TicketSeverity Severity { get; set; } = TicketSeverity.Medium;
}

public class UpdateStatusDto { public TicketStatus Status { get; set; } }
