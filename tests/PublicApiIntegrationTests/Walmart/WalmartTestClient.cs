using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using WalmartApIs;
using WalmartApIs.Core.Authentication.OAuth2;
using WalmartApIs.Core.Authentication.OAuth2.ClientCredentials;
using WalmartApIs.Core.Configuration;

namespace PublicApiIntegrationTests.Walmart;

/// <summary>Builds a <see cref="WalmartApIsClient"/> over a <see cref="StubHandler"/> with a fixed token.</summary>
internal static class WalmartTestClient
{
    public static WalmartApIsClient Create(StubHandler handler)
    {
        var options = new WalmartApIsClientOptions
        {
            WalletAuth = new OAuth2ClientCredentials { ClientId = "id", ClientSecret = "secret" },
            WalletAuthTokenStrategy = new FixedTokenStrategy(),
            // Retries off so transport-fault tests surface immediately and deterministically.
            Retry = RetryOptions.Disabled(),
        };
        return new WalmartApIsClient(new HttpClient(handler), options);
    }

    private sealed class FixedTokenStrategy : IOAuth2TokenStrategy<OAuth2ClientCredentials>
    {
        public Task<OAuthToken> GetToken(OAuth2ClientCredentials credentials, CancellationToken cancellationToken) =>
            Task.FromResult(new OAuthToken { AccessToken = "test-token", TokenType = "Bearer" });
    }
}
