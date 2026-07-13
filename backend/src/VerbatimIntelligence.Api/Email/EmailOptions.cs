namespace VerbatimIntelligence.Api.Email;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public required string SmtpHost { get; init; }

    public required int SmtpPort { get; init; }

    public required string From { get; init; }

    // Dev and tests run against Mailpit, plain SMTP. Production (Scaleway
    // TEM) requires STARTTLS and credentials.
    public bool UseStartTls { get; init; }

    public string? Username { get; init; }

    public string? Password { get; init; }
}