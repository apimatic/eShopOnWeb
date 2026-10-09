using Microsoft.eShopWeb.SquareCheck.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Square;
using Square.Core.Authentication.OAuth2;
using Square.Core.Authentication.OAuth2.AuthorizationCode;
using Square.Core.Configuration;
using Square.Servers;

namespace Microsoft.eShopWeb.SquareCheck.SquareAccess;

/// <summary>
/// Builds the two Square SDK clients a run needs, over one shared <see cref="HttpClient"/>:
/// one for exchanging the sign-in code (that call sends no credential), and one that carries
/// the access token the exchange produced.
/// </summary>
public sealed class SquareClientFactory
{
    /// <summary>Bound on one HTTP attempt. Whole-call bounds are the caller's cancellation token.</summary>
    public static readonly TimeSpan PerAttemptTimeout = TimeSpan.FromSeconds(15);

    private readonly HttpClient _httpClient;
    private readonly SquareConnectionSettings _settings;
    private readonly ServerOptions _servers = new();

    public SquareClientFactory(HttpClient httpClient, SquareConnectionSettings settings)
    {
        _httpClient = httpClient;
        _settings = settings;
    }

    /// <summary>The selected environment's Square base URL, as the SDK defines it.</summary>
    public string BaseUrl =>
        _settings.Environment == ServerEnvironment.Sandbox
            ? _servers.Default.Sandbox.BaseUrl
            : _servers.Default.Production.BaseUrl;

    /// <summary>A client with no credential configured — for <c>OAuth.ObtainToken</c>, which sends none.</summary>
    public SquareClient CreateForTokenExchange() => new(_httpClient, BaseOptions());

    /// <summary>A client that authenticates every call with <paramref name="accessToken"/>.</summary>
    public SquareClient CreateSignedIn(string accessToken)
    {
        var options = BaseOptions();
        // The sign-in already happened (once, outside the SDK's retry pipeline). The token strategy hands
        // the client that token; it never prompts and never refreshes — the tool keeps nothing between runs.
        options.Oauth2 = new OAuth2AuthorizationCodeCredentials
        {
            ClientId = _settings.ApplicationId,
            ClientSecret = _settings.ApplicationSecret,
            RedirectUri = _settings.RedirectUri,
            Scope = SignIn.AuthorizationRequest.Scope,
            Pkce = null,
            PromptForAuthorizationCode = static (_, _) =>
                throw new InvalidOperationException("SquareCheck signs in once per run; Square asked for a second sign-in."),
        };
        options.Oauth2TokenStrategy = new SignedInTokenStrategy(new OAuthTokenRefreshable
        {
            AccessToken = accessToken,
            TokenType = "bearer",
        });
        return new SquareClient(_httpClient, options);
    }

    private SquareClientOptions BaseOptions() => new()
    {
        Environment = _settings.Environment,
        Server = _servers,
        Retry = RetryOptions.Default() with { Timeout = PerAttemptTimeout },
        // Assigned explicitly so the SQUARECLIENT_LOG environment variable cannot switch on body logging:
        // the token exchange carries the application secret and the authorization code.
        Logging = new LoggingOptions { LoggerFactory = NullLoggerFactory.Instance, LogRequestBody = false },
    };

    /// <summary>One <see cref="HttpClient"/> for the whole run.</summary>
    public static HttpClient CreateHttpClient() => new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    })
    {
        // Per-attempt backstop below the SDK's own per-attempt timeout window.
        Timeout = TimeSpan.FromSeconds(20),
    };
}

/// <summary>Supplies the access token obtained at sign-in. Never prompts, never refreshes.</summary>
internal sealed class SignedInTokenStrategy(OAuthTokenRefreshable token)
    : IOAuth2RefreshableTokenStrategy<OAuth2AuthorizationCodeCredentials>
{
    public Task<OAuthTokenRefreshable> GetToken(OAuth2AuthorizationCodeCredentials credentials, CancellationToken cancellationToken) =>
        Task.FromResult(token);

    public Task<OAuthTokenRefreshable?> TryRefreshToken(
        OAuth2AuthorizationCodeCredentials credentials, string refreshToken, CancellationToken cancellationToken) =>
        Task.FromResult<OAuthTokenRefreshable?>(null);
}
