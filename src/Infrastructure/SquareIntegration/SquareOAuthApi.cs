using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Square;
using Square.Models;
using Square.Requests.OAuth;
using Square.Servers;

namespace Microsoft.eShopWeb.Infrastructure.SquareIntegration;

/// <summary>
/// Square's OAuth endpoints: the sign-in page address and the token endpoint (code exchange and refresh).
/// Uses its own client without the merchant token scheme — the token endpoint takes no bearer token.
/// </summary>
public sealed class SquareOAuthApi
{
    private const string GrantAuthorizationCode = "authorization_code";
    private const string GrantRefreshToken = "refresh_token";

    private readonly IOptions<SquareSettings> _settings;
    private readonly ILogger<SquareOAuthApi> _logger;
    private readonly Lazy<SquareClient> _client;

    public SquareOAuthApi(SquareClientFactory clientFactory, IOptions<SquareSettings> settings, ILogger<SquareOAuthApi> logger)
    {
        _settings = settings;
        _logger = logger;
        _client = new Lazy<SquareClient>(() => clientFactory.Create(tokenStrategy: null));
    }

    /// <summary>
    /// The Square page the merchant opens to sign in and approve the shop. Same host, path and query
    /// parameters the SDK's authorization-code flow uses (server group Default, <c>/oauth2/authorize</c>).
    /// </summary>
    public string BuildSignInUrl(string state)
    {
        var settings = _settings.Value;
        var defaults = new ServerOptions().Default;
        var baseUrl = settings.ServerEnvironment == ServerEnvironment.Production
            ? defaults.Production.BaseUrl
            : defaults.Sandbox.BaseUrl;

        var query = new List<KeyValuePair<string, string>>
        {
            new("client_id", settings.ApplicationId!),
            new("response_type", "code"),
            new("scope", SquareConstants.OAuthScopes),
            new("redirect_uri", settings.RedirectUri!),
            new("state", state),
        };
        return $"{baseUrl.TrimEnd('/')}/oauth2/authorize?" +
               string.Join("&", query.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
    }

    public Task<ObtainTokenResponse> ExchangeCodeAsync(string code, CancellationToken cancellationToken)
    {
        var settings = _settings.Value;
        return SquareCall.RunAsync("OAuth.ObtainToken (authorization_code)", ct => _client.Value.OAuth.ObtainToken(
            new ObtainTokenOperationRequest
            {
                Body = new ObtainTokenRequest
                {
                    ClientId = settings.ApplicationId!,
                    ClientSecret = settings.ApplicationSecret,
                    GrantType = GrantAuthorizationCode,
                    Code = code,
                    RedirectUri = settings.RedirectUri,
                },
            },
            cancellationToken: ct), _logger, cancellationToken);
    }

    public Task<ObtainTokenResponse> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var settings = _settings.Value;
        return SquareCall.RunAsync("OAuth.ObtainToken (refresh_token)", ct => _client.Value.OAuth.ObtainToken(
            new ObtainTokenOperationRequest
            {
                Body = new ObtainTokenRequest
                {
                    ClientId = settings.ApplicationId!,
                    ClientSecret = settings.ApplicationSecret,
                    GrantType = GrantRefreshToken,
                    RefreshToken = refreshToken,
                },
            },
            cancellationToken: ct), _logger, cancellationToken);
    }
}
