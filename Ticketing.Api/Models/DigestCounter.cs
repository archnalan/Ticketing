using System.ComponentModel.DataAnnotations;

namespace Ticketing.Api.Models;

/// <summary>
/// Per-source rolling counter of tickets created since the last digest email.
/// One row per <see cref="TicketSource"/>. Lives in the DB (not memory) so a
/// restart doesn't drop progress toward the 5-ticket threshold.
/// </summary>
public class DigestCounter
{
    [Key]
    public TicketSource Source { get; set; }

    /// <summary>Tickets of this source created since the last digest was sent.</summary>
    public int UnsentCount { get; set; }

    public DateTimeOffset? LastSentAt { get; set; }
}
