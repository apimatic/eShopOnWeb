using System;

namespace Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

/// <summary>
/// A subscription owned by an eShopOnWeb user, mirrored from Maxio Advanced Billing.
/// </summary>
public class SubscriptionPurchase
{
    public int SubscriptionId { get; init; }

    /// <summary>
    /// Maxio subscription state (active, trialing, past_due, canceled, ...).
    /// </summary>
    public string State { get; init; } = string.Empty;

    public string PlanHandle { get; init; } = string.Empty;

    public string PlanName { get; init; } = string.Empty;

    /// <summary>
    /// Price of the subscribed product price point expressed in the smallest currency unit.
    /// </summary>
    public int ProductPriceInCents { get; init; }

    public string Currency { get; init; } = "USD";

    /// <summary>
    /// Date the current billing period ends (also the next renewal date).
    /// </summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>
    /// Date of the next billing assessment.
    /// </summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    public DateTimeOffset? ActivatedAt { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public int BalanceInCents { get; init; }

    /// <summary>
    /// True when the call resolved to an existing active subscription rather than creating a new one.
    /// </summary>
    public bool AlreadySubscribed { get; init; }
}
