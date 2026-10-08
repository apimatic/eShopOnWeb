using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public class PaymentOptions
{
    /// <summary>
    /// The most time one API request may spend waiting on the payment provider, across every provider call it
    /// makes. Kept below the 30 second ceiling a caller may be kept waiting, leaving room for our own work.
    /// </summary>
    public TimeSpan ProviderTimeBudget { get; set; } = TimeSpan.FromSeconds(25);

    /// <summary>
    /// A claim still marked Processing after this long belongs to a request that died mid-call; it is then
    /// settled by re-sending it with its own idempotency key instead of blocking the order forever.
    /// </summary>
    public TimeSpan StaleClaimAfter { get; set; } = TimeSpan.FromMinutes(2);
}
