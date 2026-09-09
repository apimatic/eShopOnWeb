using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace MaxioAdvancedBilling.Core.Authentication.OAuth2;

internal sealed class OAuth2RefreshableScheme<TCredentials> : IRevocableAuthScheme
{
    private readonly Func<TCredentials> _credFactory;
    private readonly IOAuth2RefreshableTokenStrategy<TCredentials> _strategy;
    private volatile OAuthTokenRefreshable? _cachedToken;
    private readonly AsyncLock _lock = new();

    public OAuth2RefreshableScheme(
        Func<TCredentials> credFactory,
        IOAuth2RefreshableTokenStrategy<TCredentials> strategy)
    {
        _credFactory = credFactory;
        _strategy = strategy;
    }

    public async ValueTask Apply(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await GetAndCacheToken(cancellationToken).ConfigureAwait(false);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
    }

    // Drops the access token only; the refresh token (if any) is preserved on the next acquisition
    // path because GetAndCacheToken first attempts TryRefreshToken when a refresh token is present.
    // After Invalidate(), the next call has no cached token and no in-memory refresh token, so it
    // falls through to GetToken — which is the desired behaviour when the server has revoked the
    // session.
    //
    // Volatile write only — does not take _lock. See IRevocableAuthScheme remarks for the
    // concurrency contract.
    public void Invalidate() => _cachedToken = null;

    private async ValueTask<OAuthTokenRefreshable> GetAndCacheToken(CancellationToken ct)
    {
        var cached = _cachedToken;
        if (cached is not null && !cached.IsExpired(DateTimeOffset.UtcNow))
            return cached;

        using (await _lock.LockAsync(ct).ConfigureAwait(false))
        {
            cached = _cachedToken;
            if (cached is not null && !cached.IsExpired(DateTimeOffset.UtcNow))
                return cached;

            var credentials = _credFactory();

            // Attempt refresh if we have a stale token with a refresh token
            if (cached?.RefreshToken is not null)
            {
                var refreshed = await _strategy.TryRefreshToken(credentials, cached.RefreshToken, ct).ConfigureAwait(false);
                if (refreshed is not null)
                {
                    // RFC 6749 §6: AS may omit refresh_token from the response;
                    // preserve the previously-issued refresh token in that case.
                    _cachedToken = refreshed.RefreshToken is null
                        ? refreshed with { RefreshToken = cached.RefreshToken }
                        : refreshed;
                    return _cachedToken;
                }
            }

            _cachedToken = await _strategy.GetToken(credentials, ct).ConfigureAwait(false);
            return _cachedToken;
        }
    }
}

internal static class OAuth2RefreshableSchemeExtensions
{
    extension<TCredentials>(OAuth2RefreshableScheme<TCredentials>)
    {
        public static IAuthScheme Create(
            TCredentials? credentials,
            IOAuth2RefreshableTokenStrategy<TCredentials> strategy) =>
            credentials is not null
                ? new OAuth2RefreshableScheme<TCredentials>(() => credentials, strategy)
                : NoneAuthScheme.Instance;
    }
}
