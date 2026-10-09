using Microsoft.eShopWeb.SquareCheck.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Square;
using Square.Core.Authentication.OAuth2.AuthorizationCode;
using Square.Core.Configuration;

namespace Microsoft.eShopWeb.SquareCheck.SquareAccess;

/// <summary>
/// Builds the Square SDK client for one run: OAuth 2.0 authorization-code sign-in, no persisted token.
/// </summary>
public static class SquareClientFactory
{
    /// <summary>The permission the sign-in asks for (given by Square's account team).</summary>
    public const string Scope = "MERCHANT_PROFILE_READ";

    /// <summary>
    /// Per-attempt bound on a single HTTP exchange with Square. The SDK's own per-attempt timeout is
    /// disabled because the interactive sign-in runs inside the first call's attempt.
    /// </summary>
    public static readonly TimeSpan HttpAttemptTimeout = TimeSpan.FromSeconds(20);

    public static HttpClient CreateHttpClient(HttpMessageHandler? handler = null) =>
        new(handler ?? new SocketsHttpHandler(), disposeHandler: true) { Timeout = HttpAttemptTimeout };

    public static SquareClient Create(
        SquareSettings settings,
        string state,
        AuthorizationCodePrompt prompt,
        HttpClient httpClient)
    {
        var options = new SquareClientOptions
        {
            Environment = settings.Environment,
            Oauth2 = new OAuth2AuthorizationCodeCredentials
            {
                ClientId = settings.ApplicationId,
                ClientSecret = settings.ApplicationSecret,
                RedirectUri = settings.RedirectUri.OriginalString,
                Scope = Scope,
                State = state,
                // Square's "code flow": the code is exchanged with client_id + client_secret. With PKCE on
                // as well, the exchange would carry both a secret and a verifier, which is neither flow.
                Pkce = null,
                PromptForAuthorizationCode = prompt,
            },
            Retry = RetryOptions.Default() with
            {
                // The prompt (up to minutes) runs inside an attempt: a per-attempt timeout would cut the
                // sign-in short. Each phase is bounded by a CancellationToken deadline instead, and each
                // HTTP exchange by HttpClient.Timeout.
                Timeout = null,
                MaxRetries = 2,
            },
            Logging = new LoggingOptions
            {
                // Pinned explicitly so SQUARECLIENT_LOG cannot switch on logging of the token exchange
                // (which carries client_secret and the authorization code) from outside the code.
                LoggerFactory = NullLoggerFactory.Instance,
                LogRequestBody = false,
                LogRequestHeaders = false,
                LogResponseHeaders = false,
            },
        };

        return new SquareClient(httpClient, options);
    }
}
