using Microsoft.AspNetCore.Identity;
using Ticketing.Api.Models;

namespace Ticketing.Api.Seeding;

/// <summary>
/// Idempotently ensures (a) the SuperAdmin role exists and (b) a single
/// SuperAdmin user exists with the env-supplied credentials. Re-running is
/// safe — only missing pieces are created. Run from Program.cs at startup.
///
/// Configuration:
///   Auth:SuperAdmin:Email     — required if no SuperAdmin user yet exists
///   Auth:SuperAdmin:Password  — required only on first run (to create the user)
/// </summary>
public class IdentitySeeder
{
    private readonly UserManager<TicketingUser> _users;
    private readonly RoleManager<IdentityRole> _roles;
    private readonly IConfiguration _config;
    private readonly ILogger<IdentitySeeder> _logger;

    public IdentitySeeder(
        UserManager<TicketingUser> users,
        RoleManager<IdentityRole> roles,
        IConfiguration config,
        ILogger<IdentitySeeder> logger)
    {
        _users = users;
        _roles = roles;
        _config = config;
        _logger = logger;
    }

    public async Task SeedAsync()
    {
        // 1) Ensure every role exists.
        foreach (var role in TicketingRoles.All)
        {
            if (!await _roles.RoleExistsAsync(role))
            {
                var r = await _roles.CreateAsync(new IdentityRole(role));
                if (!r.Succeeded)
                {
                    _logger.LogError("Failed to create role {Role}: {Errors}",
                        role, string.Join("; ", r.Errors.Select(e => e.Description)));
                    return;
                }
                _logger.LogInformation("Created role {Role}", role);
            }
        }

        // 2) Ensure the SuperAdmin user exists. Env-driven; missing email aborts.
        var email = _config["Auth:SuperAdmin:Email"];
        if (string.IsNullOrWhiteSpace(email))
        {
            _logger.LogWarning(
                "Auth:SuperAdmin:Email not configured — skipping SuperAdmin seed. " +
                "Set the env var to create the initial admin user.");
            return;
        }

        var existing = await _users.FindByEmailAsync(email);
        if (existing is not null)
        {
            // Ensure the role membership is still set (idempotent re-run after manual changes).
            if (!await _users.IsInRoleAsync(existing, TicketingRoles.SuperAdmin))
            {
                await _users.AddToRoleAsync(existing, TicketingRoles.SuperAdmin);
                _logger.LogInformation("Re-attached SuperAdmin role to {Email}", email);
            }
            return;
        }

        var password = _config["Auth:SuperAdmin:Password"];
        if (string.IsNullOrWhiteSpace(password))
        {
            _logger.LogError(
                "Auth:SuperAdmin:Email is set ({Email}) but Auth:SuperAdmin:Password is not — " +
                "cannot create the initial SuperAdmin user. Set the password env var on first run.",
                email);
            return;
        }

        var user = new TicketingUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = "SuperAdmin",
        };

        var created = await _users.CreateAsync(user, password);
        if (!created.Succeeded)
        {
            _logger.LogError("Failed to create SuperAdmin {Email}: {Errors}",
                email, string.Join("; ", created.Errors.Select(e => e.Description)));
            return;
        }

        await _users.AddToRoleAsync(user, TicketingRoles.SuperAdmin);
        _logger.LogInformation("Seeded SuperAdmin user {Email}", email);
    }
}
