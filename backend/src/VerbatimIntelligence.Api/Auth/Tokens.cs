using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace VerbatimIntelligence.Api.Auth;

/// <summary>
/// Raw tokens travel (in a link, in a cookie) and are never stored; the
/// database only ever sees their SHA-256.
/// </summary>
public static class Tokens
{
    public static string CreateRaw() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string raw) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
}