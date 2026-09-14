using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription held by the caller, confirmed by POST /api/subscriptions and listed by GET /api/my-subscriptions.
/// </summary>
public class SubscriptionDto
{
    /// <summary>Billing-provider numeric subscription id.</summary>
    public int Id { get; set; }

    /// <summary>App-supplied stable reference that ties the subscription to the eShop user and plan.</summary>
    public string Reference { get; set; } = string.Empty;

    /// <summary>Lifecycle state reported by the billing provider (e.g. "active").</summary>
    public string State { get; set; } = string.Empty;

    public int ProductId { get; set; }

    public string ProductHandle { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public string Currency { get; set; } = string.Empty;

    public int Interval { get; set; }

    public string IntervalUnit { get; set; } = string.Empty;

    public string PaymentCollectionMethod { get; set; } = string.Empty;

    public int CustomerId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    public DateTimeOffset? NextBillingDate { get; set; }
}
