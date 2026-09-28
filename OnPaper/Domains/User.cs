using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace OnPaper.Models;

[Table("users")]
public class User
{
    [Column("id")]
    public int Id { get; set; }

    [Column("email")]
    public required string Email { get; set; }

    [Column("password")]
    public required string Password { get; set; }

    [Column("email_confirmed")]
    public bool EmailConfirmed { get; set; }

    [Column("email_confirmation_token")]
    public string? EmailConfirmationToken { get; set; }

    [Column("email_confirmation_token_expires_at")]
    public DateTimeOffset? EmailConfirmationTokenExpiresAt { get; set; }
}
