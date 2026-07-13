using System.Net.Http.Json;
using System.Text.Json;

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

using Microsoft.Extensions.Options;

using VerbatimIntelligence.Api.Email;

namespace VerbatimIntelligence.Api.Tests;

/// <summary>
/// Exercises the SMTP sender against a real Mailpit container and reads the
/// delivered message back through Mailpit's REST API — the same tooling the
/// dev stack and the e2e tests use.
/// </summary>
public sealed class SmtpEmailSenderTests : IAsyncLifetime
{
    private readonly IContainer _mailpit = new ContainerBuilder(
        "axllent/mailpit@sha256:5a49a77c5bdbe7c5474450b4f46348d09949df3695257729c93a30369382d4f6")
        .WithPortBinding(1025, true)
        .WithPortBinding(8025, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(
            request => request.ForPort(8025).ForPath("/readyz")))
        .Build();

    public Task InitializeAsync() => _mailpit.StartAsync();

    Task IAsyncLifetime.DisposeAsync() => _mailpit.DisposeAsync().AsTask();

    [Fact]
    public async Task SendAsync_DeliversTheMessageThroughSmtp()
    {
        var sender = new SmtpEmailSender(Options.Create(new EmailOptions
        {
            SmtpHost = _mailpit.Hostname,
            SmtpPort = _mailpit.GetMappedPublicPort(1025),
            From = "noreply@verbatim.test",
        }));

        await sender.SendAsync(
            recipient: "alice@example.test",
            subject: "Your sign-in link",
            textBody: "Open https://verbatim.test/auth/verify?token=abc to sign in.",
            cancellationToken: CancellationToken.None);

        using var http = new HttpClient();
        var inbox = await http.GetFromJsonAsync<JsonElement>(
            new Uri($"http://{_mailpit.Hostname}:{_mailpit.GetMappedPublicPort(8025)}/api/v1/messages"),
            CancellationToken.None);

        Assert.Equal(1, inbox.GetProperty("total").GetInt32());
        var message = inbox.GetProperty("messages")[0];
        Assert.Equal("alice@example.test", message.GetProperty("To")[0].GetProperty("Address").GetString());
        Assert.Equal("noreply@verbatim.test", message.GetProperty("From").GetProperty("Address").GetString());
        Assert.Equal("Your sign-in link", message.GetProperty("Subject").GetString());
    }
}