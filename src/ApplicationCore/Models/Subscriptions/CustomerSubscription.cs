using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;

/// <summary>
/// A shopper's subscription as recorded in Maxio.
/// </summary>
public class CustomerSubscription
{
    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public string? ProductHandle { get; set; }
    public string? ProductName { get; set; }
    public long PriceInCents { get; set; }
    public DateTime? ActivatedAt { get; set; }

    /// <summary>
    /// End of the current recurring period, i.e. when the next billing occurs.
    /// </summary>
    public DateTime? NextBillingAt { get; set; }

    public bool? CancelAtEndOfPeriod { get; set; }
}
