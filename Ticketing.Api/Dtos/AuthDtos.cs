using System.ComponentModel.DataAnnotations;

namespace Ticketing.Api.Dtos;

public class LoginRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = "";

    [Required]
    public string Password { get; set; } = "";
}

public record LoginResponse(
    string Token,
    DateTimeOffset ExpiresAt,
    string Email,
    string[] Roles);
