using System;
using System.Threading;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class PayPalAccessTokenCache
{
    public SemaphoreSlim Gate { get; } = new(1, 1);
    public string? AccessToken { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
