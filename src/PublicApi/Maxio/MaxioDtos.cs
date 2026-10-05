using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// A subscribable plan — a Maxio product in the configured product family.
/// </summary>
public sealed class MaxioPlanDto
{
    /// <summary>The Maxio product API handle; the value a client passes to POST /api/subscriptions.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price in integer cents.</summary>
    public long? PriceInCents { get; set; }

    /// <summary>Billing interval (e.g. 1 with IntervalUnit "month").</summary>
    public int? Interval { get; set; }

    /// <summary>Billing interval unit wire value ("month" or "day").</summary>
    public string? IntervalUnit { get; set; }

    /// <summary>True when Maxio requires a payment method to sign up for this plan.</summary>
    public bool RequiresPaymentMethod { get; set; }
}

/// <summary>
/// A subscription in Maxio Advanced Billing, as seen by its eShopOnWeb shopper.
/// </summary>
public sealed class MaxioSubscriptionDto
{
    public int MaxioSubscriptionId { get; set; }

    /// <summary>The deterministic reference this app created the subscription under.</summary>
    public string Reference { get; set; } = string.Empty;

    /// <summary>Maxio subscription state wire value (e.g. "active", "trialing").</summary>
    public string State { get; set; } = string.Empty;

    public string? PlanHandle { get; set; }

    public string? PlanName { get; set; }

    /// <summary>Recurring price in integer cents currently billed for this subscription.</summary>
    public long? PriceInCents { get; set; }

    public int? Interval { get; set; }

    public string? IntervalUnit { get; set; }

    /// <summary>End of the current billing period — the next billing date.</summary>
    public DateTimeOffset? NextBillingDateUtc { get; set; }

    public DateTimeOffset? ActivatedAtUtc { get; set; }

    public string? Currency { get; set; }

    /// <summary>True when this call created the subscription; false when an existing one was returned idempotently.</summary>
    public bool Created { get; set; }
}