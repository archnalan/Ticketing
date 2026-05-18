using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Ticketing.Api.Models;

namespace Ticketing.Api.Data;

public class TicketsDbContext : IdentityDbContext<TicketingUser>
{
    public TicketsDbContext(DbContextOptions<TicketsDbContext> options) : base(options) { }

    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<DigestCounter> DigestCounters => Set<DigestCounter>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Ticket>(e =>
        {
            e.HasIndex(t => t.Fingerprint);
            e.HasIndex(t => new { t.Source, t.Status });
            e.HasIndex(t => t.FeedbackId);
            e.HasQueryFilter(t => !t.IsDeleted);
        });

        builder.Entity<DigestCounter>(e =>
        {
            e.Property(c => c.Source).HasConversion<int>();
        });
    }
}
