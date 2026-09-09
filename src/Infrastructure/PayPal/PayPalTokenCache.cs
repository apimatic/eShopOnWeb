using System;
using System.Threading;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Process-wide cache of the PayPal OAuth access token, shared across the (transient) typed
/// client instances. Registered as a singleton; the semaphore serialises token refreshes.
/// </summary>
public sealed class PayPalTokenCache
{
    public readonly SemaphoreSlim RefreshGate = new(1, 1);
    public string? AccessToken { get; set; }
    public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.MinValue;

    public bool IsValid => AccessToken is not null && DateTimeOffset.UtcNow < ExpiresAt;
}
