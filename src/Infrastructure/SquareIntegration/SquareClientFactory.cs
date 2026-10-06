using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Square;
using Square.Core.Authentication.OAuth2;
using Square.Core.Authentication.OAuth2.AuthorizationCode;
using Square.Core.Configuration;
using Square.Servers;

namespace Microsoft.eShopWeb.Infrastructure.SquareIntegration;

/// <summary>
/// Builds <see cref="SquareClient"/> instances over the named, factory-managed <see cref="HttpClient"/>.
/// Every client gets the same per-attempt timeout, the host's logger (which also keeps the SDK's
/// log environment variable from switching body logging on), and request-body logging off: the bodies in
/// scope carry the app secret, OAuth codes/tokens and shoppers' gift messages.
/// </summary>
public sealed class SquareClientFactory
{
    public const string HttpClientName = "Square";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SquareSettings _settings;
    private readonly ILoggerFactory _loggerFactory;
    private readonly TimeProvider _timeProvider;

    public SquareClientFactory(IHttpClientFactory httpClientFactory, IOptions<SquareSettings> settings,
        ILoggerFactory loggerFactory, TimeProvider timeProvider)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
        _loggerFactory = loggerFactory;
        _timeProvider = timeProvider;
    }

    /// <summary>The Square page a merchant opens to sign in and approve the shop (same server group as the API).</summary>
    public string AuthorizationEndpoint
    {
        get
        {
            var servers = new ServerOptions();
            var baseUrl = _settings.ServerEnvironment == ServerEnvironment.Production
                ? servers.Default.Production.BaseUrl
                : servers.Default.Sandbox.BaseUrl;
            return baseUrl.TrimEnd('/') + "/oauth2/authorize";
        }
    }

    /// <summary>A client with no merchant credentials, for the OAuth token endpoint (which takes the app secret in its body).</summary>
    public SquareClient CreateAppClient() => Create(oauth2: null, strategy: null);

    /// <summary>A client that authenticates every call with whatever token <paramref name="strategy"/> supplies.</summary>
    public SquareClient CreateMerchantClient(IOAuth2RefreshableTokenStrategy<OAuth2AuthorizationCodeCredentials> strategy) =>
        Create(OAuthCredentials(), strategy);

    /// <summary>A client bound to one specific access token (used to identify a merchant right after sign-in).</summary>
    public SquareClient CreateClientForAccessToken(string accessToken) =>
        Create(OAuthCredentials(), new FixedTokenStrategy(accessToken));

    private SquareClient Create(OAuth2AuthorizationCodeCredentials? oauth2,
        IOAuth2RefreshableTokenStrategy<OAuth2AuthorizationCodeCredentials>? strategy)
    {
        var options = new SquareClientOptions
        {
            Environment = _settings.ServerEnvironment,
            Retry = RetryOptions.Default() with { Timeout = SquareTimeouts.PerAttempt },
            Logging = new LoggingOptions
            {
                LoggerFactory = _loggerFactory,
                LogRequestBody = false,
                LogRequestHeaders = false,
                LogResponseHeaders = false,
            },
            TimeProvider = _timeProvider,
            Oauth2 = oauth2,
            Oauth2TokenStrategy = strategy,
        };
        return new SquareClient(_httpClientFactory.CreateClient(HttpClientName), options);
    }

    // The SDK requires credentials for its OAuth2 scheme; the token itself always comes from our strategy, so the
    // interactive prompt is never used — a host cannot block on a browser.
    private OAuth2AuthorizationCodeCredentials OAuthCredentials() => new()
    {
        ClientId = _settings.ApplicationId!,
        ClientSecret = _settings.ApplicationSecret,
        RedirectUri = _settings.RedirectUri!,
        Pkce = null,
        PromptForAuthorizationCode = (_, _) => throw new SquareIntegrationException(SquareFailureKind.NotConnected,
            "The Square account is not connected. An administrator must connect it through GET /api/square/connect."),
    };

    private sealed class FixedTokenStrategy : IOAuth2RefreshableTokenStrategy<OAuth2AuthorizationCodeCredentials>
    {
        private readonly string _accessToken;

        public FixedTokenStrategy(string accessToken) => _accessToken = accessToken;

        public Task<OAuthTokenRefreshable> GetToken(OAuth2AuthorizationCodeCredentials credentials, CancellationToken cancellationToken) =>
            Task.FromResult(new OAuthTokenRefreshable { AccessToken = _accessToken, TokenType = "bearer" });

        public Task<OAuthTokenRefreshable?> TryRefreshToken(OAuth2AuthorizationCodeCredentials credentials, string refreshToken,
            CancellationToken cancellationToken) => Task.FromResult<OAuthTokenRefreshable?>(null);
    }
}
