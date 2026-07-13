using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VerbatimIntelligence.Api.Tests;

/// <summary>
/// Signs a test client in the way a user does: request a magic link, read
/// the mail from Mailpit, verify the token. The client keeps the session
/// cookie afterwards.
/// </summary>
internal static partial class SignInFlow
{
    [GeneratedRegex("token=([A-Za-z0-9_-]+)")]
    private static partial Regex TokenPattern();

    public static async Task SignInAsync(HttpClient apiClient, Uri mailpitBaseAddress, string email)
    {
        var requested = await apiClient.PostAsJsonAsync("/api/auth/magic-link", new { email });
        Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);

        using var mailpit = new HttpClient { BaseAddress = mailpitBaseAddress };
        var inbox = await mailpit.GetFromJsonAsync<JsonElement>("api/v1/messages");
        var message = Assert.Single(
            inbox.GetProperty("messages").EnumerateArray(),
            m => m.GetProperty("To")[0].GetProperty("Address").GetString() == email);
        var detail = await mailpit.GetFromJsonAsync<JsonElement>(
            $"api/v1/message/{message.GetProperty("ID").GetString()}");
        var token = TokenPattern().Match(detail.GetProperty("Text").GetString()!).Groups[1].Value;

        var verified = await apiClient.PostAsJsonAsync("/api/auth/verify", new { token });
        Assert.Equal(HttpStatusCode.NoContent, verified.StatusCode);
    }
}