using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PayPalAccessTokenCache
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _token;
    private DateTimeOffset _expiresAt;

    public async Task<string> GetTokenAsync(Func<CancellationToken, Task<(string Token, DateTimeOffset ExpiresAt)>> factory, CancellationToken cancellationToken)
    {
        if (HasValidToken)
        {
            return _token!;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (HasValidToken)
            {
                return _token!;
            }

            var (token, expiresAt) = await factory(cancellationToken);
            _token = token;
            _expiresAt = expiresAt;
            return _token;
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool HasValidToken =>
        !string.IsNullOrEmpty(_token) && _expiresAt > DateTimeOffset.UtcNow.AddMinutes(1);
}
