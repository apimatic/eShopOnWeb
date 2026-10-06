using System;
using System.Threading;
using System.Threading.Tasks;
using Square;
using Square.Core.Authentication.OAuth2;
using Square.Core.Authentication.OAuth2.AuthorizationCode;

namespace Microsoft.eShopWeb.Infrastructure.SquareIntegration;

/// <summary>
/// Holds the long-lived <see cref="SquareClient"/> that acts for the current merchant. The SDK caches the access
/// token per client instance, so the client is rebuilt when the connected merchant changes.
/// </summary>
public sealed class SquareClientProvider
{
    /// <summary>The SDK re-asks our strategy at least this often, so token changes made elsewhere are picked up.</summary>
    private const int MaxTokenCacheSeconds = 300;

    private readonly SquareClientFactory _factory;
    private readonly SquareAccessTokenProvider _tokens;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();
    private volatile Holder? _holder;

    public SquareClientProvider(SquareClientFactory factory, SquareAccessTokenProvider tokens, TimeProvider clock)
    {
        _factory = factory;
        _tokens = tokens;
        _clock = clock;
    }

    public SquareClient Merchant
    {
        get
        {
            var generation = _tokens.Generation;
            var holder = _holder;
            if (holder is not null && holder.Generation == generation) return holder.Client;

            lock (_gate)
            {
                holder = _holder;
                if (holder is null || holder.Generation != generation)
                {
                    holder = new Holder(_factory.CreateMerchantClient(new StoredTokenStrategy(_tokens, _clock)), generation);
                    _holder = holder;
                }
                return holder.Client;
            }
        }
    }

    private sealed record Holder(SquareClient Client, long Generation);

    /// <summary>Feeds the SDK's OAuth2 scheme from <see cref="SquareAccessTokenProvider"/>; refresh is done there, not by the SDK.</summary>
    private sealed class StoredTokenStrategy : IOAuth2RefreshableTokenStrategy<OAuth2AuthorizationCodeCredentials>
    {
        private readonly SquareAccessTokenProvider _tokens;
        private readonly TimeProvider _clock;

        public StoredTokenStrategy(SquareAccessTokenProvider tokens, TimeProvider clock)
        {
            _tokens = tokens;
            _clock = clock;
        }

        public async Task<OAuthTokenRefreshable> GetToken(OAuth2AuthorizationCodeCredentials credentials, CancellationToken cancellationToken)
        {
            var credential = await _tokens.GetAsync(cancellationToken).ConfigureAwait(false);
            var lifetime = MaxTokenCacheSeconds;
            if (credential.ExpiresAt is { } expiresAt)
                lifetime = (int)Math.Clamp((expiresAt - _clock.GetUtcNow()).TotalSeconds, 2, MaxTokenCacheSeconds);

            return new OAuthTokenRefreshable
            {
                AccessToken = credential.AccessToken,
                TokenType = "bearer",
                ExpiresIn = lifetime,
            };
        }

        public async Task<OAuthTokenRefreshable?> TryRefreshToken(OAuth2AuthorizationCodeCredentials credentials, string refreshToken,
            CancellationToken cancellationToken) => await GetToken(credentials, cancellationToken).ConfigureAwait(false);
    }
}
