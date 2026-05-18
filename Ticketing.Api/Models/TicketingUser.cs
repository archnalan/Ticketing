using Microsoft.AspNetCore.Identity;

namespace Ticketing.Api.Models;

/// <summary>
/// ASP.NET Identity user for the Ticketing app. Intentionally minimal — this
/// is a small-audience internal tool (initially just one SuperAdmin). No
/// tenancy, no profile fields. Add columns here when there's a real need.
/// </summary>
public class TicketingUser : IdentityUser
{
    public string? DisplayName { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastLoginAt { get; set; }
}

public static class TicketingRoles
{
    public const string SuperAdmin = "SuperAdmin";

    public static readonly string[] All = { SuperAdmin };
}
