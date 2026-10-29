namespace RecipePlanner.Api.Email;

/// <summary>When an account was last sent each kind of email (see EmailCooldowns)</summary>
public class EmailCooldown
{
    public string UserId { get; set; } = null!;
    public string Kind { get; set; } = null!;
    public DateTimeOffset LastSentAt { get; set; }
}
