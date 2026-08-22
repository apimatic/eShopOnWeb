using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public class PayPalAccessTokenCache
{
    private readonly ConcurrentDictionary<string, (string Token, DateTimeOffset ExpiresAt)> _tokens = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<string> GetOrCreateAsync(string cacheKey, Func<CancellationToken, Task<(string Token, int ExpiresInSeconds)>> factory, CancellationToken cancellationToken)
    {
        if (TryGet(cacheKey, out var cached))
        {
            return cached;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (TryGet(cacheKey, out cached))
            {
                return cached;
            }

            var created = await factory(cancellationToken);
            var lifetime = Math.Max(created.ExpiresInSeconds - 60, 30);
            _tokens[cacheKey] = (created.Token, DateTimeOffset.UtcNow.AddSeconds(lifetime));
            return created.Token;
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool TryGet(string cacheKey, out string token)
    {
        token = string.Empty;
        if (_tokens.TryGetValue(cacheKey, out var entry) && entry.ExpiresAt > DateTimeOffset.UtcNow)
        {
            token = entry.Token;
            return true;
        }

        return false;
    }
}
