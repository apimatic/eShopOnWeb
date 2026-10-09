using System.Security.Cryptography;
using System.Text;

namespace Microsoft.eShopWeb.SquareCheck.SignIn;

/// <summary>
/// One run's request for the merchant's approval: the Square sign-in page address and the
/// unguessable <c>state</c> value that ties Square's redirect back to this run.
/// </summary>
public sealed class AuthorizationRequest
{
    /// <summary>The single permission the tool asks for.</summary>
    public const string Scope = "MERCHANT_PROFILE_READ";

    private AuthorizationRequest(Uri signInPage, string state)
    {
        SignInPage = signInPage;
        State = state;
    }

    public Uri SignInPage { get; }
    public string State { get; }

    /// <param name="squareBaseUrl">The selected environment's Square base URL (from the SDK's server options).</param>
    public static AuthorizationRequest Create(string squareBaseUrl, string applicationId, string redirectUri)
    {
        var state = NewState();
        var query = string.Join("&",
            Param("client_id", applicationId),
            Param("response_type", "code"),
            Param("scope", Scope),
            Param("state", state),
            Param("redirect_uri", redirectUri));
        var signInPage = new Uri($"{squareBaseUrl.TrimEnd('/')}/oauth2/authorize?{query}", UriKind.Absolute);
        return new AuthorizationRequest(signInPage, state);
    }

    /// <summary>Fixed-time comparison, so response timing reveals nothing about the expected value.</summary>
    public bool IsOwnState(string? candidate) =>
        candidate is not null
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(candidate), Encoding.UTF8.GetBytes(State));

    private static string NewState() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Param(string name, string value) => $"{name}={Uri.EscapeDataString(value)}";
}
