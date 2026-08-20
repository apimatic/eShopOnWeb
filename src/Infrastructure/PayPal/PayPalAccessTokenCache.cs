using System;
using System.Threading;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public sealed class PayPalAccessTokenCache
{
    public SemaphoreSlim Gate { get; } = new(1, 1);
    public string? AccessToken { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
