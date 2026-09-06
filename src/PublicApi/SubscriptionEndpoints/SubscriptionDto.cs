using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A shopper's enrollment on a <see cref="SubscriptionPlanDto"/>.
/// </summary>
public class SubscriptionDto
{
    /// <summary>Identifier of the subscription in the billing system of record.</summary>
    public int Id { get; set; }

    /// <summary>Lifecycle state, for example "active".</summary>
    public string State { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public string? PlanHandle { get; set; }

    public string? PlanName { get; set; }

    /// <summary>Amount billed each period.</summary>
    public decimal Price { get; set; }

    public int Interval { get; set; }

    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>Human readable billing cadence, for example "1 month".</summary>
    public string BillingPeriod { get; set; } = string.Empty;

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When the subscription is next billed.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>
    /// How the subscription is paid for, for example "remittance" (invoiced) or "automatic"
    /// (charged to a payment method on file).
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>Reference this application uses to identify the shopper in the billing system.</summary>
    public string? CustomerReference { get; set; }

    public string? CustomerEmail { get; set; }
}
