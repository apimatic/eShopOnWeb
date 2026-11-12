using System;
using System.Collections.Concurrent;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// In-process claims that stop one shopper's identical subscribe request from running twice at the same time.
/// </summary>
public class SubscribeClaims
{
    private readonly ConcurrentDictionary<string, byte> _claims = new();

    public IDisposable? TryClaim(string user, string planHandle)
    {
        var key = $"{user.ToLowerInvariant()}|{planHandle.ToLowerInvariant()}";
        return _claims.TryAdd(key, 0) ? new Releaser(_claims, key) : null;
    }

    private sealed class Releaser : IDisposable
    {
        private readonly ConcurrentDictionary<string, byte> _claims;
        private readonly string _key;

        public Releaser(ConcurrentDictionary<string, byte> claims, string key)
        {
            _claims = claims;
            _key = key;
        }

        public void Dispose() => _claims.TryRemove(_key, out _);
    }
}
