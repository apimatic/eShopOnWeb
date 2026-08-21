using System;
using System.Threading;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PayPalTokenCache
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public string? AccessToken { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }

    public SemaphoreSlim Gate => _gate;

    public bool HasValidToken
        => !string.IsNullOrEmpty(AccessToken) && ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1);
}
