using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace MaxioAdvancedBilling.Core.Authentication.OAuth2;

internal sealed class OAuth2Scheme<TCredentials> : IRevocableAuthScheme
{
    private readonly Func<TCredentials> _credFactory;
    private readonly IOAuth2TokenStrategy<TCredentials> _strategy;
    private volatile OAuthToken? _cachedToken;
    private readonly AsyncLock _lock = new();

    public OAuth2Scheme(Func<TCredentials> credFactory, IOAuth2TokenStrategy<TCredentials> strategy)
    {
        _credFactory = credFactory;
        _strategy = strategy;
    }

    public async ValueTask Apply(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await GetAndCacheToken(cancellationToken).ConfigureAwait(false);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
    }

    // Volatile write only — does not take _lock. See IRevocableAuthScheme remarks for the
    // concurrency contract: a concurrent in-flight fetch may overwrite this null with a freshly
    // issued token, which is correct because that token post-dates the invalidation.
    public void Invalidate() => _cachedToken = null;

    private async ValueTask<OAuthToken> GetAndCacheToken(CancellationToken ct)
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
            _cachedToken = await _strategy.GetToken(credentials, ct).ConfigureAwait(false);
            return _cachedToken;
        }
    }
}

internal static class OAuth2SchemeExtensions
{
    extension<TCredentials>(OAuth2Scheme<TCredentials>)
    {
        public static IAuthScheme Create(
            TCredentials? credentials,
            IOAuth2TokenStrategy<TCredentials> strategy) =>
            credentials is not null
                ? new OAuth2Scheme<TCredentials>(() => credentials, strategy)
                : NoneAuthScheme.Instance;
    }
}
