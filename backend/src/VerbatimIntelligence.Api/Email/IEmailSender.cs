namespace VerbatimIntelligence.Api.Email;

public interface IEmailSender
{
    Task SendAsync(string recipient, string subject, string textBody, CancellationToken cancellationToken);
}