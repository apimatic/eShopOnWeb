using System;
using System.Threading;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PayPalAccessTokenCache
{
    private readonly object _gate = new();
    private string? _token;
    private DateTimeOffset _expiresAt;

    public bool TryGet(out string? token)
    {
        lock (_gate)
        {
            if (!string.IsNullOrEmpty(_token) && DateTimeOffset.UtcNow < _expiresAt)
            {
                token = _token;
                return true;
            }

            token = null;
            return false;
        }
    }

    public void Set(string token, TimeSpan lifetime)
    {
        lock (_gate)
        {
            _token = token;
            _expiresAt = DateTimeOffset.UtcNow.Add(lifetime);
        }
    }
}
