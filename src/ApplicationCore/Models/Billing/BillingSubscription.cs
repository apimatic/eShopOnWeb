using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Billing;

/// <summary>
/// A subscription as the billing provider currently sees it. The provider is the system of record.
/// </summary>
public class BillingSubscription
{
    public int Id { get; set; }

    /// <summary>
    /// The provider's lifecycle state, e.g. active, on_hold, canceled.
    /// </summary>
    public string State { get; set; } = string.Empty;

    public int CustomerId { get; set; }
    public string? CustomerReference { get; set; }
    public string? CustomerEmail { get; set; }

    public int ProductId { get; set; }
    public string? ProductHandle { get; set; }
    public string? ProductName { get; set; }

    /// <summary>
    /// The recurring product price in the site currency (e.g. 299.00), not in minor units.
    /// </summary>
    public decimal ProductPrice { get; set; }

    public long ProductPriceInCents { get; set; }
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// The outstanding balance in the site currency (e.g. 12.50), not in minor units.
    /// </summary>
    public decimal Balance { get; set; }

    public long BalanceInCents { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>
    /// When the customer is next billed - the "next billing date" shown after a successful subscribe.
    /// </summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public DateTimeOffset? DelayedCancelAt { get; set; }
    public bool CancelAtEndOfPeriod { get; set; }
}
