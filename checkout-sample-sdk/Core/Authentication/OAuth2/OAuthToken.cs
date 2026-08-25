using System;
using System.Text.Json.Serialization;

namespace PayPalServerSdk.Core.Authentication.OAuth2;

/// <summary>
/// Represents the token response issued by the authorization server for grant types that do not
/// support refresh tokens: <b>Client Credentials</b> (RFC 6749 §4.4.3 — SHOULD NOT include a
/// refresh token) and <b>Implicit</b> (RFC 6749 §4.2.2 — MUST NOT include a refresh token).
/// </summary>
/// <remarks>
/// Use <see cref="OAuthTokenRefreshable"/> instead for grant types whose token responses may
/// carry a refresh token (Authorization Code and Resource Owner Password Credentials).
/// <para>
/// If the server omits <c>expires_in</c> (it is RECOMMENDED but not required per RFC 6749 §5.1),
/// <see cref="IsExpired"/> will always return <c>false</c> so the token is never considered
/// expired by the client.
/// </para>
/// </remarks>
public record OAuthToken
{
    [JsonPropertyName("access_token")]
    public required string AccessToken { get; init; }

    [JsonPropertyName("token_type")]
    public required string TokenType { get; init; }

    /// <summary>
    /// Lifetime in seconds as reported by the server. RECOMMENDED but not required per RFC 6749 §5.1
    /// — may be <c>null</c> when the server omits it, in which case <see cref="IsExpired"/> always
    /// returns <c>false</c>.
    /// </summary>
    [JsonPropertyName("expires_in")]
    public int? ExpiresIn { get; init; }

    [JsonPropertyName("scope")]
    public string? Scope { get; init; }

    private const int ExpiryBufferSeconds = 30;

    // STJ runs this initializer in the parameterless ctor before init-only properties are
    // assigned. IsExpired short-circuits to false while ExpiresIn is null, so the half-built
    // window is never observed as expired.
    private readonly DateTimeOffset _receivedAt = DateTimeOffset.UtcNow;

    public bool IsExpired(DateTimeOffset timeNow) =>
        ExpiresIn is > 0 && timeNow >= _receivedAt.AddSeconds(ExpiresIn.Value - ExpiryBufferSeconds);
}
