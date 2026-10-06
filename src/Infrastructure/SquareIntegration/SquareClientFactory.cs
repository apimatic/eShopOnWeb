using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Square;
using Square.Core.Authentication.OAuth2;
using Square.Core.Authentication.OAuth2.AuthorizationCode;
using Square.Core.Configuration;

namespace Microsoft.eShopWeb.Infrastructure.SquareIntegration;

/// <summary>
/// Builds <see cref="SquareClient"/> instances over the named "Square" <see cref="HttpClient"/>.
/// Options are built here, once per client; clients are long-lived (see <see cref="SquareClientHolder"/>).
/// </summary>
public sealed class SquareClientFactory
{
    public const string HttpClientName = "Square";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<SquareSettings> _settings;
    private readonly ILoggerFactory _loggerFactory;
    private readonly TimeProvider _timeProvider;

    public SquareClientFactory(
        IHttpClientFactory httpClientFactory,
        IOptions<SquareSettings> settings,
        ILoggerFactory loggerFactory,
        TimeProvider timeProvider)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _loggerFactory = loggerFactory;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Creates a client. With a token strategy, every operation that needs the merchant's token gets it
    /// from that strategy; without one, only unauthenticated operations (the OAuth token endpoint) work.
    /// </summary>
    public SquareClient Create(IOAuth2RefreshableTokenStrategy<OAuth2AuthorizationCodeCredentials>? tokenStrategy)
    {
        var settings = _settings.Value;
        var options = new SquareClientOptions
        {
            Environment = settings.ServerEnvironment,
            TimeProvider = _timeProvider,
            // Per-attempt bound; the whole call is bounded by the caller's CancellationToken deadline.
            Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(15) },
            // Assigned explicitly so the SDK's log environment variable can never switch body logging on.
            Logging = new LoggingOptions
            {
                LoggerFactory = _loggerFactory,
                LogRequestBody = false,
                LogRequestHeaders = false,
                LogResponseHeaders = false,
            },
        };

        if (tokenStrategy is not null)
        {
            options.Oauth2 = new OAuth2AuthorizationCodeCredentials
            {
                ClientId = settings.ApplicationId!,
                ClientSecret = settings.ApplicationSecret,
                RedirectUri = settings.RedirectUri!,
                Scope = SquareConstants.OAuthScopes,
                // Sign-in happens through GET /api/square/connect + callback, never inside an API call.
                PromptForAuthorizationCode = (_, _) => Task.FromException<string>(SquareTokenSource.NotConnected()),
            };
            options.Oauth2TokenStrategy = tokenStrategy;
        }

        return new SquareClient(_httpClientFactory.CreateClient(HttpClientName), options);
    }
}
